using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Net;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MfgEnabler {
 [DataContract] public class PackageFile {
  [DataMember] public string Name;
  [DataMember] public string Path;
  [DataMember] public string Hash;
  [DataMember] public string Blob;
 }
 [DataContract] public class RuntimePackage {
  [DataMember] public string Revision;
  [DataMember] public string Version;
  [DataMember] public string Channel;
  [DataMember] public List<PackageFile> Files;
 }
 [DataContract] public class AppSettings { [DataMember] public bool AutoUpdate; }
 public sealed class UpdateResult { public string Summary; public List<string> Details=new List<string>(); }
 public static class Updates {
  public const string Channel="dlssg_for_sm86";
  const string Api="https://api.github.com/repos/sdli1995/dlssg_for_sm86/";
  public static string DataRoot { get; internal set; }=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MFG Enabler","payload");
  static string ActiveFile => Path.Combine(DataRoot,"active.json");
  static string MetadataFile => Path.Combine(DataRoot,"sdli1995.json");
  static IEnumerable<string> Names => Payload.Proxies.Concat(new[]{"dlssg_sm86.ini"});
  static bool HashLike(string value,int length) => value!=null&&Regex.IsMatch(value,"\\A[a-f0-9]{"+length+"}\\z");
  public static string RemotePath(string name) {
   if(name=="version.dll"||name=="dlssg_sm86.ini")return name;
   if(Payload.Proxies.Contains(name))return "alternatives/"+name;
   throw new IOException("Unknown runtime file: "+name);
  }
  public static void Validate(RuntimePackage p) {
   if(p==null||!HashLike(p.Revision,40)||!Version.TryParse(p.Version,out var version)||version<new Version(Payload.BaselineVersion)||p.Channel!=Channel||p.Files==null||p.Files.Count!=Names.Count())throw new IOException("Invalid runtime package metadata.");
   if(p.Files.Any(f=>f==null)||p.Files.Select(f=>f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Names.Count()||p.Files.Any(f=>!Names.Contains(f.Name)||f.Path!=RemotePath(f.Name)||!HashLike(f.Hash,64)||!HashLike(f.Blob,40)))throw new IOException("Invalid runtime file list or hashes.");
  }
  public static RuntimePackage Current() {
   if(!File.Exists(ActiveFile))return Payload.Baseline();
   Disk.Safe(ActiveFile);var p=Disk.Read<RuntimePackage>(ActiveFile);
   // Migrate retired channels and old layouts without reusing their cached binaries.
   if(p==null||p.Channel!=Channel||!Version.TryParse(p.Version,out var version)||version<new Version(Payload.BaselineVersion))return Payload.Baseline();
   Validate(p);return p;
  }
  public static string DirectoryFor(RuntimePackage p) { Validate(p);return Path.Combine(DataRoot,"sdli1995"); }
  public static string FileFor(RuntimePackage p,string name,string directory=null) {
   if(!Names.Contains(name))throw new IOException("Unknown runtime file.");
   return Path.Combine(directory??DirectoryFor(p),name);
  }
  static bool Published(RuntimePackage p) {
   try {Disk.Safe(MetadataFile);var saved=Disk.Read<RuntimePackage>(MetadataFile);Validate(saved);return saved.Revision==p.Revision&&saved.Files.All(f=>p.Files.Any(v=>v.Name==f.Name&&v.Hash==f.Hash))&&p.Files.All(f=>Disk.Hash(FileFor(p,f.Name))==f.Hash);}catch {return false;}
  }
  public static RuntimePackage Prepare(RuntimePackage p,Action<string> progress) => Published(p)?p:FetchLatest(p,progress);
  static string NewStage() {var path=Path.Combine(DataRoot,".stage-"+Guid.NewGuid().ToString("N"));Disk.Safe(path);Directory.CreateDirectory(path);return path;}
  static void RemoveCache(string path) {
   path=Path.GetFullPath(path);var root=Path.GetFullPath(DataRoot);
   if(!String.Equals(Path.GetDirectoryName(path),root,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid cache cleanup path.");
   if(!Directory.Exists(path))return;Disk.Safe(path);
   if(Directory.GetDirectories(path).Length!=0)return;
   var files=Directory.GetFiles(path);
   if(files.Any(f=>!Names.Contains(Path.GetFileName(f))&&Path.GetFileName(f)!="winhttp.dll"&&Path.GetFileName(f)!="package.json"))return;
   foreach(var f in files)Disk.Safe(f);
   foreach(var f in files)File.Delete(f);Directory.Delete(path);
  }
  internal static void Publish(RuntimePackage p,string staging) {
   Validate(p);Disk.Safe(staging);
   foreach(var f in p.Files)ValidateInstallBytes(p,f.Name,File.ReadAllBytes(FileFor(p,f.Name,staging)));
   string target=DirectoryFor(p),backup=Path.Combine(DataRoot,".backup-"+Guid.NewGuid().ToString("N"));
   Disk.Safe(target);Disk.Safe(backup);Disk.Safe(MetadataFile);Disk.Safe(ActiveFile);
   byte[] oldMeta=File.Exists(MetadataFile)?File.ReadAllBytes(MetadataFile):null,oldActive=File.Exists(ActiveFile)?File.ReadAllBytes(ActiveFile):null;
   bool moved=false,installed=false;
   try {
    if(Directory.Exists(target)){Directory.Move(target,backup);moved=true;}
    Directory.Move(staging,target);installed=true;Disk.Save(MetadataFile,p);Disk.Save(ActiveFile,p);
   }catch {
    if(installed)RemoveCache(target);if(moved)Directory.Move(backup,target);
    if(oldMeta!=null)Disk.Write(MetadataFile,oldMeta);else if(File.Exists(MetadataFile))File.Delete(MetadataFile);
    if(oldActive!=null)Disk.Write(ActiveFile,oldActive);else if(File.Exists(ActiveFile))File.Delete(ActiveFile);
    throw;
   }
   try {RemoveCache(backup);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
  public static void ValidateInstallBytes(RuntimePackage p,string name,byte[] bytes) {
   if(Disk.HashBytes(bytes)!=p.Files.Single(f=>f.Name==name).Hash)throw new IOException("Runtime hash mismatch: "+name);
   if(name=="dlssg_sm86.ini")ValidateIni(bytes);
  }
  internal static void ValidateIni(byte[] bytes) {
   if(bytes.Length==0||bytes.Length>1024*1024)throw new IOException("Invalid INI size.");
   string text;try{text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');}catch(DecoderFallbackException){throw new IOException("Invalid INI encoding.");}
   bool section=false;
   foreach(var raw in text.Split('\n')) {
    string line=raw.Trim();if(line.Length==0||line.StartsWith(";")||line.StartsWith("#"))continue;
    if(line.IndexOf('\0')>=0)throw new IOException("Invalid INI content.");
    if(line.StartsWith("[")&&line.EndsWith("]")&&line.Length>2){section=true;continue;}
    if(!section||line.IndexOf('=')<=0)throw new IOException("Invalid INI content.");
   }
   if(!section)throw new IOException("INI has no sections.");
  }
  internal static byte[] VerifyDownload(RuntimePackage p,string name,byte[] bytes) {
   var f=p.Files.Single(x=>x.Name==name);
   if(GitBlob(bytes)!=f.Blob||(f.Hash!=null&&Disk.HashBytes(bytes)!=f.Hash))throw new IOException("Source archive integrity check failed: "+name);
   if(name=="dlssg_sm86.ini")ValidateIni(bytes);return bytes;
  }
  static HttpWebRequest Request(string url) {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   var r=(HttpWebRequest)WebRequest.Create(url);r.UserAgent="MFG-Enabler/1.2b1";r.Timeout=30000;r.ReadWriteTimeout=30000;return r;
  }
  static void DownloadTo(string url,Stream output,long max) {
   using(var response=(HttpWebResponse)Request(url).GetResponse())using(var input=response.GetResponseStream()) {
    if(response.ContentLength>max)throw new IOException("Download size limit exceeded.");
    var buffer=new byte[81920];int read;long count=0;
    while((read=input.Read(buffer,0,buffer.Length))>0){count+=read;if(count>max)throw new IOException("Download size limit exceeded.");output.Write(buffer,0,read);}
   }
  }
  public static byte[] Download(string url,int maxBytes) {using(var output=new MemoryStream()){DownloadTo(url,output,maxBytes);return output.ToArray();}}
  internal static string GitBlob(byte[] bytes) {
   using(var sha=SHA1.Create()){var header=Encoding.ASCII.GetBytes("blob "+bytes.Length+"\0");sha.TransformBlock(header,0,header.Length,null,0);sha.TransformFinalBlock(bytes,0,bytes.Length);return Convert.ToHexString(sha.Hash).ToLowerInvariant();}
  }
  static Dictionary<string,object> Json(string url) => new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(Encoding.UTF8.GetString(Download(url,16*1024*1024))) as Dictionary<string,object> ?? throw new IOException("Invalid GitHub response.");
  static object Get(Dictionary<string,object> d,string key) {object v;return d!=null&&d.TryGetValue(key,out v)?v:null;}
  public static void Ensure(RuntimePackage p,string name,Action<string> progress,string cacheDirectory=null) {
   Validate(p);string path=FileFor(p,name,cacheDirectory);Disk.Safe(path);
   if(Disk.Hash(path)==p.Files.Single(f=>f.Name==name).Hash)return;
   // Repair from the same immutable source ZIP, never from a moving branch.
   DownloadPackage(p,progress);
   if(cacheDirectory!=null&&!String.Equals(Path.GetFullPath(cacheDirectory),Path.GetFullPath(DirectoryFor(p)),StringComparison.OrdinalIgnoreCase))throw new IOException("Custom payload cache is incomplete.");
  }
  internal static RuntimePackage ExtractSourceArchive(RuntimePackage p,string archivePath,string staging) {
   using(var archive=ZipFile.OpenRead(archivePath)) {
    if(archive.Entries.Count>100000)throw new IOException("Source ZIP contains too many files.");
    var wanted=p.Files.ToDictionary(f=>f.Path,StringComparer.Ordinal);
    var found=new HashSet<string>(StringComparer.Ordinal);string root=null;
    foreach(var entry in archive.Entries) {
     int slash=entry.FullName.IndexOf('/');if(slash<1)continue;
     string relative=entry.FullName.Substring(slash+1);
     if(!wanted.TryGetValue(relative,out var spec))continue;
     string prefix=entry.FullName.Substring(0,slash);
     if(root==null)root=prefix;
     if(root!=prefix||prefix==".."||prefix=="."||prefix.Contains('\\')||!found.Add(relative)||((entry.ExternalAttributes>>16)&0xF000)==0xA000||entry.Length<=0||entry.Length>128L*1024*1024)throw new IOException("Unsafe or duplicate runtime entry.");
     byte[] bytes;using(var input=entry.Open())using(var output=new MemoryStream()){
      var buffer=new byte[81920];int count;while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>128L*1024*1024)throw new IOException("Runtime file too large.");output.Write(buffer,0,count);}bytes=output.ToArray();
     }
     VerifyDownload(p,spec.Name,bytes);spec.Hash=Disk.HashBytes(bytes);
     string target=FileFor(p,spec.Name,staging);Disk.Write(target,bytes);
     if(spec.Name.EndsWith(".dll")&&!Discovery.IsX64(target))throw new IOException("Runtime is not an x64 DLL: "+spec.Name);
    }
    if(found.Count!=wanted.Count)throw new IOException("Source ZIP is missing required runtime files.");
   }
   Validate(p);return p;
  }
  static RuntimePackage DownloadPackage(RuntimePackage p,Action<string> progress) {
   string staging=NewStage(),zip=Path.Combine(DataRoot,".source-"+Guid.NewGuid().ToString("N")+".zip");Disk.Safe(zip);
   try {
    progress("dlssg_for_sm86 "+p.Version+" · source ZIP 다운로드 중…");
    using(var file=new FileStream(zip,FileMode.CreateNew))DownloadTo(Api+"zipball/"+p.Revision,file,1024L*1024*1024);
    ExtractSourceArchive(p,zip,staging);Publish(p,staging);return p;
   }finally {if(File.Exists(zip))File.Delete(zip);RemoveCache(staging);}
  }
  public static RuntimePackage FetchLatest(RuntimePackage current,Action<string> progress) {
   progress("dlssg_for_sm86 최신 릴리스 확인 중…");var release=Json(Api+"releases/latest");
   string tag=Get(release,"tag_name") as string;
   if(Get(release,"draft") is not false||Get(release,"prerelease") is not false||!Regex.IsMatch(tag??"",@"\Av?\d+\.\d+\.\d+\z"))throw new IOException("Unsupported upstream release version.");
   string version=tag.TrimStart('v');if(new Version(version)<new Version(current.Version))throw new IOException("Refusing an older upstream runtime.");
   var commit=Json(Api+"commits/"+Uri.EscapeDataString(tag));string revision=Get(commit,"sha") as string;
   if(!HashLike(revision,40))throw new IOException("Invalid release commit.");
   if(revision==current.Revision&&Published(current))return current;
   var tree=Json(Api+"git/trees/"+revision+"?recursive=1");
   if(Get(tree,"truncated") is not false||Get(tree,"tree") is not object[] rows)throw new IOException("Incomplete release tree.");
   var entries=rows.OfType<Dictionary<string,object>>().ToList();
   var next=new RuntimePackage{Revision=revision,Version=version,Channel=Channel,Files=new List<PackageFile>()};
   foreach(string name in Names) {
    string path=RemotePath(name);var node=entries.SingleOrDefault(e=>Get(e,"path") as string==path&&Get(e,"type") as string=="blob"&&Get(e,"mode") as string=="100644");
    string blob=Get(node,"sha") as string;if(!HashLike(blob,40))throw new IOException("Unsupported release layout: "+path);
    next.Files.Add(new PackageFile{Name=name,Path=path,Blob=blob});
   }
   return DownloadPackage(next,progress);
  }
  public static UpdateResult CheckAndApply(IEnumerable<Game> known,Action<string> progress) {
   var result=new UpdateResult();RuntimePackage selected=Current();
   try {selected=FetchLatest(selected,progress);}catch(Exception e){result.Summary="Runtime update check failed; installed games unchanged.";result.Details.Add(result.Summary+" "+e.Message);return result;}
   int applied=0,skipped=0;
   foreach(var game in known.Where(g=>g!=null&&!String.IsNullOrEmpty(g.Exe)).GroupBy(g=>Path.GetDirectoryName(g.Exe),StringComparer.OrdinalIgnoreCase).Select(g=>g.First())) {
    try {
     var installer=new Installer(Path.GetDirectoryName(game.Exe));var j=installer.Load();if(j==null||j.Phase=="disabled")continue;
     if(!File.Exists(game.Exe))throw new IOException("Game executable is missing.");
     if(j.Phase!="enabled")throw new IOException("Restore the interrupted operation first.");
     if(j.RuntimeRevision==selected.Revision&&j.Files.Where(e=>e.Name.EndsWith(".dll")).All(e=>selected.Files.Any(f=>f.Name==e.Name&&f.Hash==e.Installed)))continue;
     progress(game.Name+" · restore, then install "+selected.Version);installer.Upgrade(selected);applied++;
    }catch(Exception e){skipped++;result.Details.Add(game.Name+" · update deferred: "+e.Message);}
   }
   result.Summary="dlssg_for_sm86 "+selected.Version+" · "+applied+" updated, "+skipped+" deferred";result.Details.Insert(0,result.Summary);return result;
  }
 }
}