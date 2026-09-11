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
  public const string Revision="5f62ff44a9c08f9841fa605e7b7160f79ccd2c40";
  public const string BaselineVersion="0.2.4";
  public static readonly string[] Proxies={"version.dll","winmm.dll","dinput8.dll","winhttp.dll","dxgi.dll"};
  public static RuntimePackage Baseline() {
   using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.lock"))) {
    var files=r.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim('\uFEFF').Split('|')).Select(x=>new PackageFile{Name=x[0],Path=x[1],Hash=x[2],Blob=x[3]}).ToList();
    return new RuntimePackage{Revision=Revision,Version=BaselineVersion,Files=files};
   }
  }
  public static Dictionary<string,string[]> Specs() { return Updates.Current().Files.ToDictionary(f=>f.Name,f=>new[]{f.Name,f.Path,f.Hash,f.Blob}); }
  public static byte[] SpoofBytes() { using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("spoof5080.dll")) using(var output=new MemoryStream()) { if(input==null) throw new IOException("RTX 5080 프록시 리소스가 없습니다."); input.CopyTo(output); return output.ToArray(); } }
  static string cacheOverride;
  public static string Cache {get{return cacheOverride??Updates.DirectoryFor(Updates.Current());}set{cacheOverride=value;}}
  public static void Ensure(string proxy,Action<string> progress) {
   var package=Updates.Current();foreach(var name in new[]{proxy,"dlssg_sm86.ini"})Updates.Ensure(package,name,progress,cacheOverride);
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
    bool proxyOk=IsSpoof?j.Proxy=="nvapi64.dll":Payload.Proxies.Contains(j.Proxy);
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
   if(File.Exists(Path.Combine(Target,proxy))) throw new IOException(proxy+" 파일이 이미 있습니다. 다른 프록시를 선택하세요. 기존 모드는 덮어쓰지 않습니다.");
   foreach(var p in Payload.Proxies) if(File.Exists(Path.Combine(Target,p)) && Disk.Hash(Path.Combine(Target,p))==Payload.Specs()[p][2]) throw new IOException("수동 설치된 DLSSG 프록시가 있습니다: "+p+". 먼저 수동 설치를 정리하세요.");
   foreach(var n in new[]{proxy,"dlssg_sm86.ini"}) if(Disk.Hash(Path.Combine(payload,n))!=Payload.Specs()[n][2]) throw new IOException("설치 파일 검증 실패: "+n);
   InstallFiles(proxy,new[]{proxy,"dlssg_sm86.ini"}.ToDictionary(n=>n,n=>File.ReadAllBytes(Path.Combine(payload,n))));
  }
   public void EnableSpoof() {
    if(!IsSpoof) throw new IOException("잘못된 설치 종류");
    InstallFiles("nvapi64.dll",new Dictionary<string,byte[]>{{"nvapi64.dll",Payload.SpoofBytes()}});
   }
  void InstallFiles(string proxy,Dictionary<string,byte[]> files) {
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
     j.Phase="enabled";if(!IsSpoof){var runtime=Updates.Current();j.RuntimeVersion=runtime.Version;j.RuntimeRevision=runtime.Revision;}Disk.Save(StateFile,j);
   } catch {try{Restore();}catch{} throw;}
  }
  public void VerifyRestore() {
   Disk.Safe(Target);CheckRunning(Target);var j=Load();if(j==null||j.Phase=="disabled")return;
   foreach(var e in j.Files){var path=Path.Combine(Target,e.Name);if(Directory.Exists(path))throw new IOException("복구 경로에 폴더가 있습니다: "+e.Name);string now=Disk.Hash(path);if(now!=e.Installed&&now!=e.Original&&now!=null&&!((j.Phase=="updating"||j.Phase=="restoring")&&now==e.Previous))throw new IOException(e.Name+"이 설치 후 변경되었습니다. 파일을 다른 폴더에 보관한 뒤 다시 해제하세요.");if(e.Original!=null&&Disk.Hash(Path.Combine(Store,e.Backup))!=e.Original)throw new IOException("원본 백업 손상: "+e.Name);}
  }
  public void Upgrade(RuntimePackage package) {
     if(IsSpoof)throw new IOException("dlssg_for_sm86 업데이트 대상이 아닙니다.");
   Updates.Validate(package);VerifyRestore();var j=Load();if(j==null||j.Phase!="enabled")throw new IOException("적용된 MFG만 업데이트할 수 있습니다.");
   var bytes=new Dictionary<string,byte[]>();
   foreach(var e in j.Files){if(Disk.Hash(Path.Combine(Target,e.Name))!=e.Installed)throw new IOException("설치 파일 변경 감지: "+e.Name);var spec=package.Files.Single(f=>f.Name==e.Name);var data=File.ReadAllBytes(Path.Combine(Updates.DirectoryFor(package),e.Name));if(Disk.HashBytes(data)!=spec.Hash)throw new IOException("업데이트 캐시 검증 실패");bytes[e.Name]=data;}
   foreach(var e in j.Files){e.Previous=e.Installed;e.UpgradeBackup=Guid.NewGuid().ToString("N")+".upg";Disk.Write(Path.Combine(Store,e.UpgradeBackup),File.ReadAllBytes(Path.Combine(Target,e.Name)));if(Disk.Hash(Path.Combine(Store,e.UpgradeBackup))!=e.Previous)throw new IOException("업데이트 직전 파일 변경 감지");e.Installed=Disk.HashBytes(bytes[e.Name]);}
   j.Phase="updating";Disk.Save(StateFile,j);
   try {
    foreach(var e in j.Files){if(Disk.Hash(Path.Combine(Target,e.Name))!=e.Previous)throw new IOException("업데이트 도중 파일 변경 감지");Disk.Write(Path.Combine(Target,e.Name),bytes[e.Name]);}
    if(j.Files.Any(e=>Disk.Hash(Path.Combine(Target,e.Name))!=e.Installed))throw new IOException("업데이트 후 검증 실패");
   }catch {
    // Revert to the previous installed runtime without touching original backups.
    try {
     foreach(var e in j.Files){var now=Disk.Hash(Path.Combine(Target,e.Name));if(now!=e.Previous&&now!=e.Installed)throw new IOException("업데이트 복구 중 외부 변경 감지");if(Disk.Hash(Path.Combine(Store,e.UpgradeBackup))!=e.Previous)throw new IOException("업데이트 백업 손상");}
     foreach(var e in j.Files){Disk.Write(Path.Combine(Target,e.Name),File.ReadAllBytes(Path.Combine(Store,e.UpgradeBackup)));e.Installed=e.Previous;e.Previous=null;e.UpgradeBackup=null;}
     j.Phase="enabled";Disk.Save(StateFile,j);
    }catch{}throw;
   }
   j.Phase="enabled";j.RuntimeVersion=package.Version;j.RuntimeRevision=package.Revision;foreach(var e in j.Files){e.Previous=null;e.UpgradeBackup=null;}Disk.Save(StateFile,j);
  }
   public static bool HasAny(string folder){return new Installer(folder).Status()!="미적용"||new Installer(folder,true).Status()!="미적용";}
   public static void RestoreAll(string folder){var mfg=new Installer(folder);var spoof=new Installer(folder,true);mfg.VerifyRestore();spoof.VerifyRestore();spoof.Restore();mfg.Restore();}
  public void Restore() {
   VerifyRestore(); var j=Load(); if(j==null||j.Phase=="disabled") return;
   foreach(var e in j.Files) { string now=Disk.Hash(Path.Combine(Target,e.Name)); if(now!=e.Installed && now!=e.Original && now!=null && !((j.Phase=="updating"||j.Phase=="restoring") && now==e.Previous)) throw new IOException(e.Name+"이 설치 후 변경되었습니다. 파일을 다른 폴더에 보관한 뒤 다시 해제하세요."); if(e.Original!=null && Disk.Hash(Path.Combine(Store,e.Backup))!=e.Original) throw new IOException("원본 백업 손상: "+e.Name); }
   bool interruptedUpdate=j.Files.Any(e=>e.Previous!=null); j.Phase="restoring"; Disk.Save(StateFile,j); int step=0;
   foreach(var e in j.Files) { var path=Path.Combine(Target,e.Name); var now=Disk.Hash(path); if(now!=e.Installed && now!=e.Original && now!=null && !(interruptedUpdate && now==e.Previous)) throw new IOException("복구 중 파일 변경 감지"); if(e.Original!=null) Disk.Write(path,File.ReadAllBytes(Path.Combine(Store,e.Backup))); else if(File.Exists(path)) File.Delete(path); if(Fault!=null) Fault(++step); }
   if(j.Files.Any(e=>Disk.Hash(Path.Combine(Target,e.Name))!=e.Original)) throw new IOException("복구 검증 실패"); j.Phase="disabled"; Disk.Save(StateFile,j);
  }
 }
}
