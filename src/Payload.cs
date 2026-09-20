using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MfgEnabler;

public static class Payload
{
	public const string Revision = "196fcb61ef414992a6bef2d237ff609aab09f0d9";

	public const string BaselineVersion = "0.3.4";

	public static readonly string[] Proxies = new string[6] { "version.dll", "winmm.dll", "dinput8.dll", "dxgi.dll", "d3d12.dll", "dbghelp.dll" };

	private static string cacheOverride;

	public static string Cache
	{
		get
		{
			return cacheOverride ?? Updates.DirectoryFor(Updates.Current());
		}
		set
		{
			cacheOverride = value;
		}
	}

	public static RuntimePackage Baseline()
	{
		using StreamReader streamReader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.lock"));
		List<PackageFile> files = (from x in streamReader.ReadToEnd().Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
			select x.Trim('\ufeff').Split('|') into x
			select new PackageFile
			{
				Name = x[0],
				Path = x[1],
				Hash = x[2],
				Blob = x[3]
			}).ToList();
		return new RuntimePackage
		{
			Revision = "196fcb61ef414992a6bef2d237ff609aab09f0d9",
			Version = "0.3.4",
			Channel = "dlssg_for_sm86",
			Files = files
		};
	}

	public static Dictionary<string, string[]> Specs()
	{
		return Updates.Current().Files.ToDictionary((PackageFile f) => f.Name, (PackageFile f) => new string[4] { f.Name, f.Path, f.Hash, f.Blob });
	}

	public static void Ensure(string proxy, Action<string> progress)
	{
		RuntimePackage p = Updates.Current();
		p = Updates.Prepare(p, progress);
		string[] array = new string[2] { proxy, "dlssg_sm86.ini" };
		foreach (string name in array)
		{
			Updates.Ensure(p, name, progress, cacheOverride);
		}
	}
}
