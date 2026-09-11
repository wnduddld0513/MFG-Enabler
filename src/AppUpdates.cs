using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MfgEnabler;

internal sealed record AppRelease(string Version, string Channel, string Url, long Size, string Digest);
internal sealed record ReleaseNumber(Version Core, int? Beta) : IComparable<ReleaseNumber>
{
    public static ReleaseNumber Parse(string value)
    {
        var m = Regex.Match(value ?? "", @"\A(?:v)?(\d+\.\d+(?:\.\d+)?(?:\.\d+)?)(?:b([1-9]\d*))?\z");
        if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var v)) return null;
        int? beta = null;
        if (m.Groups[2].Success) { if (!int.TryParse(m.Groups[2].Value, out int b)) return null; beta = b; }
        return new(new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)), beta);
    }
    public int CompareTo(ReleaseNumber other)
    {
        int c = Core.CompareTo(other.Core);
        return c != 0 ? c : (Beta ?? int.MaxValue).CompareTo(other.Beta ?? int.MaxValue);
    }
}

internal static class AppUpdates
{
    const string Repository = "https://github.com/wnduddld0513/MFG-Enabler/";
    const string Api = "https://api.github.com/repos/wnduddld0513/MFG-Enabler/releases";
    const long MaxZip = 1024L * 1024 * 1024, MaxExtracted = 3L * 1024 * 1024 * 1024;
    public static string CurrentVersion => typeof(AppUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0";
    static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MFG-Enabler/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
    public static AppRelease SelectRelease(string json, string channel, string current)
    {
        if (channel != "main" && channel != "beta") throw new ArgumentException("Unknown update channel.");
        var installed = ReleaseNumber.Parse(current) ?? throw new IOException("Invalid installed application version.");
        using var doc = JsonDocument.Parse(json);
        AppRelease best = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("target_commitish").GetString() != channel) continue;
            string tag = release.GetProperty("tag_name").GetString()?.TrimStart('v');
            var number = ReleaseNumber.Parse(tag);
            if (number == null || (number.Beta != null) != (channel == "beta")) continue;
            if (channel == "main" && release.GetProperty("prerelease").GetBoolean()) continue;
            bool switchingChannel = (installed.Beta != null) != (channel == "beta");
            if (switchingChannel ? number.Core.CompareTo(installed.Core) < 0 : number.CompareTo(installed) <= 0) continue;
            string expected = "MFG-Enabler-Package-" + tag + ".zip";
            var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == expected && a.GetProperty("state").GetString() == "uploaded").ToArray();
            if (assets.Length != 1) continue;
            var asset = assets[0];
            string url = asset.GetProperty("browser_download_url").GetString();
            long size = asset.GetProperty("size").GetInt64();
            string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            if (size <= 0 || size > MaxZip || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !url.StartsWith(Repository + "releases/download/", StringComparison.Ordinal)) continue;
            if (!Regex.IsMatch(digest ?? "", @"\Asha256:[0-9a-fA-F]{64}\z")) continue;
            if (best == null || number.CompareTo(ReleaseNumber.Parse(best.Version)) > 0)
                best = new(tag, channel, url, size, digest.Substring(7));
        }
        return best;
    }
    public static async Task<AppRelease> Check(string channel)
    {
        using var client = Client();
        AppRelease best = null;
        for (int page = 1; page <= 20; page++)
        {
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            string json = await client.GetStringAsync(Api + "?per_page=100&page=" + page, timeout.Token);
            var candidate = SelectRelease(json, channel, CurrentVersion);
            if (candidate != null && (best == null || ReleaseNumber.Parse(candidate.Version).CompareTo(ReleaseNumber.Parse(best.Version)) > 0)) best = candidate;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() < 100) return best;
        }
        throw new IOException("Too many release pages. Please check the releases page manually.");
    }
    internal static void ExtractPackage(string zip, string destination, string version)
    {
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 10000) throw new IOException("Too many ZIP entries.");
        var executables = archive.Entries.Where(e => e.Name.Equals("MFG-Enabler.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (executables.Length != 1) throw new IOException("The package must contain exactly one MFG-Enabler.exe.");
        string prefix = executables[0].FullName.Substring(0, executables[0].FullName.Length - executables[0].Name.Length);
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destination);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) throw new IOException("Files outside the application package folder.");
            string relative = entry.FullName.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p == ".." || p == "." || p.Length == 0 || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw new IOException("Unsafe ZIP path.");
            string target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !names.Add(target) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Unsafe or duplicate ZIP entry.");
            total = checked(total + entry.Length);
            if (total > MaxExtracted) throw new IOException("The extracted package is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            entry.ExtractToFile(target);
        }
        foreach (string name in new[] { "MFG-Enabler.exe", "MFG-Enabler.dll", "MFG-Enabler.deps.json", "MFG-Enabler.runtimeconfig.json", "Microsoft.ui.xaml.dll", "coreclr.dll", "hostfxr.dll", "Assets/MFG-Enabler.ico" })
            if (!File.Exists(Path.Combine(destination, name))) throw new IOException("Incomplete application package: " + name);
        string actual = FileVersionInfo.GetVersionInfo(Path.Combine(destination, "MFG-Enabler.dll")).ProductVersion?.Split('+')[0];
        if (ReleaseNumber.Parse(actual) is not { } number || number.CompareTo(ReleaseNumber.Parse(version)) != 0) throw new IOException("Package version does not match its release.");
    }
    public static async Task<string> Prepare(AppRelease release)
    {
        string target = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string job = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MFG Enabler", "app-updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        string zip = Path.Combine(job, "package.zip"), stage = Path.Combine(job, "stage");
        using (var client = Client())
        using (var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new FileStream(zip, FileMode.CreateNew);
            byte[] buffer = new byte[81920]; long count = 0; int read;
            while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                count += read;
                if (count > release.Size || count > MaxZip) throw new IOException("Unexpected download size.");
                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            if (count != release.Size) throw new IOException("Incomplete download.");
        }
        using (var input = File.OpenRead(zip))
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(release.Digest, StringComparison.OrdinalIgnoreCase)) throw new IOException("Application package SHA-256 mismatch.");
        await Task.Run(() => ExtractPackage(zip, stage, release.Version));
        string helper = Path.Combine(job, "apply.ps1");
        using (var resource = typeof(AppUpdates).Assembly.GetManifestResourceStream("app-update.ps1"))
        using (var output = File.Create(helper)) resource.CopyTo(output);
        var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Select(file => new { path = Path.GetRelativePath(stage, file), hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) }).ToArray();
        File.WriteAllText(Path.Combine(job, "job.json"), JsonSerializer.Serialize(new { target, stage, processId = Environment.ProcessId, processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(), files }));
        File.Delete(zip);
        return job;
    }
    public static async Task StartHelper(string job)
    {
        bool elevate = false;
        string target = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string probe = Path.Combine(target, ".mfg-write-" + Guid.NewGuid().ToString("N"));
        try { using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { } }
        catch (UnauthorizedAccessException) { elevate = true; }
        catch (IOException) { elevate = true; }
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = elevate, CreateNoWindow = !elevate, WorkingDirectory = job,
            Verb = elevate ? "runas" : "", WindowStyle = ProcessWindowStyle.Hidden
        };
        if (!elevate) start.Environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "Modules");
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(job, "apply.ps1"), "-JobDirectory", job }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start the updater.");
        for (int n = 0; n < 100; n++)
        {
            if (File.Exists(Path.Combine(job, "ready"))) return;
            if (process.HasExited) throw new IOException("Updater initialization failed. See " + Path.Combine(job, "result.txt"));
            await Task.Delay(100);
        }
        throw new IOException("Updater did not become ready. The application remains open.");
    }
}
