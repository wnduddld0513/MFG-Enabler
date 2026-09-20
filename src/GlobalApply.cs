using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MfgEnabler;

public static class GlobalApply
{
    public static List<string> Apply(GlobalPolicy policy, IEnumerable<Game> games, Action save,
        Action prepare = null, Action<Game> validate = null, Action<Game> install = null)
    {
        policy.Normalize();
        var messages = new List<string>();
        bool prepared = false;
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
                if (preparationError != null) throw new IOException("Runtime preparation failed; retry after resolving the download error.", preparationError);
                if (!prepared)
                {
                    try { (prepare ?? (() => Payload.Ensure("version.dll", _ => { })))(); prepared = true; }
                    catch (Exception error) { preparationError = error; throw; }
                }
                (validate ?? (g => Discovery.ValidateFreshCandidate(g, g.Exe)))(game);
                // Persist ownership before installation, including an interrupted operation.
                if (!policy.ManagedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) policy.ManagedFolders.Add(folder);
                save();
                (install ?? (g => new Installer(Path.GetDirectoryName(g.Exe)).Enable("version.dll", Payload.Cache)))(game);
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
