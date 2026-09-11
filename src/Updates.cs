using System;
using System.IO;
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
  [DataMember] public List<PackageFile> Files;
 }
 [DataContract] public class AppSettings {
  [DataMember] public bool AutoUpdate;
 }
 public sealed class UpdateResult {
  public string Summary;
  public List<string> Details=new List<string>();
 }

 public static class Updates {
  public static readonly string DataRoot=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MFG Enabler","payload");
  static string ActiveFile { get { return System.IO.Path.Combine(DataRoot,"active.json"); } }
  public static string RemotePath(string name) {
   if(name=="version.dll"||name=="dlssg_sm86.ini") return name;
   if(Payload.Proxies.Contains(name)) return "altnative/"+name;
   throw new IOException("알 수 없는 업데이트 파일: "+name);
  }
  static bool HashLike(string value,int length) { return value!=null && Regex.IsMatch(value,"\\A[a-f0-9]{"+length+"}\\z"); }
  public static void Validate(RuntimePackage p) {
   if(p==null||!HashLike(p.Revision,40)||p.Version==null||!Regex.IsMatch(p.Version,"\\A[0-9]+\\.[0-9]+\\.[0-9]+(?:\\.[0-9]+)?\\z")||p.Files==null||p.Files.Count!=6) throw new IOException("업데이트 패키지 기록이 유효하지 않습니다.");
   var names=Payload.Proxies.Concat(new[]{"dlssg_sm86.ini"}).ToArray();
   if(p.Files.Any(f=>f==null)||p.Files.Select(f=>f.Name).Distinct().Count()!=6||p.Files.Any(f=>!names.Contains(f.Name)||f.Path!=RemotePath(f.Name)||!HashLike(f.Hash,64)||!HashLike(f.Blob,40))) throw new IOException("업데이트 파일 목록 또는 해시가 잘못되었습니다.");
  }
  public static RuntimePackage Current() {
   if(!File.Exists(ActiveFile)) return Payload.Baseline();
   Disk.Safe(ActiveFile);var package=Disk.Read<RuntimePackage>(ActiveFile);Validate(package);
   // Installing a newer app must not silently select an older cached runtime.
   return new Version(package.Version)<new Version(Payload.BaselineVersion)?Payload.Baseline():package;
  }
  public static string DirectoryFor(RuntimePackage p) { if(!HashLike(p.Revision,40)) throw new IOException("잘못된 패키지 커밋");return System.IO.Path.Combine(DataRoot,p.Revision); }

  public static byte[] Download(string url,int maxBytes) {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="MFG-Enabler/1.3";request.Timeout=30000;request.ReadWriteTimeout=30000;
   using(var response=(HttpWebResponse)request.GetResponse()) using(var input=response.GetResponseStream()) using(var output=new MemoryStream()) {
    if(response.ContentLength>maxBytes) throw new IOException("업데이트 파일 크기 제한 초과");
    var buffer=new byte[65536];int read;
    while((read=input.Read(buffer,0,buffer.Length))>0){if(output.Length+read>maxBytes)throw new IOException("업데이트 파일 크기 제한 초과");output.Write(buffer,0,read);}
    return output.ToArray();
   }
  }
  static string GitBlob(byte[] bytes) {
   using(var sha=SHA1.Create()) { var header=Encoding.ASCII.GetBytes("blob "+bytes.Length+"\0");sha.TransformBlock(header,0,header.Length,null,0);sha.TransformFinalBlock(bytes,0,bytes.Length);return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant(); }
  }
  static Dictionary<string,object> Json(string url) {var obj=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(Encoding.UTF8.GetString(Download(url,16*1024*1024))) as Dictionary<string,object>;if(obj==null)throw new IOException("GitHub 응답 형식 오류");return obj;}
  static object Get(Dictionary<string,object> d,string key) {object value;return d!=null&&d.TryGetValue(key,out value)?value:null;}

  public static void Ensure(RuntimePackage package,string name,Action<string> progress,string cacheDirectory=null) {
   Validate(package);var spec=package.Files.Single(f=>f.Name==name);string directory=cacheDirectory??DirectoryFor(package),target=System.IO.Path.Combine(directory,name);Disk.Safe(directory);
   if(Disk.Hash(target)==spec.Hash)return;
    progress("dlssg_for_sm86 "+package.Version+" · "+name+" 다운로드 중…");
   byte[] bytes=Download("https://raw.githubusercontent.com/sdli1995/dlssg_for_sm86/"+package.Revision+"/"+spec.Path,128*1024*1024);
   if(Disk.HashBytes(bytes)!=spec.Hash||GitBlob(bytes)!=spec.Blob)throw new IOException("다운로드 무결성 확인 실패: "+name);
   Directory.CreateDirectory(directory);Disk.Write(target,bytes);
  }

  static RuntimePackage FetchLatest(RuntimePackage current,Action<string> progress) {
   const string api="https://api.github.com/repos/sdli1995/dlssg_for_sm86/";
       progress("dlssg_for_sm86 최신 버전 확인 중…");var head=Json(api+"commits/main");string revision=Get(head,"sha") as string;
   if(!HashLike(revision,40))throw new IOException("GitHub 커밋 정보 오류");
   if(revision==current.Revision)return current;
   var tree=Json(api+"git/trees/"+revision+"?recursive=1");
   if(!(Get(tree,"truncated") is bool)||((bool)Get(tree,"truncated")))throw new IOException("GitHub 파일 목록이 불완전합니다.");
   var rows=Get(tree,"tree") as object[];if(rows==null)throw new IOException("GitHub 파일 목록 오류");
   var entries=rows.OfType<Dictionary<string,object>>().ToList();
   var readme=entries.SingleOrDefault(e=>(Get(e,"path") as string)=="README.en.md"&&(Get(e,"type") as string)=="blob");
    if(readme==null)throw new IOException("dlssg_for_sm86 버전 안내 파일이 없습니다.");
   var readmeBytes=Download("https://raw.githubusercontent.com/sdli1995/dlssg_for_sm86/"+revision+"/README.en.md",1024*1024);
   if(GitBlob(readmeBytes)!=(Get(readme,"sha") as string))throw new IOException("버전 안내 파일 무결성 오류");
   var match=Regex.Match(Encoding.UTF8.GetString(readmeBytes),@"(?m)^#\s+DLSSG Native\s+([0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)\s*$");
    if(!match.Success)throw new IOException("새 dlssg_for_sm86 버전 형식을 인식하지 못했습니다. 현재 버전을 유지합니다.");
   string version=match.Groups[1].Value;if(new Version(version)<new Version(current.Version))throw new IOException("이전 버전으로 자동 변경하지 않습니다.");
   var next=new RuntimePackage{Revision=revision,Version=version,Files=new List<PackageFile>()};
   foreach(string name in Payload.Proxies.Concat(new[]{"dlssg_sm86.ini"})) {
    string path=RemotePath(name);var node=entries.SingleOrDefault(e=>(Get(e,"path") as string)==path&&(Get(e,"type") as string)=="blob"&&(Get(e,"mode") as string)=="100644");
    string blob=node==null?null:Get(node,"sha") as string;
    if(!HashLike(blob,40))throw new IOException("업데이트 설치 형식이 달라졌습니다: "+name);
    var previous=current.Files.Single(f=>f.Name==name);
    next.Files.Add(new PackageFile{Name=name,Path=path,Blob=blob,Hash=previous.Blob==blob?previous.Hash:null});
   }
   if(next.Version==current.Version&&next.Files.All(f=>f.Blob==current.Files.Single(o=>o.Name==f.Name).Blob))return current;
   // Download the complete set before changing the active package pointer.
   string staging=DirectoryFor(next);Disk.Safe(staging);Directory.CreateDirectory(staging);
   foreach(var file in next.Files) {
    string target=System.IO.Path.Combine(staging,file.Name);byte[] bytes=null;
    if(File.Exists(target)){var existing=File.ReadAllBytes(target);if(GitBlob(existing)==file.Blob)bytes=existing;}
    if(bytes==null) {
     string prior=System.IO.Path.Combine(DirectoryFor(current),file.Name);
     if(file.Hash!=null&&Disk.Hash(prior)==file.Hash)bytes=File.ReadAllBytes(prior);
           else {progress("dlssg_for_sm86 "+version+" · "+file.Name+" 다운로드 중…");bytes=Download("https://raw.githubusercontent.com/sdli1995/dlssg_for_sm86/"+revision+"/"+file.Path,128*1024*1024);}
    }
    if(GitBlob(bytes)!=file.Blob)throw new IOException("GitHub 파일 해시 불일치: "+file.Name);
    file.Hash=Disk.HashBytes(bytes);Disk.Write(target,bytes);
    if(file.Name.EndsWith(".dll")&&!Discovery.IsX64(target))throw new IOException("x64 DLL 형식이 아닙니다: "+file.Name);
    if(file.Name=="dlssg_sm86.ini") {
     string ini=Encoding.UTF8.GetString(bytes);
     if(!Regex.IsMatch(ini,@"(?mi)^\s*Router\s*=\s*SM86\s*$")||!Regex.IsMatch(ini,@"(?mi)^\s*KernelImage\s*=\s*PTX\s*$"))throw new IOException("dlssg_for_sm86/PTX 기본 설정이 변경되었습니다. 자동 적용을 중단합니다.");
    }
   }
   Validate(next);Disk.Save(System.IO.Path.Combine(staging,"package.json"),next);Disk.Save(ActiveFile,next);return next;
  }

  public static UpdateResult CheckAndApply(IEnumerable<Game> known,Action<string> progress) {
   var result=new UpdateResult();RuntimePackage selected=Current();bool checkFailed=false;
   try {selected=FetchLatest(selected,progress);}catch(Exception e){checkFailed=true;result.Details.Add("업데이트 확인 실패 · 기존 버전 유지: "+e.Message);}
   int applied=0,skipped=0;
   foreach(var game in known.Where(g=>g!=null&&!String.IsNullOrEmpty(g.Exe)).GroupBy(g=>System.IO.Path.GetDirectoryName(g.Exe),StringComparer.OrdinalIgnoreCase).Select(g=>g.First())) {
    try {
     var installer=new Installer(System.IO.Path.GetDirectoryName(game.Exe));var journal=installer.Load();
     if(journal==null||journal.Phase=="disabled")continue;
     if(!File.Exists(game.Exe))throw new IOException("게임 실행 파일이 없습니다.");
     var relevant=selected.Files.Where(f=>f.Name==journal.Proxy||f.Name=="dlssg_sm86.ini").ToList();
     if(journal.Phase=="enabled"&&journal.Files.All(e=>relevant.Any(f=>f.Name==e.Name&&f.Hash==e.Installed)))continue;
     if(journal.Phase!="enabled")throw new IOException("먼저 중단된 작업을 복구하세요.");
     installer.VerifyRestore();
     foreach(var file in relevant)Ensure(selected,file.Name,progress);
      progress(game.Name+" · dlssg_for_sm86 "+selected.Version+" 적용 중…");installer.Upgrade(selected);applied++;
    }catch(Exception e){skipped++;result.Details.Add(game.Name+" · 적용 보류: "+e.Message);}
   }
    if(checkFailed)result.Summary="dlssg_for_sm86 "+selected.Version+" · 확인 실패";
    else if(applied>0)result.Summary="dlssg_for_sm86 "+selected.Version+" · "+applied+"개 갱신"+(skipped>0?" · "+skipped+"개 보류":"");
    else result.Summary="dlssg_for_sm86 "+selected.Version+" · 업데이트 없음"+(skipped>0?" · "+skipped+"개 보류":"");
    result.Details.Insert(0,result.Summary);return result;
  }
 }
}
