using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MfgEnabler;

public sealed class RuntimeIniSettings
{
    public int Optimized = 1;
    public int MaxGeneratedFrames = 3;
    public string Preset = "Auto";
    public int LogLevel = 1;

    public void Validate()
    {
        if (Optimized < 0 || Optimized > 3 || MaxGeneratedFrames < 0 || MaxGeneratedFrames > 5 ||
            LogLevel < 0 || LogLevel > 3 || (Preset != "Auto" && Preset != "A" && Preset != "B"))
            throw new IOException("Invalid runtime INI settings.");
    }
}

public static class RuntimeIni
{
    static readonly string[] Keys = { "FrameGeneration/Optimized", "FrameGeneration/MaxGeneratedFrames", "Compatibility/Preset", "Logging/Level" };
    static string Decode(byte[] bytes) { Updates.ValidateIni(bytes); return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
    static string Key(string section, string line)
    {
        int eq = line.IndexOf('=');
        return eq <= 0 || line.StartsWith(';') || line.StartsWith('#') ? null : section + "/" + line.Substring(0, eq).Trim();
    }
    public static RuntimeIniSettings Read(byte[] bytes)
    {
        string section = "";
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in Decode(bytes).Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            string key = Key(section, line);
            if (key == null || (!Keys.Contains(key, StringComparer.OrdinalIgnoreCase) && !key.Equals("Compatibility/OptimizedKernels", StringComparison.OrdinalIgnoreCase))) continue;
            if (!values.TryAdd(key, line[(line.IndexOf('=') + 1)..].Split(';', '#')[0].Trim()))
                throw new IOException("Duplicate INI setting: " + key);
        }
        int Number(string key, int fallback)
        {
            if (!values.TryGetValue(key, out string text)) return fallback;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) throw new IOException("Invalid INI setting: " + key);
            return value;
        }
        var result = new RuntimeIniSettings {
            // With neither Optimized nor its legacy alias present, upstream uses tier 0.
            Optimized = values.ContainsKey(Keys[0]) ? Number(Keys[0], 0) : Number("Compatibility/OptimizedKernels", 0), MaxGeneratedFrames = Number(Keys[1], 3),
            Preset = values.TryGetValue(Keys[2], out string preset) ? (preset.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "Auto" : preset.ToUpperInvariant()) : "Auto",
            LogLevel = Number(Keys[3], 1)
        };
        result.Validate(); return result;
    }
    public static byte[] Apply(byte[] bytes, RuntimeIniSettings settings)
    {
        settings.Validate();
        string text = Decode(bytes), newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var values = new[] { settings.Optimized.ToString(CultureInfo.InvariantCulture), settings.MaxGeneratedFrames.ToString(CultureInfo.InvariantCulture), settings.Preset, settings.LogLevel.ToString(CultureInfo.InvariantCulture) };
        for (int k = 0; k < Keys.Length; k++)
        {
            string[] pair = Keys[k].Split('/'); string section = ""; bool written = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
                if (!String.Equals(Key(section, line), Keys[k], StringComparison.OrdinalIgnoreCase)) continue;
                if (written) { lines.RemoveAt(i--); continue; }
                int comment = line.IndexOfAny(new[] { ';', '#' }, line.IndexOf('=') + 1);
                lines[i] = pair[1] + "=" + values[k] + (comment < 0 ? "" : " " + line[comment..]); written = true;
            }
            if (written) continue;
            int at = lines.FindIndex(line => line.Trim().Equals("[" + pair[0] + "]", StringComparison.OrdinalIgnoreCase));
            if (at < 0) { if (lines.Count > 0 && lines[^1].Length != 0) lines.Add(""); lines.Add("[" + pair[0] + "]"); at = lines.Count - 1; }
            lines.Insert(at + 1, pair[1] + "=" + values[k]);
        }
        byte[] result = new UTF8Encoding(false).GetBytes(String.Join(newline, lines));
        Read(result); return result;
    }
}
