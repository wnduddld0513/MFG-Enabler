using System.IO;

namespace MfgEnabler;

public sealed class RuntimeIniSettings
{
	public int Optimized = 1;

	public int MaxGeneratedFrames = 3;

	public string Preset = "Auto";

	public int LogLevel = 1;

	public void Validate()
	{
		if (Optimized < 0 || Optimized > 3 || MaxGeneratedFrames < 0 || MaxGeneratedFrames > 5 || LogLevel < 0 || LogLevel > 3 || (Preset != "Auto" && Preset != "A" && Preset != "B"))
		{
			throw new IOException("Invalid runtime INI settings.");
		}
	}
}
