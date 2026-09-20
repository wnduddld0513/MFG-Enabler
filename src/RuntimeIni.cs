using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MfgEnabler;

public static class RuntimeIni
{
	private static readonly string[] Keys = new string[4] { "FrameGeneration/Optimized", "FrameGeneration/MaxGeneratedFrames", "Compatibility/Preset", "Logging/Level" };

	private static string Decode(byte[] bytes)
	{
		Updates.ValidateIni(bytes);
		return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('\ufeff');
	}

	private static string Key(string section, string line)
	{
		int num = line.IndexOf('=');
		if (num > 0 && !line.StartsWith(';') && !line.StartsWith('#'))
		{
			return section + "/" + line.Substring(0, num).Trim();
		}
		return null;
	}

	public static RuntimeIniSettings Read(byte[] bytes)
	{
		string section = "";
		Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] array = Decode(bytes).Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.StartsWith('[') && text.EndsWith(']'))
			{
				string text2 = text;
				section = text2.Substring(1, text2.Length - 1 - 1).Trim();
				continue;
			}
			string text3 = Key(section, text);
			if (text3 != null && (Keys.Contains<string>(text3, StringComparer.OrdinalIgnoreCase) || text3.Equals("Compatibility/OptimizedKernels", StringComparison.OrdinalIgnoreCase)) && !values.TryAdd(text3, text.Substring(text.IndexOf('=') + 1).Split(';', '#')[0].Trim()))
			{
				throw new IOException("Duplicate INI setting: " + text3);
			}
		}
		RuntimeIniSettings runtimeIniSettings = new RuntimeIniSettings();
		runtimeIniSettings.Optimized = (values.ContainsKey(Keys[0]) ? Number(Keys[0], 0) : Number("Compatibility/OptimizedKernels", 0));
		runtimeIniSettings.MaxGeneratedFrames = Number(Keys[1], 3);
		runtimeIniSettings.Preset = ((!values.TryGetValue(Keys[2], out var value)) ? "Auto" : (value.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "Auto" : value.ToUpperInvariant()));
		runtimeIniSettings.LogLevel = Number(Keys[3], 1);
		runtimeIniSettings.Validate();
		return runtimeIniSettings;
		int Number(string key, int fallback)
		{
			if (!values.TryGetValue(key, out var value2))
			{
				return fallback;
			}
			if (!int.TryParse(value2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				throw new IOException("Invalid INI setting: " + key);
			}
			return result;
		}
	}

	public static byte[] Apply(byte[] bytes, RuntimeIniSettings settings)
	{
		settings.Validate();
		string text = Decode(bytes);
		string separator = (text.Contains("\r\n") ? "\r\n" : "\n");
		List<string> list = text.Replace("\r\n", "\n").Split('\n').ToList();
		string[] array = new string[4]
		{
			settings.Optimized.ToString(CultureInfo.InvariantCulture),
			settings.MaxGeneratedFrames.ToString(CultureInfo.InvariantCulture),
			settings.Preset,
			settings.LogLevel.ToString(CultureInfo.InvariantCulture)
		};
		for (int i = 0; i < Keys.Length; i++)
		{
			string[] pair = Keys[i].Split('/');
			string section = "";
			bool flag = false;
			for (int j = 0; j < list.Count; j++)
			{
				string text2 = list[j].Trim();
				if (text2.StartsWith('[') && text2.EndsWith(']'))
				{
					string text3 = text2;
					section = text3.Substring(1, text3.Length - 1 - 1).Trim();
				}
				else if (string.Equals(Key(section, text2), Keys[i], StringComparison.OrdinalIgnoreCase))
				{
					if (flag)
					{
						list.RemoveAt(j--);
						continue;
					}
					int num = text2.IndexOfAny(new char[2] { ';', '#' }, text2.IndexOf('=') + 1);
					list[j] = pair[1] + "=" + array[i] + ((num < 0) ? "" : (" " + text2.Substring(num)));
					flag = true;
				}
			}
			if (flag)
			{
				continue;
			}
			int num2 = list.FindIndex((string line) => line.Trim().Equals("[" + pair[0] + "]", StringComparison.OrdinalIgnoreCase));
			if (num2 < 0)
			{
				if (list.Count > 0)
				{
					if (list[list.Count - 1].Length != 0)
					{
						list.Add("");
					}
				}
				list.Add("[" + pair[0] + "]");
				num2 = list.Count - 1;
			}
			list.Insert(num2 + 1, pair[1] + "=" + array[i]);
		}
		byte[] bytes2 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(string.Join(separator, list));
		Read(bytes2);
		return bytes2;
	}
}
