using System.IO.Compression;
using System.Text;
using MfgEnabler;

string sandbox = Path.Combine(Path.GetTempPath(), "MFG-runtime-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
int checks = 0;
byte[] stockIni = Encoding.UTF8.GetBytes("[FrameGeneration]\r\nOptimized=1\r\nMaxGeneratedFrames=3\r\n");
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (IOException) { Check(true, name); return; }
    throw new Exception("Expected rejection: " + name);
}
byte[] Pe(byte marker, bool x64 = true)
{
    byte[] bytes = new byte[128];
    using var writer = new BinaryWriter(new MemoryStream(bytes));
    writer.Write((ushort)0x5a4d);
    writer.BaseStream.Position = 0x3c; writer.Write(64);
    writer.BaseStream.Position = 64; writer.Write(0x4550); writer.Write((ushort)(x64 ? 0x8664 : 0x14c));
    bytes[^1] = marker;
    return bytes;
}
RuntimePackage Package(byte marker = 1, string version = "0.3.4")
{
    return new RuntimePackage {
        Version = version, Revision = new string(marker == 1 ? 'a' : 'b', 40), Channel = Updates.Channel,
        Files = Payload.Proxies.Append("dlssg_sm86.ini").Select(name => {
            byte[] bytes = name.EndsWith(".ini") ? stockIni : Pe(marker);
            return new PackageFile { Name = name, Path = Updates.RemotePath(name), Hash = Disk.HashBytes(bytes), Blob = Updates.GitBlob(bytes) };
        }).ToList()
    };
}
string Cache(RuntimePackage p, byte marker = 1)
{
    string directory = Updates.DirectoryFor(p);
    Directory.CreateDirectory(directory);
    foreach (var file in p.Files) File.WriteAllBytes(Path.Combine(directory, file.Name), file.Name.EndsWith(".ini") ? stockIni : Pe(marker));
    Disk.Save(Path.Combine(Updates.DataRoot, "active.json"), p);
    return directory;
}
(Installer Installer, RuntimePackage Package) Fixture(string name, bool original = true, bool install = true)
{
    string root = Path.Combine(sandbox, name);
    Directory.CreateDirectory(root);
    Updates.DataRoot = Path.Combine(root, "cache");
    var p = Package(); Cache(p);
    string game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
    File.WriteAllText(Path.Combine(game, "game.exe"), "fixture only");
    File.WriteAllText(Path.Combine(game, "keep.txt"), "user data");
    if (original) File.WriteAllBytes(Path.Combine(game, "version.dll"), Pe(99));
    var installer = new Installer(game);
    if (install) installer.Enable("version.dll", Updates.DirectoryFor(p));
    return (installer, p);
}
bool Original(Installer i) => File.ReadAllBytes(Path.Combine(i.Target, "version.dll")).SequenceEqual(Pe(99));
void Clean(Installer i, string name)
{
    Check(!Directory.Exists(i.Store) && !File.Exists(Path.Combine(i.Target, "dlssg_sm86.ini"))
        && File.ReadAllText(Path.Combine(i.Target, "keep.txt")) == "user data", name);
}

Updates.Validate(Payload.Baseline());
Check(Payload.Proxies.Length == 6 && !Payload.Proxies.Contains("winhttp.dll"), "Single-runtime baseline and proxy set");
var defaults = RuntimeIni.Read(stockIni);
Check(defaults.Optimized == 1 && defaults.MaxGeneratedFrames == 3 && defaults.Preset == "Auto" && defaults.LogLevel == 1, "Factory INI defaults");
Check(RuntimeIni.Read(Encoding.UTF8.GetBytes("[Compatibility]\nOptimizedKernels=2\n")).Optimized == 2, "Legacy optimization alias");
Check(RuntimeIni.Read(Encoding.UTF8.GetBytes("[General]\nEnabled=1\n")).Optimized == 0, "Missing optimization follows runtime tier zero");
string custom = "[FrameGeneration]\r\nOptimized=1 ; keep comment\r\nMaxGeneratedFrames=3\r\n[Runtime]\r\nCacheDirectory=custom\\cache\r\n[Logging]\r\nDirectory=custom\\logs\r\n";
byte[] changed = RuntimeIni.Apply(Encoding.UTF8.GetBytes(custom), new RuntimeIniSettings { Optimized = 3, MaxGeneratedFrames = 5, Preset = "B", LogLevel = 2 });
var settings = RuntimeIni.Read(changed);
Check(settings.Optimized == 3 && settings.MaxGeneratedFrames == 5 && settings.Preset == "B" && settings.LogLevel == 2, "Four settings round trip");
Check(Encoding.UTF8.GetString(changed).Contains("; keep comment\r\n") && Encoding.UTF8.GetString(changed).Contains("CacheDirectory=custom\\cache"), "Keep comments, custom keys and CRLF");
Reject(() => RuntimeIni.Read(Encoding.UTF8.GetBytes("[FrameGeneration]\nOptimized=1\nOptimized=2")), "Reject ambiguous duplicate values");
Reject(() => RuntimeIni.Read(Encoding.UTF8.GetBytes("[FrameGeneration]\nMaxGeneratedFrames=99")), "Reject unsupported frame count");
Reject(() => RuntimeIni.Apply(stockIni, new RuntimeIniSettings { Optimized = 4 }), "Reject unsupported tier");
Reject(() => RuntimeIni.Apply(stockIni, new RuntimeIniSettings { Preset = "C" }), "Reject unsupported preset");
Reject(() => RuntimeIni.Read(new byte[] { 0xff }), "Reject invalid INI encoding");

var f = Fixture("original");
Check(OriginalBackup(f.Installer), "Original DLL stored as version.backup");
bool OriginalBackup(Installer i) => File.ReadAllBytes(Path.Combine(i.Store, "version.backup")).SequenceEqual(Pe(99));
f.Installer.SaveIniSettings(new RuntimeIniSettings { Optimized = 2, MaxGeneratedFrames = 5 });
Check(f.Installer.ReadIniSettings().Optimized == 2 && f.Installer.Status() == "적용됨" && OriginalBackup(f.Installer), "Save game INI and journal without changing DLL backup");
byte[] beforeSave = File.ReadAllBytes(Path.Combine(f.Installer.Target, "dlssg_sm86.ini"));
f.Installer.Fault = _ => throw new IOException("injected save fault");
Reject(() => f.Installer.SaveIniSettings(new RuntimeIniSettings()), "INI save failure is reported");
Check(beforeSave.SequenceEqual(File.ReadAllBytes(Path.Combine(f.Installer.Target, "dlssg_sm86.ini"))) && f.Installer.Status() == "적용됨", "INI save rollback preserves matching journal");
f.Installer.Fault = null;
File.WriteAllText(Path.Combine(f.Installer.Target, "dlssg_sm86.ini"), "manually edited");
Directory.CreateDirectory(Path.Combine(f.Installer.Target, "dlssg_sm86", "logs", "nested"));
File.WriteAllText(Path.Combine(f.Installer.Target, "dlssg_sm86", "logs", "nested", "loader.jsonl"), "log");
File.WriteAllText(Path.Combine(f.Installer.Target, "dlssg_sm86_loader.log"), "old log");
f.Installer.Restore();
Check(Original(f.Installer) && !Directory.Exists(Path.Combine(f.Installer.Target, "dlssg_sm86")), "Restore original DLL and clean runtime logs");
Clean(f.Installer, "Delete edited INI and recovery store after success");
f.Installer.Restore(); Check(!Installer.HasAny(f.Installer.Target), "Repeat restore is harmless");

f = Fixture("no-original", false); f.Installer.Restore();
Check(!File.Exists(Path.Combine(f.Installer.Target, "version.dll")), "Remove injected DLL when no original existed");
Clean(f.Installer, "Clean first-time installation");
f = Fixture("preexisting-ini", install: false);
File.WriteAllText(Path.Combine(f.Installer.Target, "dlssg_sm86.ini"), "old settings");
f.Installer.Enable("version.dll", Updates.DirectoryFor(f.Package)); f.Installer.Restore();
Clean(f.Installer, "Delete pre-existing runtime INI on restore as requested");

f = Fixture("restore-order");
var journal = f.Installer.Load(); journal.Files.Reverse(); Disk.Save(Path.Combine(f.Installer.Store, "state.json"), journal);
f.Installer.Fault = step => { if (step == 1) {
    Check(Original(f.Installer) && File.Exists(Path.Combine(f.Installer.Target, "dlssg_sm86.ini")) && OriginalBackup(f.Installer), "DLL restored before INI or backups, independent of journal order");
    throw new IOException("injected restore fault");
} };
Reject(f.Installer.Restore, "Interrupted restore retains recovery journal");
f.Installer.Fault = null; f.Installer.Restore(); Clean(f.Installer, "Retry interrupted restore completes cleanup");
f = Fixture("install-fault", install: false);
int faults = 0; f.Installer.Fault = _ => { if (++faults == 1) throw new IOException("injected install fault"); };
Reject(() => f.Installer.Enable("version.dll", Updates.DirectoryFor(f.Package)), "Installation fault is reported");
Check(Original(f.Installer), "Installation rollback restores original DLL"); Clean(f.Installer, "Installation rollback removes owned files");

f = Fixture("tampered-dll"); File.WriteAllText(Path.Combine(f.Installer.Target, "version.dll"), "other mod");
Reject(f.Installer.Restore, "Refuse to overwrite an externally changed DLL");
Check(OriginalBackup(f.Installer) && File.Exists(Path.Combine(f.Installer.Target, "dlssg_sm86.ini")), "Preserve backup and INI on refused restore");
f = Fixture("tampered-backup"); File.WriteAllText(Path.Combine(f.Installer.Store, "version.backup"), "damaged");
Reject(f.Installer.Restore, "Reject damaged original backup before changing game files");
f = Fixture("missing-backup"); File.Delete(Path.Combine(f.Installer.Store, "version.backup"));
Reject(f.Installer.Restore, "Reject missing original backup");
f = Fixture("blocked-install", install: false); Directory.CreateDirectory(Path.Combine(f.Installer.Target, "dlssg_sm86.ini"));
Reject(() => f.Installer.Enable("version.dll", Updates.DirectoryFor(f.Package)), "Check all installation destinations before writing");
Check(Original(f.Installer) && !Directory.Exists(f.Installer.Store), "Failed preflight does not leave backups");

f = Fixture("unknown-store-file"); File.WriteAllText(Path.Combine(f.Installer.Store, "keep.txt"), "unknown");
Reject(f.Installer.Restore, "Preserve unknown recovery-store files");
Check(Original(f.Installer) && OriginalBackup(f.Installer), "Keep original backup until store cleanup can complete");
File.Delete(Path.Combine(f.Installer.Store, "keep.txt")); f.Installer.Restore(); Clean(f.Installer, "Retry cleanup from disabled journal");
f = Fixture("legacy-backup"); journal = f.Installer.Load();
var dllEntry = journal.Files.Single(e => e.Name == "version.dll");
string oldBackup = Guid.NewGuid().ToString("N") + ".bak";
File.Move(Path.Combine(f.Installer.Store, dllEntry.Backup), Path.Combine(f.Installer.Store, oldBackup)); dllEntry.Backup = oldBackup;
Disk.Save(Path.Combine(f.Installer.Store, "state.json"), journal);
File.WriteAllText(Path.Combine(f.Installer.Store, Guid.NewGuid().ToString("N") + ".upg"), "old upgrade scratch");
f.Installer.Restore(); Check(Original(f.Installer), "Read old GUID backup names"); Clean(f.Installer, "Clean legacy backup and upgrade records");

f = Fixture("upgrade"); f.Installer.SaveIniSettings(new RuntimeIniSettings { Optimized = 3 });
var next = Package(2, "0.3.5"); Cache(next, 2); f.Installer.Upgrade(next);
Check(f.Installer.Load().RuntimeVersion == "0.3.5" && OriginalBackup(f.Installer) && f.Installer.ReadIniSettings().Optimized == 1, "Upgrade restores then installs and resets INI");
f.Installer.Restore(); Check(Original(f.Installer), "Upgrade preserves true original DLL"); Clean(f.Installer, "Restore after upgrade cleans game directory");
f = Fixture("invalid-upgrade"); next = Package(2, "0.3.5"); Cache(next, 2);
File.WriteAllText(Updates.FileFor(next, "version.dll"), "corrupt");
Reject(() => f.Installer.Upgrade(next), "Validate new runtime before restoring old installation");
Check(f.Installer.Status() == "적용됨" && OriginalBackup(f.Installer), "Bad download leaves installed game unchanged");
f = Fixture("legacy-winhttp"); journal = f.Installer.Load(); dllEntry = journal.Files.Single(e => e.Name == "version.dll");
File.Move(Path.Combine(f.Installer.Target, "version.dll"), Path.Combine(f.Installer.Target, "winhttp.dll"));
dllEntry.Name = journal.Proxy = "winhttp.dll"; journal.RuntimeVersion = "0.2.4";
Disk.Save(Path.Combine(f.Installer.Store, "state.json"), journal);
next = Package(2, "0.3.5"); Cache(next, 2); f.Installer.Upgrade(next);
Check(f.Installer.Load().Proxy == "version.dll" && File.ReadAllBytes(Path.Combine(f.Installer.Target, "winhttp.dll")).SequenceEqual(Pe(99)), "Migrate old winhttp installation after restoring its original");
f.Installer.Restore(); Clean(f.Installer, "Clean migrated installation");

string Archive(RuntimePackage p, string scenario)
{
    string path = Path.Combine(sandbox, scenario + ".zip");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var file in p.Files)
    {
        if (scenario == "missing" && file.Name == "version.dll") continue;
        var entry = zip.CreateEntry("source/" + file.Path);
        if (scenario == "symlink" && file.Name == "version.dll") entry.ExternalAttributes = 0xA000 << 16;
        using var output = entry.Open();
        output.Write(file.Name.EndsWith(".ini") ? stockIni : Pe(scenario == "corrupt" ? (byte)2 : (byte)1));
    }
    if (scenario == "duplicate") { using var output = zip.CreateEntry("source/version.dll").Open(); output.Write(Pe(1)); }
    using (var output = zip.CreateEntry("source/../../outside.txt").Open()) output.Write(Encoding.UTF8.GetBytes("irrelevant"));
    return path;
}
Updates.DataRoot = Path.Combine(sandbox, "archive-cache");
foreach (string scenario in new[] { "valid", "missing", "corrupt", "symlink", "duplicate" })
{
    var p = Package(); string zip = Archive(p, scenario); string stage = Path.Combine(Updates.DataRoot, scenario); Directory.CreateDirectory(stage);
    if (scenario == "valid") {
        Updates.ExtractSourceArchive(p, zip, stage);
        Check(Directory.GetFiles(stage).Length == 7 && !File.Exists(Path.Combine(Updates.DataRoot, "outside.txt")), "Extract only seven verified runtime files");
        Updates.Publish(p, stage); Check(Updates.Current().Revision == p.Revision, "Publish verified cache and metadata");
    }
    else Reject(() => Updates.ExtractSourceArchive(p, zip, stage), "Reject archive " + scenario);
}

var policy = new GlobalPolicy();
var detectedGame = new Game { NvidiaId = "42", Root = "C:/fixture", Exe = "C:/fixture/game.exe", CanEnable = true, Api = GraphicsApi.DirectX12 };
Check(!policy.Allows(detectedGame), "Global override is opt-in");
policy.Enabled = true;
Check(!policy.Allows(detectedGame), "Future games require explicit opt-in");
policy.Approved.Add(GlobalPolicy.Key(detectedGame));
Check(policy.Allows(detectedGame), "Approved existing game is eligible");
policy.Excluded.Add("NVIDIA:42"); policy.FutureGames = true;
Check(!policy.Allows(detectedGame), "Exclusion wins over approval and future-game option, case insensitive");
policy.Excluded.Clear(); policy.Approved.Clear();
Check(policy.Allows(detectedGame), "Future-game option allows newly detected game");
detectedGame.Manual = true;
Check(!policy.Allows(detectedGame), "Manual programs cannot be auto-applied as FG candidates");
detectedGame.Manual = false; detectedGame.CanEnable = false;
Check(!policy.Allows(detectedGame), "Recovery-only entries are never auto-applied");
detectedGame.CanEnable = true; detectedGame.Exe = null;
Check(!policy.Allows(detectedGame), "Ambiguous rendering executable requires user selection");
detectedGame.Exe = "C:/fixture/game.exe"; policy.Enabled = false;
Check(!policy.Allows(detectedGame), "Master switch stops all automatic installs");
policy.Approved = null; policy.Excluded = null; policy.Normalize();
Check(policy.Approved.Count == 0 && policy.Excluded.Count == 0, "Older persisted settings normalize safely");

var globalGood = Fixture("global-off-good");
var globalBad = Fixture("global-off-bad");
string damagedBackup = Path.Combine(globalBad.Installer.Store, "version.backup");
byte[] savedBackup = File.ReadAllBytes(damagedBackup);
File.WriteAllText(damagedBackup, "damaged");
var restorePolicy = new GlobalPolicy { ManagedFolders = new() { globalGood.Installer.Target, globalBad.Installer.Target } };
int restoreSaves = 0;
var globalRestore = GlobalApply.Restore(restorePolicy, () => restoreSaves++);
Check(globalRestore.restored == 1 && globalRestore.failed.Count == 1 && restoreSaves == 1, "Global OFF restores healthy games and retains failed ownership");
Check(Original(globalGood.Installer) && !Directory.Exists(globalGood.Installer.Store) && Directory.Exists(globalBad.Installer.Store), "Global OFF cleans completed recovery while preserving damaged backup journal");
File.WriteAllBytes(damagedBackup, savedBackup);
globalRestore = GlobalApply.Restore(restorePolicy, () => restoreSaves++);
Check(globalRestore.failed.Count == 0 && restorePolicy.ManagedFolders.Count == 0 && Original(globalBad.Installer), "Failed global recovery can be retried after backup repair");

if (args.Contains("--live"))
{
    Updates.DataRoot = Path.Combine(sandbox, "live-cache");
    var live = Updates.FetchLatest(Payload.Baseline(), Console.WriteLine);
    Check(live.Files.All(file => Disk.Hash(Updates.FileFor(live, file.Name)) == file.Hash), "Live upstream source ZIP and hashes");
    string game = Path.Combine(sandbox, "live-game"); Directory.CreateDirectory(game);
    File.WriteAllBytes(Path.Combine(game, "version.dll"), Pe(99));
    var installer = new Installer(game); installer.Enable("version.dll", Updates.DirectoryFor(live));
    installer.SaveIniSettings(new RuntimeIniSettings { Optimized = 1, MaxGeneratedFrames = 3 });
    Check(installer.Status() == "적용됨", "Install real runtime and save INI in isolated fixture");
    installer.Restore(); Check(Original(installer) && !Directory.Exists(installer.Store), "Restore real runtime fixture cleanly");
    Console.WriteLine("Verified upstream runtime " + live.Version);
}
Console.WriteLine($"{checks} checks passed. Fixtures: {sandbox}");
