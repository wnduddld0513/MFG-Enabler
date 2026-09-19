using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using MfgEnabler;

string sandbox=Path.Combine(Path.GetTempPath(),"MFG-payload-tests-"+Guid.NewGuid().ToString("N"));
Updates.DataRoot=Path.Combine(sandbox,"cache");Directory.CreateDirectory(Updates.DataRoot);
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){bool rejected=false;try{action();}catch(IOException){rejected=true;}Check(rejected,label);}
byte[] ini=Encoding.UTF8.GetBytes("[General]\nEnabled=1\n[FrameGeneration]\nOptimized=1\nMaxGeneratedFrames=3\n[Logging]\nDirectory=dlssg_sm86\\logs\n");
byte[] Dll(byte marker){var b=new byte[128];b[0]=0x4d;b[1]=0x5a;b[0x3c]=64;b[64]=0x50;b[65]=0x45;b[68]=0x64;b[69]=0x86;b[100]=marker;return b;}
RuntimePackage Package(char revision,byte marker)=>new(){Channel=Updates.Channel,Version="0.3.4",Revision=new string(revision,40),Files=Payload.Proxies.Concat(new[]{"dlssg_sm86.ini"}).Select(n=>{var b=n.EndsWith(".dll")?Dll(marker):ini;return new PackageFile{Name=n,Path=Updates.RemotePath(n),Hash=Disk.HashBytes(b),Blob=Updates.GitBlob(b)};}).ToList()};
string Stage(RuntimePackage p,byte marker){string s=Path.Combine(Updates.DataRoot,".stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(s);foreach(var f in p.Files)File.WriteAllBytes(Path.Combine(s,f.Name),f.Name.EndsWith(".dll")?Dll(marker):ini);return s;}
string Game(string name){string path=Path.Combine(sandbox,name);Directory.CreateDirectory(path);return path;}
void Legacy(string game,string proxy,byte[] original,byte[] installed,string version="0.2.4"){
 var i=new Installer(game);Directory.CreateDirectory(i.Store);File.WriteAllBytes(Path.Combine(game,proxy),installed);File.WriteAllBytes(Path.Combine(game,"dlssg_sm86.ini"),ini);
 var j=new Journal{Target=game,Phase="enabled",Proxy=proxy,RuntimeVersion=version,RuntimeRevision=new string('c',40)};
 var e=new Entry{Name=proxy,Original=original==null?null:Disk.HashBytes(original),Installed=Disk.HashBytes(installed),Backup="original.bak"};
 if(original!=null)File.WriteAllBytes(Path.Combine(i.Store,e.Backup),original);j.Files.Add(e);
 j.Files.Add(new Entry{Name="dlssg_sm86.ini",Installed=Disk.HashBytes(ini),Backup="ini.bak"});Disk.Save(Path.Combine(i.Store,"state.json"),j);
}
void Logs(string game){Directory.CreateDirectory(Path.Combine(game,"dlssg_sm86","logs"));File.WriteAllText(Path.Combine(game,"dlssg_sm86","logs","loader_123.jsonl"),"log");File.WriteAllText(Path.Combine(game,"dlssg_sm86.log"),"legacy log");}
void Zip(string path,RuntimePackage p,byte marker,string extra=null,bool missing=false){using var z=ZipFile.Open(path,ZipArchiveMode.Create);foreach(var f in p.Files.Where(f=>!missing||f.Name!="dbghelp.dll")){using var s=z.CreateEntry("source/"+f.Path).Open();s.Write(f.Name.EndsWith(".dll")?Dll(marker):ini);}if(extra!=null){using var s=z.CreateEntry(extra).Open();s.Write(Dll(marker));}}
try {
 var p=Package('a',1);Updates.Publish(p,Stage(p,1));
 Check(Updates.Current().Revision==p.Revision,"publish seven-file package in fixed owner cache");
 Check(Payload.Proxies.Length==6&&!Payload.Proxies.Contains("winhttp.dll")&&Payload.Proxies.Contains("dbghelp.dll"),"new alternative DLL list");
 byte[] original=Encoding.UTF8.GetBytes("original game DLL"),unrelated=Encoding.UTF8.GetBytes("other DLL");
 string game=Game("fresh");File.WriteAllBytes(Path.Combine(game,"version.dll"),original);File.WriteAllBytes(Path.Combine(game,"nvngx_dlssg.dll"),unrelated);
 var installer=new Installer(game);installer.Enable("version.dll",Updates.DirectoryFor(p));
 var journal=installer.Load();Check(File.ReadAllBytes(Path.Combine(installer.Store,journal.Files[0].Backup)).SequenceEqual(original),"pre-existing original DLL backed up inside .mfg-enabler");
 File.WriteAllText(Path.Combine(game,"dlssg_sm86.ini"),"user edited INI");Logs(game);installer.Restore();
 Check(File.ReadAllBytes(Path.Combine(game,"version.dll")).SequenceEqual(original),"restore original DLL byte-for-byte");
 Check(!File.Exists(Path.Combine(game,"dlssg_sm86.ini"))&&!Directory.Exists(Path.Combine(game,"dlssg_sm86"))&&!File.Exists(Path.Combine(game,"dlssg_sm86.log")),"restore deletes edited INI and runtime logs");
 Check(File.ReadAllBytes(Path.Combine(game,"nvngx_dlssg.dll")).SequenceEqual(unrelated)&&Directory.Exists(installer.Store),"restore preserves unrelated DLL and recovery backups");
 installer.Restore();Check(installer.Status()=="미적용","restore can be repeated");
 File.WriteAllText(Path.Combine(game,"dlssg_sm86.ini"),"left by old restore");Logs(game);Check(Installer.HasAny(game),"disabled legacy journal with leftovers still enables recovery");installer.Restore();Check(!Installer.HasAny(game),"legacy disabled state cleanup");
 string noOriginal=Game("no-original");var fresh=new Installer(noOriginal);fresh.Enable("dxgi.dll",Updates.DirectoryFor(p));fresh.Restore();Check(!File.Exists(Path.Combine(noOriginal,"dxgi.dll")),"no original DLL means remove installed proxy");
 foreach(string proxy in new[]{"version.dll","winhttp.dll"}){
  string g=Game("legacy-"+proxy);Legacy(g,proxy,original,Dll(9),proxy=="version.dll"?"310.9.1.9":"0.2.4");Logs(g);File.WriteAllText(Path.Combine(g,"dlssg_sm86.ini"),"edited old INI");
  var upgrade=new Installer(g);upgrade.Upgrade(p);var j=upgrade.Load();string selected=proxy=="winhttp.dll"?"version.dll":proxy;
  Check(j.Proxy==selected&&j.RuntimeVersion=="0.3.4"&&File.ReadAllBytes(Path.Combine(g,selected)).SequenceEqual(Dll(1)),"restore then install 0.3.4 from "+proxy);
  if(proxy=="winhttp.dll")Check(File.ReadAllBytes(Path.Combine(g,proxy)).SequenceEqual(original),"retired winhttp original restored before proxy migration");
  else Check(File.ReadAllBytes(Path.Combine(upgrade.Store,j.Files.Single(e=>e.Name==proxy).Backup)).SequenceEqual(original),"upgrade backs up original, not the previous mod");
  Check(!Directory.Exists(Path.Combine(g,"dlssg_sm86")),"upgrade clears previous logs");upgrade.Restore();
  Check(File.ReadAllBytes(Path.Combine(g,proxy)).SequenceEqual(original)&&!File.Exists(Path.Combine(g,"dlssg_sm86.ini")),"post-upgrade restore returns original game");
 }
 string corruptGame=Game("corrupt-backup");Legacy(corruptGame,"version.dll",original,Dll(9));var corrupt=new Installer(corruptGame);File.WriteAllText(Path.Combine(corrupt.Store,"original.bak"),"bad");Reject(()=>corrupt.Upgrade(p),"corrupt original backup prevents upgrade");Check(File.ReadAllBytes(Path.Combine(corruptGame,"version.dll")).SequenceEqual(Dll(9)),"backup failure leaves installed DLL untouched");
 string changedGame=Game("changed-dll");Legacy(changedGame,"version.dll",original,Dll(9));File.WriteAllBytes(Path.Combine(changedGame,"version.dll"),Dll(8));Reject(()=>new Installer(changedGame).Restore(),"unexpected external DLL changes are preserved");
 string failureGame=Game("install-failure");Legacy(failureGame,"version.dll",original,Dll(9));var failure=new Installer(failureGame);int steps=0;failure.Fault=_=>{if(++steps==3)throw new IOException("injected new install failure");};Reject(()=>failure.Upgrade(p),"new installation failure rolls back after restore");Check(File.ReadAllBytes(Path.Combine(failureGame,"version.dll")).SequenceEqual(original)&&failure.Load().Phase=="disabled","failed upgrade leaves original DLL recoverably restored");
 string interruptedGame=Game("interrupted-restore");Legacy(interruptedGame,"version.dll",original,Dll(9));var interrupted=new Installer(interruptedGame);interrupted.Fault=_=>throw new IOException("interruption");Reject(interrupted.Restore,"interrupted restoration recorded");interrupted.Fault=null;interrupted.Restore();Check(interrupted.Load().Phase=="disabled"&&!File.Exists(Path.Combine(interruptedGame,"dlssg_sm86.ini")),"restore resumes after interruption");
 string linkGame=Game("linked-logs"),outside=Game("outside");Legacy(linkGame,"version.dll",original,Dll(9));File.WriteAllText(Path.Combine(outside,"keep.txt"),"keep");Directory.CreateDirectory(Path.Combine(linkGame,"dlssg_sm86"));
 try {var linkStart=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}; foreach(string arg in new[]{"/c","mklink","/J",Path.Combine(linkGame,"dlssg_sm86","logs"),outside})linkStart.ArgumentList.Add(arg); using(var proc=Process.Start(linkStart)){proc.WaitForExit();if(proc.ExitCode!=0)throw new Exception("Cannot create junction fixture: "+proc.StandardError.ReadToEnd());}Reject(()=>new Installer(linkGame).Restore(),"log cleanup rejects symlink before changing DLL");Check(File.Exists(Path.Combine(outside,"keep.txt"))&&File.ReadAllBytes(Path.Combine(linkGame,"version.dll")).SequenceEqual(Dll(9)),"linked directory and game remain untouched");Directory.Delete(Path.Combine(linkGame,"dlssg_sm86","logs"));}catch(UnauthorizedAccessException){Console.WriteLine("SKIP symlink fixture requires privilege");}
 string zip=Path.Combine(sandbox,"valid.zip");Zip(zip,p,1);string extracted=Path.Combine(sandbox,"extracted");Directory.CreateDirectory(extracted);Updates.ExtractSourceArchive(p,zip,extracted);Check(Directory.GetFiles(extracted).Length==7,"source ZIP extracts only seven required files");
 foreach(var test in new[]{("missing",(string)null,true),("duplicate","source/version.dll",false),("other-root","other/version.dll",false)}){string z=Path.Combine(sandbox,test.Item1+".zip"),stage=Path.Combine(sandbox,test.Item1);Directory.CreateDirectory(stage);Zip(z,p,1,test.Item2,test.Item3);Reject(()=>Updates.ExtractSourceArchive(p,z,stage),"reject "+test.Item1+" archive");}
 string badZip=Path.Combine(sandbox,"bad.zip"),badStage=Path.Combine(sandbox,"bad-stage");Directory.CreateDirectory(badStage);Zip(badZip,p,2);Reject(()=>Updates.ExtractSourceArchive(p,badZip,badStage),"reject altered source ZIP DLL hash");
 var next=Package('d',2);Reject(()=>Updates.Publish(next,Stage(next,1)),"corrupt staged package never replaces active cache");Check(Updates.Current().Revision==p.Revision,"failed cache validation retains active metadata");
 File.Delete(Path.Combine(Updates.DataRoot,"sdli1995.json"));Directory.CreateDirectory(Path.Combine(Updates.DataRoot,"sdli1995.json"));Reject(()=>Updates.Publish(next,Stage(next,2)),"metadata write failure rolls back published folder");Check(Updates.Current().Revision==p.Revision&&File.ReadAllBytes(Updates.FileFor(p,"version.dll")).SequenceEqual(Dll(1)),"cache rollback retains previous binaries");Directory.Delete(Path.Combine(Updates.DataRoot,"sdli1995.json"));
 Updates.Publish(next,Stage(next,2));Check(Updates.Current().Revision==next.Revision,"successful cache update replaces fixed folder");
 Disk.Save(Path.Combine(Updates.DataRoot,"active.json"),new RuntimePackage{Channel="retired",Version="310.9.1.9"});Check(Updates.Current().Revision==Payload.Revision,"retired channel metadata migrates to supported baseline");
 if(args.Length==2&&args[0]=="--source-zip"){
  var baseline=Payload.Baseline();string stage=Path.Combine(sandbox,"real-source");Directory.CreateDirectory(stage);Updates.ExtractSourceArchive(baseline,args[1],stage);Updates.Publish(baseline,stage);Check(Updates.Current().Version=="0.3.4","real 0.3.4 ZIP passes blob hashes, SHA-256, x64 and layout checks");
 }
 if(args.Contains("--live")){
  Updates.DataRoot=Path.Combine(sandbox,"live-cache");var downloaded=Updates.FetchLatest(Payload.Baseline(),Console.WriteLine);Check(downloaded.Version=="0.3.4"&&downloaded.Files.Count==7,"live GitHub release source ZIP download and publish");
 }
 Console.WriteLine($"PASS all {checks} payload checks; fixtures: {sandbox}");
} finally {
 // Retain fixtures for diagnosis; no recursive cleanup of potentially linked test paths.
}