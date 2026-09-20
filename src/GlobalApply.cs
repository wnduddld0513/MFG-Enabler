using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MfgEnabler;

public sealed class ProxyRecommendationRecord
{
    public string Key { get; init; }
    public string Exe { get; init; }
    public string Proxy { get; init; }
    public string Result { get; init; }
}

public static class ProxyRecommendations
{
    static string Escape(string value) => Uri.EscapeDataString(value ?? "");
    static string Unescape(string value) => Uri.UnescapeDataString(value ?? "");

    static Dictionary<string, ProxyRecommendationRecord> Read(string file)
    {
        var records = new Dictionary<string, ProxyRecommendationRecord>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(file)) return records;
        Disk.Safe(file);
        foreach (string line in File.ReadLines(file))
        {
            if (String.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            string[] fields = line.Split('\t');
            if (fields.Length != 4) continue;
            var record = new ProxyRecommendationRecord
            {
                Key = Unescape(fields[0]),
                Exe = Unescape(fields[1]),
                Proxy = Unescape(fields[2]),
                Result = Unescape(fields[3])
            };
            if (!String.IsNullOrWhiteSpace(record.Key)) records[record.Key] = record;
        }
        return records;
    }

    static void Write(string file, Dictionary<string, ProxyRecommendationRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        var lines = records.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x =>
            String.Join("\t", Escape(x.Key), Escape(x.Exe), Escape(x.Proxy), Escape(x.Result)));
        string text = "# MFG Enabler proxy recommendations v1\r\n" + String.Join("\r\n", lines);
        if (records.Count != 0) text += "\r\n";
        Disk.Write(file, Encoding.UTF8.GetBytes(text));
    }

    public static ProxyRecommendationRecord Get(Game game, string file = null)
    {
        if (game == null) return null;
        file ??= AppPaths.ProxyRecommendationsFile;
        return Read(file).GetValueOrDefault(GlobalPolicy.Key(game));
    }

    public static ProxyRecommendationRecord DetectAndStore(Game game, bool force = false, string ownedProxy = null,
        string file = null, Func<string, string, ProxyDetectionResult> detector = null)
    {
        if (game == null || String.IsNullOrWhiteSpace(game.Exe)) throw new IOException("Rendering executable is missing.");
        file ??= AppPaths.ProxyRecommendationsFile;
        var records = Read(file);
        string key = GlobalPolicy.Key(game);
        if (!force && records.TryGetValue(key, out var cached)) return cached;

        detector ??= (exe, owned) => ProxyDetector.Detect(exe, owned);
        ProxyDetectionResult detected = detector(game.Exe, ownedProxy);
        string result = detected.Selected != null ? "selected" :
            detected.Ambiguous != null && detected.Ambiguous.Count != 0 ? "ambiguous:" + String.Join(",", detected.Ambiguous) : "none";
        var record = new ProxyRecommendationRecord
        {
            Key = key,
            Exe = Path.GetFullPath(game.Exe),
            Proxy = detected.Selected ?? "",
            Result = result
        };
        records[key] = record;
        Write(file, records);
        return record;
    }
}

public static class GlobalApply
{
    public static List<string> Apply(GlobalPolicy policy, IEnumerable<Game> games, Action save,
        Action prepare = null, Action<Game> validate = null, Action<Game> install = null,
        Action<Game, string> installWithProxy = null)
    {
        policy.Normalize();
        var messages = new List<string>();
        bool prepared = false;
        var preparedProxies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Exception preparationError = null;
        foreach (var game in games.Where(policy.Allows).GroupBy(g => Path.GetDirectoryName(g.Exe), StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            string folder = Path.GetDirectoryName(game.Exe);
            try
            {
                var target = new Installer(folder);
                if (target.Status() == "적용됨")
                {
                    if (!policy.ManagedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) { policy.ManagedFolders.Add(folder); save(); }
                    continue;
                }
                if (Installer.HasAny(folder)) throw new IOException("Recovery required before automatic installation.");
                (validate ?? (g => Discovery.ValidateFreshCandidate(g, g.Exe)))(game);
                var recommendation = ProxyRecommendations.DetectAndStore(game);
                string proxy = recommendation.Proxy;
                if (String.IsNullOrWhiteSpace(proxy))
                    throw new IOException(recommendation.Result.StartsWith("ambiguous:")
                        ? "Proxy recommendation is ambiguous: " + recommendation.Result.Substring("ambiguous:".Length)
                        : "No suitable proxy DLL was detected.");
                if (preparationError != null) throw new IOException("Runtime preparation failed; retry after resolving the download error.", preparationError);
                if (prepare != null && !prepared)
                {
                    try { prepare(); prepared = true; }
                    catch (Exception error) { preparationError = error; throw; }
                }
                else if (prepare == null && preparedProxies.Add(proxy))
                {
                    try { Payload.Ensure(proxy, _ => { }); }
                    catch (Exception error) { preparationError = error; throw; }
                }
                (validate ?? (g => Discovery.ValidateFreshCandidate(g, g.Exe)))(game);
                // Persist ownership before installation, including an interrupted operation.
                if (!policy.ManagedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) policy.ManagedFolders.Add(folder);
                save();
                if (installWithProxy != null) installWithProxy(game, proxy);
                else if (install != null) install(game);
                else new Installer(Path.GetDirectoryName(game.Exe)).Enable(proxy, Payload.Cache);
                messages.Add(game.Name + ": Applied");
            }
            catch (Exception error) { messages.Add(game.Name + ": " + error.Message); }
        }
        return messages;
    }

    public static (int restored, List<string> failed) Restore(GlobalPolicy policy, Action save)
    {
        policy.Normalize();
        int restored = 0;
        var failed = new List<string>();
        foreach (string folder in policy.ManagedFolders.ToArray())
        {
            try { if (Installer.HasAny(folder)) Installer.RestoreAll(folder); restored++; }
            catch { failed.Add(folder); }
        }
        policy.ManagedFolders = failed;
        save();
        return (restored, failed);
    }
}
