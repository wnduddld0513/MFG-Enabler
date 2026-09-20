using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace MfgEnabler;
internal static class AppPaths
{
    public static bool IsIsolated => Environment.GetEnvironmentVariable("MFG_ENABLER_DATA_DIR") != null;
    public static string DataDir => Path.GetFullPath(Environment.GetEnvironmentVariable("MFG_ENABLER_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MFG Enabler"));
    public static string InstanceName => @"Local\MFG-Enabler-1" + (Environment.GetEnvironmentVariable("MFG_ENABLER_DATA_DIR") == null ? "" : "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDir)))[..12]);
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ProxyRecommendationsFile => Path.Combine(DataDir, "proxy-recommendations.txt");
}
