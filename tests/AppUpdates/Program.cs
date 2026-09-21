using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MfgEnabler;

if (args.Length == 5 && args[0] == "--verify-release")
{
    var release = AppUpdates.SelectRelease(File.ReadAllText(args[1]), args[3], args[4])
        ?? throw new Exception("Release is not offered to the installed version/channel.");
    using (var stream = File.OpenRead(args[2]))
        if (stream.Length != release.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(release.Digest, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Release asset size or digest mismatch.");
    string stage = Path.Combine(Path.GetTempPath(), "MFG-release-validation-" + Guid.NewGuid().ToString("N"));
    AppUpdates.ExtractPackage(args[2], stage, release.Version);
    Console.WriteLine("PASS published release selection, SHA-256, extraction and embedded version: " + release.Version);
    return;
}

if (File.Exists(Path.Combine(AppContext.BaseDirectory, "restart-probe")))
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "restarted"), "ok");
    return;
}
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
object Release(string version, string branch, bool prerelease = false, string assetName = null, string digest = null) => new {
    draft = false, target_commitish = branch, tag_name = "v" + version, prerelease,
    assets = new[] { new { name = assetName ?? "MFG-Enabler-Package-" + version + ".zip", state = "uploaded", size = 100,
        browser_download_url = "https://github.com/wnduddld0513/MFG-Enabler/releases/download/v" + version + "/package.zip", digest = digest ?? "sha256:" + new string('a', 64) } }
};
string releases = JsonSerializer.Serialize(new[] { Release("1.1", "main"), Release("1.2b2", "beta", true), Release("1.2b10", "beta", true), Release("9.0b1", "main", true), Release("9.0", "beta") });
Check(AppUpdates.SelectRelease(releases, "main", "1.0")?.Version == "1.1", "Stable excludes Beta and wrong branches");
Check(AppUpdates.SelectRelease(releases, "beta", "1.0b1")?.Version == "1.2b10", "Beta picks newest eligible release with numeric beta ordering");
Check(AppUpdates.SelectRelease(releases, "main", "1.1") == null, "No repeat for installed version");
Check(AppUpdates.SelectRelease(releases, "beta", "2.0b1") == null, "No older base version");
Check(AppUpdates.SelectRelease(JsonSerializer.Serialize(new[] { Release("1.0b1", "beta", true) }), "beta", "1.0") == null, "Stable on Beta channel does not downgrade to an older beta");
string graduation = JsonSerializer.Serialize(new[] { Release("1.4b2", "beta", true), Release("1.4", "main") });
Check(AppUpdates.SelectRelease(graduation, "beta", "1.4b2")?.Version == "1.4", "Beta users receive the final stable release");
Check(AppUpdates.SelectRelease(graduation, "beta", "1.4") == null, "Shared stable release is not repeatedly offered on Beta");
Check(AppUpdates.SelectRelease(JsonSerializer.Serialize(new[] { Release("1.4", "main"), Release("1.5b1", "beta", true) }), "beta", "1.4")?.Version == "1.5b1", "Beta users continue to receive the next beta series");
Check(AppUpdates.SelectRelease(JsonSerializer.Serialize(new[] { Release("1.5", "main", true) }), "beta", "1.4") == null, "Reject prerelease mislabeled as stable");
Check(AppUpdates.SelectRelease(JsonSerializer.Serialize(new[] { Release("1.0", "main") }), "main", "1.0b3") != null, "Explicit Beta to Stable switch");
Check(AppUpdates.SelectRelease(JsonSerializer.Serialize(new[] { Release("1.1", "main", assetName: "installer.msi") }), "main", "1.0") == null, "MSI is ignored");
Check(AppUpdates.SelectRelease(JsonSerializer.Serialize(new[] { Release("1.1", "main", digest: "invalid") }), "main", "1.0") == null, "Unverifiable asset is ignored");
Check(ReleaseNumber.Parse("1.0b0") == null && ReleaseNumber.Parse("1.0b999999999999") == null, "Invalid beta versions rejected");

string sandbox = Path.Combine(Path.GetTempPath(), "MFG-updater-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
void Zip(string path, string extra = null)
{
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var name in new[] { "MFG-Enabler.exe", "MFG-Enabler.deps.json", "MFG-Enabler.runtimeconfig.json", "Microsoft.ui.xaml.dll", "coreclr.dll", "hostfxr.dll", "Assets/MFG-Enabler.ico" })
    { using var writer = new StreamWriter(zip.CreateEntry("package/" + name).Open()); writer.Write("fixture"); }
    zip.CreateEntryFromFile(typeof(AppUpdates).Assembly.Location, "package/MFG-Enabler.dll");
    if (extra != null) { using var writer = new StreamWriter(zip.CreateEntry(extra).Open()); writer.Write("bad"); }
}
string valid = Path.Combine(sandbox, "valid.zip"); Zip(valid);
AppUpdates.ExtractPackage(valid, Path.Combine(sandbox, "valid"), "1.0");
Check(File.Exists(Path.Combine(sandbox, "valid", "Assets", "MFG-Enabler.ico")), "Wrapped package extracts");
foreach (var pair in new[] { ("traversal", "package/../outside.txt"), ("ads", "package/file:stream"), ("duplicate", "package/MFG-Enabler.exe"), ("outside", "elsewhere.txt") })
{
    string zip = Path.Combine(sandbox, pair.Item1 + ".zip"); Zip(zip, pair.Item2);
    bool rejected = false;
    try { AppUpdates.ExtractPackage(zip, Path.Combine(sandbox, pair.Item1), "1.0"); } catch (IOException) { rejected = true; }
    Check(rejected, "Reject " + pair.Item1);
}
bool mismatch = false;
try { AppUpdates.ExtractPackage(valid, Path.Combine(sandbox, "mismatch"), "1.1"); } catch (IOException) { mismatch = true; }
Check(mismatch, "Reject mismatched package version");

string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
ProcessStartInfo Shell(params string[] args)
{
    var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true };
    start.Environment["PSModulePath"] = Path.Combine(Path.GetDirectoryName(powershell), "Modules");
    foreach (string arg in args) start.ArgumentList.Add(arg);
    return start;
}
async Task Transaction(string name, bool fail)
{
    string job = Path.Combine(sandbox, name), stage = Path.Combine(job, "stage"), target = Path.Combine(job, "installed");
    Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
    File.WriteAllText(Path.Combine(target, "first.txt"), "old");
    File.WriteAllText(Path.Combine(target, "user.txt"), "preserve");
    File.WriteAllText(Path.Combine(stage, "first.txt"), "new");
    File.WriteAllText(Path.Combine(stage, "second.txt"), "second");
    if (fail) Directory.CreateDirectory(Path.Combine(target, "second.txt"));
    using var parent = Process.Start(Shell("-NoProfile", "-Command", "Start-Sleep -Seconds 2"));
    File.WriteAllText(Path.Combine(job, "job.json"), JsonSerializer.Serialize(new {
        target, stage, processId = parent.Id, processStart = parent.StartTime.ToUniversalTime().Ticks.ToString(),
        files = new[] { "first.txt", "second.txt" }.Select(path => new { path, hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(stage, path)))) }).ToArray()
    }));
    File.WriteAllText(Path.Combine(job, "commit"), "apply");
    using var helper = Process.Start(Shell("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "app-update.ps1"), "-JobDirectory", job, "-NoRestart"));
    await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    string result = File.ReadAllText(Path.Combine(job, "result.txt"));
    Check(result.StartsWith(fail ? "Update failed:" : "Update completed."), name + " result: " + result.Split('\n')[0]);
    Check(File.ReadAllText(Path.Combine(target, "first.txt")) == (fail ? "old" : "new"), name + " content");
    Check(File.ReadAllText(Path.Combine(target, "user.txt")) == "preserve", name + " preserves unrelated files");
}
await Transaction("success", false);
await Transaction("rollback", true);
string restartJob = Path.Combine(sandbox, "restart"), restartStage = Path.Combine(restartJob, "stage"), restartTarget = Path.Combine(restartJob, "installed");
Directory.CreateDirectory(restartStage); Directory.CreateDirectory(restartTarget);
foreach (string source in Directory.GetFiles(AppContext.BaseDirectory))
{
    string name = Path.GetFileName(source);
    if (name == "AppUpdates.Tests.exe") name = "MFG-Enabler.exe";
    File.Copy(source, Path.Combine(restartStage, name));
}
File.WriteAllText(Path.Combine(restartStage, "restart-probe"), "test fixture");
using (var parent = Process.Start(Shell("-NoProfile", "-Command", "Start-Sleep -Seconds 2")))
{
    File.WriteAllText(Path.Combine(restartJob, "job.json"), JsonSerializer.Serialize(new {
        target = restartTarget, stage = restartStage, processId = parent.Id, processStart = parent.StartTime.ToUniversalTime().Ticks.ToString(),
        files = Directory.GetFiles(restartStage).Select(file => new { path = Path.GetFileName(file), hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) }).ToArray()
    }));
    File.WriteAllText(Path.Combine(restartJob, "commit"), "apply");
    using var helper = Process.Start(Shell("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "app-update.ps1"), "-JobDirectory", restartJob));
    await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    for (int n = 0; n < 50 && !File.Exists(Path.Combine(restartTarget, "restarted")); n++) await Task.Delay(100);
    Check(File.Exists(Path.Combine(restartTarget, "restarted")), "Helper restarts installed fixture executable");
}
Console.WriteLine($"{checks} checks passed. Fixtures: {sandbox}");
