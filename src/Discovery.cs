using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace MfgEnabler {
 [DataContract] public class Game {
  [DataMember] public string Name;
  [DataMember] public string Root;
  [DataMember] public string Source;
  [DataMember] public string Exe;
   [DataMember] public string NvidiaId;
   [DataMember] public bool Manual;
  // Eligibility is deliberately never loaded from our persisted games.json.
  public bool CanEnable;
  public GraphicsApi Api;
  public string Evidence;
  public string StorageFile;
  public List<string> Executables = new List<string>();
  public override string ToString() { return Name; }
 }
 public class NvidiaScan {
  public List<Game> Games = new List<Game>();
  public List<string> Notes = new List<string>();
  public string StorageFile;
  public DateTime UpdatedUtc;
  public int Detected;
  public int Excluded;
   public string Summary { get { return "NVIDIA App 감지 "+Detected+"개 · FG 구성 확인 "+Games.Count(g=>g.CanEnable)+"개 · 제외 "+Excluded+"개"; } }
 }
 // Independent reader of NVIDIA's installed-app output, not a launcher scanner.
 public static class Discovery {
  public static string DefaultStorage { get { return Environment.GetEnvironmentVariable("MFG_ENABLER_STORAGE_FILE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NVIDIA Corporation","NVIDIA App","NvBackend","ApplicationStorage.json"); } }
  public static string NvidiaAppPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVIDIA App","CEF","NVIDIA App.exe"); } }
  static Dictionary<string,object> Obj(object value) { return value as Dictionary<string,object>; }
  static object Field(Dictionary<string,object> obj,string name) { object value; return obj!=null && obj.TryGetValue(name,out value)?value:null; }
  static string Str(Dictionary<string,object> obj,string name) { return Field(obj,name) as string; }
  static bool? Flag(Dictionary<string,object> obj,string name) { object value=Field(obj,name); return value is bool?(bool?)value:null; }
  static IEnumerable<string> Strings(object value) { var array=value as object[]; return array==null?Enumerable.Empty<string>():array.OfType<string>(); }
  static string Id(object value) { if(value is int || value is long || value is decimal) return Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture); return value as string; }
  static string ReadShared(string path) {
   using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
    if(file.Length>64*1024*1024) throw new IOException("NVIDIA 검색 데이터가 너무 큽니다.");
    using(var reader=new StreamReader(file)) return reader.ReadToEnd();
   }
  }
  public static NvidiaScan Scan() { return Scan(DefaultStorage,null); }
  public static NvidiaScan Scan(string storageFile,string fingerprintFile) {
   if(!File.Exists(storageFile)) throw new IOException("NVIDIA App 검색 결과가 없습니다. NVIDIA App에서 게임을 검색한 뒤 목록을 새로고침하세요.");
   var result=new NvidiaScan { StorageFile=Path.GetFullPath(storageFile), UpdatedUtc=File.GetLastWriteTimeUtc(storageFile) };
   Dictionary<string,object> document;
   try { document=Obj(new JavaScriptSerializer { MaxJsonLength=64*1024*1024 }.DeserializeObject(ReadShared(storageFile))); }
   catch(ArgumentException e) { throw new IOException("NVIDIA App이 검색 결과를 갱신 중이거나 JSON 형식이 올바르지 않습니다. 잠시 후 새로고침하세요.",e); }
   var apps=Field(document,"Applications") as object[];
   if(apps==null) throw new IOException("지원하지 않는 NVIDIA ApplicationStorage 형식입니다. 기존 스토어 탐색으로 대체하지 않습니다.");
   var profiles=ReadProfiles(fingerprintFile??FindFingerprint(storageFile),result.Notes);
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(object item in apps) {
    result.Detected++;
    var row=Obj(item); var app=Obj(Field(row,"Application"));
    string name=Str(app,"DisplayName")??"이름 없는 항목";
    try {
     string reason; var game=ReadGame(row,app,profiles,result.StorageFile,out reason);
      if(game==null) { result.Excluded++; result.Notes.Add(name+": "+reason); continue; }
     string key=game.Exe??game.Root;
     if(!seen.Add(key)) { result.Excluded++; result.Notes.Add(name+": 중복 실행 경로"); continue; }
     result.Games.Add(game);
    } catch(Exception e) {
     if(!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is System.Security.SecurityException)) throw;
      result.Excluded++; result.Notes.Add(name+": "+e.Message); result.Games.Add(LockedGame(row,app,name,e.Message,result.StorageFile));
    }
   }
    result.Games=result.Games.OrderBy(g=>g.Name).ToList(); return result;
   }
   static Game LockedGame(Dictionary<string,object> row,Dictionary<string,object> app,string name,string reason,string storage) {
    string root=null,id=null;
    try {
     string r=app!=null?Str(app,"InstallDirectory"):null;
     if(!String.IsNullOrWhiteSpace(r)&&Path.IsPathRooted(r)) root=Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
     if(row!=null) id=Id(Field(row,"LocalId"));
    } catch { root=null; }
    return new Game { Name=name,Root=root,Source="NVIDIA App 감지 · 적용 제외",CanEnable=false,NvidiaId=id,StorageFile=storage,Exe=null,Executables=new List<string>(),Evidence=reason };
   }
  static string FindFingerprint(string storage) {
   string local=Path.Combine(Path.GetDirectoryName(storage),"ApplicationOntology","data","fingerprint.db");
   if(File.Exists(local)) return local;
   return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVIDIA App","NvBackend","ApplicationOntology","data","fingerprint.db");
  }
  static List<XElement> ReadProfiles(string file,List<string> notes) {
   if(!File.Exists(file)) { notes.Add("fingerprint.db 없음: NVIDIA 검색 결과에 기록된 실행 경로만 사용합니다."); return new List<XElement>(); }
   try {
    var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=64*1024*1024 };
    using(var text=new StringReader(ReadShared(file))) using(var reader=XmlReader.Create(text,settings)) {
     var doc=XDocument.Load(reader);
     if(doc.Root==null || doc.Root.Name!="FingerprintDB") throw new XmlException("FingerprintDB root missing");
     return doc.Root.Elements("Fingerprint").SelectMany(x=>x.Elements("Version")).ToList();
    }
   } catch(Exception e) {
    if(!(e is IOException || e is XmlException || e is UnauthorizedAccessException)) throw;
    notes.Add("fingerprint.db 읽기 실패: 검색 결과의 명시적 정보만 사용합니다. "+e.Message); return new List<XElement>();
   }
  }
  static XElement Profile(Dictionary<string,object> app,List<XElement> profiles) {
   string cms=Id(Field(app,"CmsId")), version=Str(app,"Version"), shortName=Str(app,"ShortName");
   if(String.IsNullOrEmpty(cms)||String.IsNullOrEmpty(version)||String.IsNullOrEmpty(shortName)) return null;
   var matches=profiles.Where(p=>(string)p.Element("CMSID")==cms && (string)p.Attribute("name")==version && (string)p.Parent.Attribute("name")==shortName).ToList();
   return matches.Count==1?matches[0]:null;
  }
  static bool? ProfileFg(XElement p) {
   if(p==null) return null;
   // Observed schema: absent disable flag means not deny-listed, NOT proven FG.
   string value=(string)p.Element("Disable_FG_Override");
   if(value==null || value=="0") return false; if(value=="1") return true; return null;
  }
   static Game ReadGame(Dictionary<string,object> row,Dictionary<string,object> app,List<XElement> profiles,string storage,out string reason) {
    reason="불완전한 NVIDIA 검색 항목"; if(app==null) return null;
    if(Flag(app,"IsCreativeApplication")==true) { reason="게임 외 크리에이티브 앱"; return null; }
    string name=Str(app,"DisplayName")??"이름 없는 항목";
    var profile=Profile(app,profiles); bool? denied=Flag(app,"Disable_FG_Override"), profileDenied=ProfileFg(profile);
    if(denied==true || profileDenied==true) { reason="NVIDIA FG override 차단 프로필"; return null; }
    bool fpOk=Flag(app,"IsFingerprintDetected")==true;
    bool profileOk=denied==false || profileDenied==false;
    string root=Str(app,"InstallDirectory");
    if(String.IsNullOrWhiteSpace(root)||!Path.IsPathRooted(root)||!Directory.Exists(root)) { reason="설치 경로 없음 / 제거된 게임"; return LockedGame(row,app,name,reason,storage); }
    root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
    if(root.Length<=3) { reason="게임 설치 폴더로 볼 수 없는 드라이브 루트"; return LockedGame(row,app,name,reason,storage); }
    Disk.Safe(root);
    string driver=Resolve(root,Str(app,"DriverProfile"));
    if(driver==null && profile!=null) driver=Resolve(root,(string)profile.Element("DriverProfile"));
    var paths=new List<string>(); if(driver!=null) paths.Add(driver);
    foreach(string value in Strings(Field(app,"DetectedFiles")).Concat(Strings(Field(app,"ImageFiles")))) {
     string exe=Resolve(root,value); if(exe!=null && !paths.Contains(exe,StringComparer.OrdinalIgnoreCase)) paths.Add(exe);
    }
    if(paths.Count==0) { reason="NVIDIA 기록에 유효한 x64 실행 파일 없음"; return null; }
    bool complete=true; string dlssg=FindDlssg(root,0,ref complete);
    if(dlssg==null) { reason=complete?"게임 폴더에 x64 nvngx_dlssg.dll 없음 (SR만으로는 FG 판정 불가)":"FG 파일 확인 불완전: 접근 불가 또는 탐색 한도"; return null; }
    return new Game {
     Name=Str(app,"DisplayName")??Path.GetFileName(root), Root=root, Source="NVIDIA App · FG DLL 확인",
     NvidiaId=Id(Field(row,"LocalId")), StorageFile=storage, CanEnable=true,
     Exe=driver??(paths.Count==1?paths[0]:null), Executables=paths,
     Api=driver!=null?GraphicsApiDetector.Detect(driver):(paths.Count==1?GraphicsApiDetector.Detect(paths[0]):GraphicsApi.Unknown),
     Evidence=(fpOk?"NVIDIA fingerprint 확인":"NVIDIA fingerprint 미확인 · DLSS 파일 기준으로 허용")+(profileOk?" · FG 차단 없음":" · FG 프로필 미확인 · DLSS 파일 기준으로 허용")+"\nFG 구성 파일: "+dlssg
    };
   }
  static string Resolve(string root,string candidate) {
   if(String.IsNullOrWhiteSpace(candidate)||!candidate.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) return null;
   string path=Path.GetFullPath(Path.IsPathRooted(candidate)?candidate:Path.Combine(root,candidate));
   if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) return null;
   Disk.Safe(path); return IsX64(path)?path:null;
  }
  // Check only a NVIDIA-detected install tree, not arbitrary drives or backups.
  static string FindDlssg(string root,int depth,ref bool complete) {
   if(depth>12) { complete=false; return null; }
   try {
    if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0) { complete=false; return null; }
    string candidate=Path.Combine(root,"nvngx_dlssg.dll");
    if(File.Exists(candidate) && (File.GetAttributes(candidate)&FileAttributes.ReparsePoint)==0 && IsX64(candidate)) return candidate;
    foreach(string child in Directory.GetDirectories(root)) {
     if(Path.GetFileName(child).Equals(".mfg-enabler",StringComparison.OrdinalIgnoreCase)) continue;
     string found=FindDlssg(child,depth+1,ref complete); if(found!=null) return found;
    }
   } catch(UnauthorizedAccessException) { complete=false; } catch(IOException) { complete=false; }
   return null;
  }
  public static bool IsX64(string file) {
   try { using(var r=new BinaryReader(File.OpenRead(file))) {
    if(r.ReadUInt16()!=0x5a4d) return false; r.BaseStream.Position=0x3c; int pos=r.ReadInt32();
    if(pos<0||pos>r.BaseStream.Length-6) return false; r.BaseStream.Position=pos;
    return r.ReadUInt32()==0x4550 && r.ReadUInt16()==0x8664;
   } } catch { return false; }
  }
   public static void ValidateForEnable(Game game,string selectedExe) {
    if(game==null) throw new IOException("NVIDIA App에서 FG 구성을 확인한 게임만 새로 활성화할 수 있습니다.");
    if(game.Manual) { ValidateManual(game,selectedExe); return; }
    if(!game.CanEnable || String.IsNullOrEmpty(game.StorageFile)) throw new IOException("NVIDIA App에서 FG 구성을 확인한 게임만 새로 활성화할 수 있습니다.");
    var fresh=Scan(game.StorageFile,null).Games.FirstOrDefault(g=>g.NvidiaId==game.NvidiaId && String.Equals(g.Root,game.Root,StringComparison.OrdinalIgnoreCase));
    if(fresh==null || !fresh.Executables.Contains(selectedExe,StringComparer.OrdinalIgnoreCase)) throw new IOException("NVIDIA 검색 결과 또는 게임 파일이 변경되었습니다. 목록을 새로고침하세요.");
   }
   public static void ValidateFreshCandidate(Game game,string selectedExe) {
    if(game==null || game.Manual || !game.CanEnable || String.IsNullOrEmpty(game.StorageFile)) throw new IOException("NVIDIA App에서 FG 구성을 확인한 게임만 새로 활성화할 수 있습니다.");
    if(String.IsNullOrEmpty(selectedExe) || game.Executables==null || !game.Executables.Contains(selectedExe,StringComparer.OrdinalIgnoreCase)) throw new IOException("NVIDIA 검색 결과 또는 게임 파일이 변경되었습니다. 목록을 새로고침하세요.");
    if(!File.Exists(selectedExe) || !IsX64(selectedExe)) throw new IOException("선택한 x64 게임 실행 파일을 찾을 수 없습니다.");
    Disk.Safe(selectedExe);
   }
   static void ValidateManual(Game game,string selectedExe) {
    string root=game.Root;
    if(String.IsNullOrWhiteSpace(root)||!Path.IsPathRooted(root)||!Directory.Exists(root)) throw new IOException("게임 설치 폴더가 유효하지 않습니다.");
    root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
    if(root.Length<=3) throw new IOException("게임 설치 폴더가 유효하지 않습니다.");
    Disk.Safe(root);
    if(String.IsNullOrEmpty(selectedExe)||!File.Exists(selectedExe)) throw new IOException("선택한 실행 파일을 찾을 수 없습니다.");
    string exe=Path.GetFullPath(selectedExe);
    if(!exe.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!IsX64(exe)) throw new IOException("게임 폴더 안의 x64 실행 파일을 선택하세요.");
    Disk.Safe(exe);
   }
  public static List<Game> MergeLibrary(IEnumerable<Game> saved,IEnumerable<Game> detected) {
   var list=detected.ToList();
   foreach(var old in saved.Where(g=>g!=null&&!String.IsNullOrEmpty(g.Exe))) {
    var current=list.FirstOrDefault(g=>String.Equals(g.Root,old.Root,StringComparison.OrdinalIgnoreCase));
     if(current!=null && current.Executables.Contains(old.Exe,StringComparer.OrdinalIgnoreCase)) { current.Exe=old.Exe; current.Api=GraphicsApiDetector.Detect(old.Exe); }
     if(old.Manual && current==null) {
      try {
       string folder=Path.GetDirectoryName(Path.GetFullPath(old.Exe));
       if(!list.Any(g=>g.Exe!=null && String.Equals(Path.GetDirectoryName(g.Exe),folder,StringComparison.OrdinalIgnoreCase)) && (File.Exists(old.Exe) || Installer.HasAny(folder)))
        list.Add(new Game { Name=old.Name??Path.GetFileName(folder),Root=old.Root??folder,Exe=old.Exe,Source="수동 추가",Manual=true,CanEnable=true,Executables=ManualExecutables(folder,old.Exe),Evidence="사용자가 직접 추가한 프로그램입니다." });
      } catch(ArgumentException) { } catch(IOException) { }
      continue;
     }
     try {
     string folder=Path.GetDirectoryName(Path.GetFullPath(old.Exe));
     if(!Installer.HasAny(folder)) continue;
     if(list.Any(g=>g.Exe!=null && String.Equals(Path.GetDirectoryName(g.Exe),folder,StringComparison.OrdinalIgnoreCase))) continue;
     list.Add(new Game { Name=old.Name??Path.GetFileName(folder),Root=old.Root,Exe=old.Exe,Source="이전 적용 · 복구 전용",CanEnable=false,Executables=new List<string>{old.Exe},Evidence="현재 NVIDIA FG 후보에 없음 · 기존 적용 해제만 가능합니다." });
    } catch(ArgumentException) { } catch(IOException) { }
   }
    return list.OrderBy(g=>g.Name).ToList();
   }
   public static List<string> ManualExecutables(string folder,string preferred) {
    var list=new List<string>();
    try {
     if(!String.IsNullOrEmpty(preferred)&&File.Exists(preferred)) list.Add(Path.GetFullPath(preferred));
     if(!String.IsNullOrEmpty(folder)&&Directory.Exists(folder)) foreach(var f in Directory.GetFiles(folder,"*.exe")) {
      try { string full=Path.GetFullPath(f); if(IsX64(full)&&!list.Contains(full,StringComparer.OrdinalIgnoreCase)) list.Add(full); }
      catch(IOException) { } catch(UnauthorizedAccessException) { } catch(ArgumentException) { }
     }
    } catch(ArgumentException) { } catch(IOException) { } catch(UnauthorizedAccessException) { }
    if(list.Count==0&&!String.IsNullOrEmpty(preferred)) list.Add(preferred);
    return list;
   }
  }
}
