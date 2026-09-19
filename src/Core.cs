using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace MfgEnabler {
 [DataContract] public class Entry {
  [DataMember] public string Name;
  [DataMember] public string Original;
  [DataMember] public string Installed;
  [DataMember] public string Backup;
  [DataMember] public string Previous;
  [DataMember] public string UpgradeBackup;
 }
 [DataContract] public class Journal {
  [DataMember] public string Target;
  [DataMember] public string Phase;
  [DataMember] public string Proxy;
  [DataMember] public string RuntimeVersion;
  [DataMember] public string RuntimeRevision;
  [DataMember] public List<Entry> Files = new List<Entry>();
 }
 public static class Disk {
  public static string HashBytes(byte[] bytes) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
  public static string Hash(string path) { if(!File.Exists(path)) return null; using(var s=File.OpenRead(path)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
  public static void Safe(string path) { var p=Path.GetFullPath(path); while(!String.IsNullOrEmpty(p)) { if((Directory.Exists(p)||File.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("심볼릭 링크/정션 경로는 지원하지 않습니다: "+p); p=Path.GetDirectoryName(p); } }
  public static void Write(string path, byte[] bytes) { Safe(path); string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp"; try { using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { f.Write(bytes,0,bytes.Length); f.Flush(true); } if(File.Exists(path)) File.Replace(tmp,path,null); else File.Move(tmp,path); } finally { if(File.Exists(tmp)) File.Delete(tmp); } }
  public static T Read<T>(string p) { using(var f=File.OpenRead(p)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(f); }
  public static void Save<T>(string p,T value) { using(var m=new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(m,value); Write(p,m.ToArray()); } }
 }
 public static class Payload {
  public const string Revision="196fcb61ef414992a6bef2d237ff609aab09f0d9";
  public const string BaselineVersion="0.3.4";
  public static readonly string[] Proxies={"version.dll","winmm.dll","dinput8.dll","dxgi.dll","d3d12.dll","dbghelp.dll"};
  public static RuntimePackage Baseline() {
   using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.lock"))) {
    var files=r.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim('\uFEFF').Split('|')).Select(x=>new PackageFile{Name=x[0],Path=x[1],Hash=x[2],Blob=x[3]}).ToList();
    return new RuntimePackage{Revision=Revision,Version=BaselineVersion,Channel=Updates.Channel,Files=files};
   }
  }
  public static Dictionary<string,string[]> Specs() { return Updates.Current().Files.ToDictionary(f=>f.Name,f=>new[]{f.Name,f.Path,f.Hash,f.Blob}); }
  static string cacheOverride;
  public static string Cache {get{return cacheOverride??Updates.DirectoryFor(Updates.Current());}set{cacheOverride=value;}}
   public static void Ensure(string proxy,Action<string> progress) {
    var package=Updates.Current();
    package=Updates.Prepare(package,progress);
    foreach(var name in new[]{proxy,"dlssg_sm86.ini"})Updates.Ensure(package,name,progress,cacheOverride);
   }
 }

 public class Installer {
  public readonly string Target;
  public readonly string Store;
  public Action<int> Fault;
   public readonly bool IsSpoof;
  string StateFile { get { return Path.Combine(Store,"state.json"); } }
   public Installer(string target,bool spoof=false) { Target=Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar); IsSpoof=spoof; Store=spoof?Path.Combine(Target,".mfg-enabler","nvapi"):Path.Combine(Target,".mfg-enabler"); }
   void MigrateStore() {
    try {
     string legacy=Path.Combine(Target,".mfg-enabler","spoof5080");
     if(!string.Equals(Store,legacy,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(legacy)&&!Directory.Exists(Store))Directory.Move(legacy,Store);
    } catch { }
   }
   public Journal Load() {
    if(IsSpoof)MigrateStore();
    Disk.Safe(Store); if(!File.Exists(StateFile)) return null; var j=Disk.Read<Journal>(StateFile);
   if(j==null || !String.Equals(j.Target,Target,StringComparison.OrdinalIgnoreCase) || j.Files==null) throw new IOException("복구 기록이 유효하지 않습니다.");
    var expected=IsSpoof?new[]{"nvapi64.dll"}:new[]{j.Proxy,"dlssg_sm86.ini"};
    bool proxyOk=IsSpoof?j.Proxy=="nvapi64.dll":(Payload.Proxies.Contains(j.Proxy)||j.Proxy=="winhttp.dll");
    if(!proxyOk || j.Files.Count!=expected.Length || j.Files.Any(e=>e==null) || j.Files.Select(e=>e.Name).Distinct().Count()!=expected.Length || j.Files.Any(e=>!expected.Contains(e.Name))) throw new IOException("복구 파일 목록이 유효하지 않습니다.");
   foreach(var e in j.Files) { if(e.Backup==null || e.Backup!=Path.GetFileName(e.Backup) || e.Backup.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || e.Backup=="." || e.Backup=="..") throw new IOException("잘못된 백업 경로"); Disk.Safe(Path.Combine(Store,e.Backup)); Disk.Safe(Path.Combine(Target,e.Name)); }
   foreach(var e in j.Files){if(e.Previous!=null&&(e.Previous.Length!=64||e.UpgradeBackup==null||e.UpgradeBackup!=Path.GetFileName(e.UpgradeBackup)||e.UpgradeBackup.IndexOfAny(Path.GetInvalidFileNameChars())>=0))throw new IOException("잘못된 업데이트 복구 기록");if(e.UpgradeBackup!=null)Disk.Safe(Path.Combine(Store,e.UpgradeBackup));}
   if(!new[]{"installing","enabled","restoring","disabled","updating"}.Contains(j.Phase)) throw new IOException("알 수 없는 복구 상태"); return j;
  }
  public string Status() { try { var j=Load(); if(j==null||j.Phase=="disabled") return "미적용"; if(j.Phase!="enabled") return "복구 필요"; return j.Files.All(e=>Disk.Hash(Path.Combine(Target,e.Name))==e.Installed)?"적용됨":"파일 변경 감지"; } catch { return "기록 확인 필요"; } }
  public static void CheckRunning(string target) {
   foreach(var p in Process.GetProcesses()) using(p) { try { string path=p.MainModule.FileName; if(path.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("게임 또는 해당 폴더의 프로그램을 종료하세요: "+p.ProcessName); } catch(System.ComponentModel.Win32Exception) {} catch(InvalidOperationException) {} }
  }
  public void Enable(string proxy,string payload) {
   if(IsSpoof) throw new IOException("RTX 5080 설치는 EnableSpoof를 사용하세요.");
   if(!Payload.Proxies.Contains(proxy)) throw new IOException("지원하지 않는 프록시"); Disk.Safe(Target); CheckRunning(Target);
   var old=Load(); if(old!=null&&old.Phase!="disabled") throw new IOException("먼저 기존 적용을 해제하거나 복구하세요.");

   foreach(var p in Payload.Proxies) if(File.Exists(Path.Combine(Target,p)) && Disk.Hash(Path.Combine(Target,p))==Payload.Specs()[p][2]) throw new IOException("수동 설치된 DLSSG 프록시가 있습니다: "+p+". 먼저 수동 설치를 정리하세요.");
   var package=Updates.Current();
   var files=new[]{proxy,"dlssg_sm86.ini"}.ToDictionary(n=>n,n=>File.ReadAllBytes(Updates.FileFor(package,n,payload)));
   foreach(var pair in files)Updates.ValidateInstallBytes(package,pair.Key,pair.Value);
   InstallFiles(proxy,files);
  }
   void InstallFiles(string proxy,Dictionary<string,byte[]> files,RuntimePackage package=null) {
   Disk.Safe(Target);CheckRunning(Target);var old=Load();
   if(old!=null&&old.Phase!="disabled")throw new IOException("먼저 해당 기능을 해제하거나 복구하세요.");
    Directory.CreateDirectory(Store); Disk.Safe(Store); var j=new Journal{Target=Target,Phase="installing",Proxy=proxy};
   foreach(var pair in files) {
    string name=pair.Key;var path=Path.Combine(Target,name);Disk.Safe(path);
    if(Directory.Exists(path))throw new IOException("파일 경로에 폴더가 있습니다: "+name);
    var e=new Entry{Name=name,Original=Disk.Hash(path),Installed=Disk.HashBytes(pair.Value),Backup=Guid.NewGuid().ToString("N")+".bak"};
    if(e.Original!=null){Disk.Write(Path.Combine(Store,e.Backup),File.ReadAllBytes(path));if(Disk.Hash(Path.Combine(Store,e.Backup))!=e.Original)throw new IOException("백업 검증 실패");}
    j.Files.Add(e);
   }
   Disk.Save(StateFile,j);
   try {
    int step=0;foreach(var e in j.Files){if(Disk.Hash(Path.Combine(Target,e.Name))!=e.Original)throw new IOException("설치 중 원본 변경 감지");Disk.Write(Path.Combine(Target,e.Name),files[e.Name]);if(Fault!=null)Fault(++step);}
    if(j.Files.Any(e=>Disk.Hash(Path.Combine(Target,e.Name))!=e.Installed))throw new IOException("설치 후 검증 실패");
     j.Phase="enabled";if(!IsSpoof){var runtime=package??Updates.Current();j.RuntimeVersion=runtime.Version;j.RuntimeRevision=runtime.Revision;}Disk.Save(StateFile,j);
   } catch {try{Restore();}catch{} throw;}
  }
  static bool IsIni(Entry e) => e.Name=="dlssg_sm86.ini";
  bool CanRestore(Entry e,string hash,string phase) => IsIni(e)||hash==e.Installed||hash==e.Original||hash==null||((phase=="updating"||phase=="restoring")&&hash==e.Previous);
  string OriginalAfterRestore(Entry e) => IsIni(e)?null:e.Original;
  void VerifyLogCleanup() {
   if(IsSpoof)return;
   foreach(string name in new[]{"dlssg_sm86.log","dlssg_sm86_loader.log"}) {
    string path=Path.Combine(Target,name);Disk.Safe(path);if(Directory.Exists(path))throw new IOException("A directory blocks log cleanup: "+name);
   }
   string logs=Path.Combine(Target,"dlssg_sm86","logs");Disk.Safe(logs);
   if(File.Exists(logs))throw new IOException("A file blocks log cleanup.");
   if(Directory.Exists(logs))VerifyLogTree(logs);
  }
  static void VerifyLogTree(string folder) {
   Disk.Safe(folder);
   foreach(string file in Directory.GetFiles(folder))Disk.Safe(file);
   foreach(string child in Directory.GetDirectories(folder)){Disk.Safe(child);VerifyLogTree(child);}
  }
  void CleanupLogs() {
   if(IsSpoof)return;VerifyLogCleanup();
   foreach(string name in new[]{"dlssg_sm86.log","dlssg_sm86_loader.log"}) {
    string path=Path.Combine(Target,name);if(File.Exists(path))File.Delete(path);
   }
   string root=Path.Combine(Target,"dlssg_sm86"),logs=Path.Combine(root,"logs");
   if(Directory.Exists(logs))DeleteLogTree(logs);
   if(Directory.Exists(root)&&!Directory.EnumerateFileSystemEntries(root).Any())Directory.Delete(root);
  }
  static void DeleteLogTree(string folder) {
   Disk.Safe(folder);
   foreach(string file in Directory.GetFiles(folder)){Disk.Safe(file);File.Delete(file);}
   foreach(string child in Directory.GetDirectories(folder))DeleteLogTree(child);
   Directory.Delete(folder);
  }
  public void VerifyRestore() {
   Disk.Safe(Target);CheckRunning(Target);var j=Load();if(j==null)return;
   VerifyLogCleanup();
   if(j.Phase=="disabled")return;
   foreach(var e in j.Files) {
    var path=Path.Combine(Target,e.Name);if(Directory.Exists(path))throw new IOException("A directory blocks restore: "+e.Name);
    if(!CanRestore(e,Disk.Hash(path),j.Phase))throw new IOException("Installed DLL changed; preserve it elsewhere before restoring: "+e.Name);
    if(!IsIni(e)&&e.Original!=null&&Disk.Hash(Path.Combine(Store,e.Backup))!=e.Original)throw new IOException("Original backup is damaged: "+e.Name);
   }
  }
  public bool CanEditIni() {
   var j=Load();return !IsSpoof&&j?.Phase=="enabled"&&Version.TryParse(j.RuntimeVersion,out var version)&&version.Major==0&&version>=new Version("0.3.4");
  }
  public RuntimeIniSettings ReadIniSettings() {
   if(!CanEditIni())throw new IOException("Install runtime 0.3.4 or newer before editing the INI.");
   string path=Path.Combine(Target,"dlssg_sm86.ini");Disk.Safe(path);
   return RuntimeIni.Read(File.ReadAllBytes(path));
  }
  public void SaveIniSettings(RuntimeIniSettings settings) {
   settings.Validate();Disk.Safe(Target);CheckRunning(Target);
   if(!CanEditIni())throw new IOException("Install runtime 0.3.4 or newer before editing the INI.");
   var j=Load();
   foreach(var dll in j.Files.Where(e=>e.Name.EndsWith(".dll")))
    if(Disk.Hash(Path.Combine(Target,dll.Name))!=dll.Installed)throw new IOException("Installed DLL changed; restore it before editing settings.");
   var entry=j.Files.Single(e=>e.Name=="dlssg_sm86.ini");string path=Path.Combine(Target,entry.Name);
   Disk.Safe(path);byte[] before=File.ReadAllBytes(path),after=RuntimeIni.Apply(before,settings);
   string oldHash=entry.Installed;
   if(Disk.Hash(path)!=Disk.HashBytes(before))throw new IOException("INI changed while saving.");
   try {
    Disk.Write(path,after);if(Fault!=null)Fault(1);
    entry.Installed=Disk.HashBytes(after);Disk.Save(StateFile,j);
   }catch {
    if(Disk.Hash(path)==Disk.HashBytes(after))Disk.Write(path,before);
    entry.Installed=oldHash;throw;
   }
  }
  public void Upgrade(RuntimePackage package) {
   if(IsSpoof)throw new IOException("Not a runtime installation.");
   Updates.Validate(package);VerifyRestore();var journal=Load();
   if(journal==null||journal.Phase!="enabled")throw new IOException("Only an enabled installation can be updated.");
   string proxy=Payload.Proxies.Contains(journal.Proxy)?journal.Proxy:"version.dll";
   // Validate the entire incoming pair before touching the installed game.
   var bytes=new Dictionary<string,byte[]>();
   foreach(string name in new[]{proxy,"dlssg_sm86.ini"}) {
    byte[] data=File.ReadAllBytes(Updates.FileFor(package,name));Updates.ValidateInstallBytes(package,name,data);bytes[name]=data;
   }
   Restore();
   // Original DLLs are now in place. InstallFiles backs those originals up, not the old mod.
   // If installation fails, its journal restores the original game state again.
   InstallFiles(proxy,bytes,package);
  }
   public static bool HasAny(string folder){var mfg=new Installer(folder);return mfg.Status()!="미적용"||new Installer(folder,true).Status()!="미적용"||mfg.NeedsCleanup();}
   bool NeedsCleanup(){return Load()!=null&&(File.Exists(Path.Combine(Target,"dlssg_sm86.ini"))||Directory.Exists(Path.Combine(Target,"dlssg_sm86","logs"))||File.Exists(Path.Combine(Target,"dlssg_sm86.log"))||File.Exists(Path.Combine(Target,"dlssg_sm86_loader.log")));}
   public static void RestoreAll(string folder){var mfg=new Installer(folder);var spoof=new Installer(folder,true);mfg.VerifyRestore();spoof.VerifyRestore();spoof.Restore();mfg.Restore();}
  public void Restore() {
   VerifyRestore();var j=Load();if(j==null)return;
   if(j.Phase=="disabled") {
    // An older app may have left INI/log files after restoring the DLL.
    if(!IsSpoof) {string ini=Path.Combine(Target,"dlssg_sm86.ini");Disk.Safe(ini);if(File.Exists(ini))File.Delete(ini);CleanupLogs();}
    return;
   }
   j.Phase="restoring";Disk.Save(StateFile,j);int step=0;
   foreach(var e in j.Files) {
    string path=Path.Combine(Target,e.Name);Disk.Safe(path);
    if(!CanRestore(e,Disk.Hash(path),j.Phase))throw new IOException("File changed during restore: "+e.Name);
    if(OriginalAfterRestore(e)!=null)Disk.Write(path,File.ReadAllBytes(Path.Combine(Store,e.Backup)));
    else if(File.Exists(path))File.Delete(path);
    if(Fault!=null)Fault(++step);
   }
   if(j.Files.Any(e=>Disk.Hash(Path.Combine(Target,e.Name))!=OriginalAfterRestore(e)))throw new IOException("Restore verification failed.");
   CleanupLogs();j.Phase="disabled";Disk.Save(StateFile,j);
  }
 }
}
