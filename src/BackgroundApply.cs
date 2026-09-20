using System;
using System.IO;
using System.Collections.Generic;
namespace MfgEnabler;
internal static class BackgroundApply
{
    public static int Run()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return 0;
            var settings = Disk.Read<DesktopSettings>(AppPaths.SettingsFile) ?? new();
            settings.Global ??= new(); settings.Global.Normalize();
            if (!settings.Global.Enabled) return 0;
            string library = Path.Combine(AppPaths.DataDir, "games.json");
            var saved = File.Exists(library) ? Disk.Read<List<Game>>(library) ?? new() : new();
            var report = Discovery.Scan();
            var games = Discovery.MergeLibrary(saved, report.Games);
            var messages = GlobalApply.Apply(settings.Global, games, () => Disk.Save(AppPaths.SettingsFile, settings));
            Disk.Save(library, games);
            File.WriteAllLines(Path.Combine(AppPaths.DataDir, "background-result.txt"), messages);
            return 0;
        }
        catch (Exception error)
        {
            try { Directory.CreateDirectory(AppPaths.DataDir); File.WriteAllText(Path.Combine(AppPaths.DataDir, "background-result.txt"), error.Message); } catch { }
            return 1;
        }
    }
}
