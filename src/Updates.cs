using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MfgEnabler;

public static class Updates
{
	public const string Channel = "dlssg_for_sm86";

	private const string Api = "https://api.github.com/repos/sdli1995/dlssg_for_sm86/";

	public static string DataRoot { get; internal set; } = Path.Combine(Environment.GetEnvironmentVariable("MFG_ENABLER_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MFG Enabler"), "payload");

	private static string ActiveFile => Path.Combine(DataRoot, "active.json");

	private static string MetadataFile => Path.Combine(DataRoot, "sdli1995.json");

	private static IEnumerable<string> Names => Payload.Proxies.Concat(new string[1] { "dlssg_sm86.ini" });

	private static bool HashLike(string value, int length)
	{
		if (value != null)
		{
			return Regex.IsMatch(value, "\\A[a-f0-9]{" + length + "}\\z");
		}
		return false;
	}

	public static string RemotePath(string name)
	{
		if (name == "version.dll" || name == "dlssg_sm86.ini")
		{
			return name;
		}
		if (Payload.Proxies.Contains(name))
		{
			return "alternatives/" + name;
		}
		throw new IOException("Unknown runtime file: " + name);
	}

	public static void Validate(RuntimePackage p)
	{
		if (p == null || !HashLike(p.Revision, 40) || !Version.TryParse(p.Version, out Version result) || result < new Version("0.3.4") || p.Channel != "dlssg_for_sm86" || p.Files == null || p.Files.Count != Names.Count())
		{
			throw new IOException("Invalid runtime package metadata.");
		}
		if (p.Files.Any((PackageFile f) => f == null) || p.Files.Select((PackageFile f) => f.Name).Distinct<string>(StringComparer.OrdinalIgnoreCase).Count() != Names.Count() || p.Files.Any((PackageFile f) => !Names.Contains(f.Name) || f.Path != RemotePath(f.Name) || !HashLike(f.Hash, 64) || !HashLike(f.Blob, 40)))
		{
			throw new IOException("Invalid runtime file list or hashes.");
		}
	}

	public static RuntimePackage Current()
	{
		if (!File.Exists(ActiveFile))
		{
			return Payload.Baseline();
		}
		Disk.Safe(ActiveFile);
		RuntimePackage runtimePackage = Disk.Read<RuntimePackage>(ActiveFile);
		if (runtimePackage == null || runtimePackage.Channel != "dlssg_for_sm86" || !Version.TryParse(runtimePackage.Version, out Version result) || result < new Version("0.3.4"))
		{
			return Payload.Baseline();
		}
		Validate(runtimePackage);
		return runtimePackage;
	}

	public static string DirectoryFor(RuntimePackage p)
	{
		Validate(p);
		return Path.Combine(DataRoot, "sdli1995");
	}

	public static string FileFor(RuntimePackage p, string name, string directory = null)
	{
		if (!Names.Contains(name))
		{
			throw new IOException("Unknown runtime file.");
		}
		return Path.Combine(directory ?? DirectoryFor(p), name);
	}

	private static bool Published(RuntimePackage p)
	{
		try
		{
			Disk.Safe(MetadataFile);
			RuntimePackage runtimePackage = Disk.Read<RuntimePackage>(MetadataFile);
			Validate(runtimePackage);
			return runtimePackage.Revision == p.Revision && runtimePackage.Files.All((PackageFile f) => p.Files.Any((PackageFile v) => v.Name == f.Name && v.Hash == f.Hash)) && p.Files.All((PackageFile f) => Disk.Hash(FileFor(p, f.Name)) == f.Hash);
		}
		catch
		{
			return false;
		}
	}

	public static RuntimePackage Prepare(RuntimePackage p, Action<string> progress)
	{
		if (!Published(p))
		{
			return FetchLatest(p, progress);
		}
		return p;
	}

	private static string NewStage()
	{
		string text = Path.Combine(DataRoot, ".stage-" + Guid.NewGuid().ToString("N"));
		Disk.Safe(text);
		Directory.CreateDirectory(text);
		return text;
	}

	private static void RemoveCache(string path)
	{
		path = Path.GetFullPath(path);
		string fullPath = Path.GetFullPath(DataRoot);
		if (!string.Equals(Path.GetDirectoryName(path), fullPath, StringComparison.OrdinalIgnoreCase))
		{
			throw new IOException("Invalid cache cleanup path.");
		}
		if (!Directory.Exists(path))
		{
			return;
		}
		Disk.Safe(path);
		if (Directory.GetDirectories(path).Length != 0)
		{
			return;
		}
		string[] files = Directory.GetFiles(path);
		if (!files.Any((string f) => !Names.Contains<string>(Path.GetFileName(f)) && Path.GetFileName(f) != "winhttp.dll" && Path.GetFileName(f) != "package.json"))
		{
			string[] array = files;
			for (int num = 0; num < array.Length; num++)
			{
				Disk.Safe(array[num]);
			}
			array = files;
			for (int num = 0; num < array.Length; num++)
			{
				File.Delete(array[num]);
			}
			Directory.Delete(path);
		}
	}

	internal static void Publish(RuntimePackage p, string staging)
	{
		Validate(p);
		Disk.Safe(staging);
		foreach (PackageFile file in p.Files)
		{
			ValidateInstallBytes(p, file.Name, File.ReadAllBytes(FileFor(p, file.Name, staging)));
		}
		string text = DirectoryFor(p);
		string text2 = Path.Combine(DataRoot, ".backup-" + Guid.NewGuid().ToString("N"));
		Disk.Safe(text);
		Disk.Safe(text2);
		Disk.Safe(MetadataFile);
		Disk.Safe(ActiveFile);
		byte[] array = (File.Exists(MetadataFile) ? File.ReadAllBytes(MetadataFile) : null);
		byte[] array2 = (File.Exists(ActiveFile) ? File.ReadAllBytes(ActiveFile) : null);
		bool flag = false;
		bool flag2 = false;
		try
		{
			if (Directory.Exists(text))
			{
				Directory.Move(text, text2);
				flag = true;
			}
			Directory.Move(staging, text);
			flag2 = true;
			Disk.Save(MetadataFile, p);
			Disk.Save(ActiveFile, p);
		}
		catch
		{
			if (flag2)
			{
				RemoveCache(text);
			}
			if (flag)
			{
				Directory.Move(text2, text);
			}
			if (array != null)
			{
				Disk.Write(MetadataFile, array);
			}
			else if (File.Exists(MetadataFile))
			{
				File.Delete(MetadataFile);
			}
			if (array2 != null)
			{
				Disk.Write(ActiveFile, array2);
			}
			else if (File.Exists(ActiveFile))
			{
				File.Delete(ActiveFile);
			}
			throw;
		}
		try
		{
			RemoveCache(text2);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	public static void ValidateInstallBytes(RuntimePackage p, string name, byte[] bytes)
	{
		if (Disk.HashBytes(bytes) != p.Files.Single((PackageFile f) => f.Name == name).Hash)
		{
			throw new IOException("Runtime hash mismatch: " + name);
		}
		if (name == "dlssg_sm86.ini")
		{
			ValidateIni(bytes);
		}
	}

	internal static void ValidateIni(byte[] bytes)
	{
		if (bytes.Length == 0 || bytes.Length > 1048576)
		{
			throw new IOException("Invalid INI size.");
		}
		string text;
		try
		{
			text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('\ufeff');
		}
		catch (DecoderFallbackException)
		{
			throw new IOException("Invalid INI encoding.");
		}
		bool flag = false;
		string[] array = text.Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.Length != 0 && !text2.StartsWith(";") && !text2.StartsWith("#"))
			{
				if (text2.IndexOf('\0') >= 0)
				{
					throw new IOException("Invalid INI content.");
				}
				if (text2.StartsWith("[") && text2.EndsWith("]") && text2.Length > 2)
				{
					flag = true;
				}
				else if (!flag || text2.IndexOf('=') <= 0)
				{
					throw new IOException("Invalid INI content.");
				}
			}
		}
		if (!flag)
		{
			throw new IOException("INI has no sections.");
		}
	}

	internal static byte[] VerifyDownload(RuntimePackage p, string name, byte[] bytes)
	{
		PackageFile packageFile = p.Files.Single((PackageFile x) => x.Name == name);
		if (GitBlob(bytes) != packageFile.Blob || (packageFile.Hash != null && Disk.HashBytes(bytes) != packageFile.Hash))
		{
			throw new IOException("Source archive integrity check failed: " + name);
		}
		if (name == "dlssg_sm86.ini")
		{
			ValidateIni(bytes);
		}
		return bytes;
	}

	private static HttpWebRequest Request(string url)
	{
		ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
		HttpWebRequest obj = (HttpWebRequest)WebRequest.Create(url);
		obj.UserAgent = "MFG-Enabler/1.2";
		obj.Timeout = 30000;
		obj.ReadWriteTimeout = 30000;
		return obj;
	}

	private static void DownloadTo(string url, Stream output, long max)
	{
		using HttpWebResponse httpWebResponse = (HttpWebResponse)Request(url).GetResponse();
		using Stream stream = httpWebResponse.GetResponseStream();
		if (httpWebResponse.ContentLength > max)
		{
			throw new IOException("Download size limit exceeded.");
		}
		byte[] array = new byte[81920];
		long num = 0L;
		int num2;
		while ((num2 = stream.Read(array, 0, array.Length)) > 0)
		{
			num += num2;
			if (num > max)
			{
				throw new IOException("Download size limit exceeded.");
			}
			output.Write(array, 0, num2);
		}
	}

	public static byte[] Download(string url, int maxBytes)
	{
		using MemoryStream memoryStream = new MemoryStream();
		DownloadTo(url, memoryStream, maxBytes);
		return memoryStream.ToArray();
	}

	internal static string GitBlob(byte[] bytes)
	{
		using SHA1 sHA = SHA1.Create();
		byte[] bytes2 = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
		sHA.TransformBlock(bytes2, 0, bytes2.Length, null, 0);
		sHA.TransformFinalBlock(bytes, 0, bytes.Length);
		return Convert.ToHexString(sHA.Hash).ToLowerInvariant();
	}

	private static Dictionary<string, object> Json(string url)
	{
		return (new JavaScriptSerializer
		{
			MaxJsonLength = 16777216
		}.DeserializeObject(Encoding.UTF8.GetString(Download(url, 16777216))) as Dictionary<string, object>) ?? throw new IOException("Invalid GitHub response.");
	}

	private static object Get(Dictionary<string, object> d, string key)
	{
		if (d == null || !d.TryGetValue(key, out var value))
		{
			return null;
		}
		return value;
	}

	public static void Ensure(RuntimePackage p, string name, Action<string> progress, string cacheDirectory = null)
	{
		Validate(p);
		string path = FileFor(p, name, cacheDirectory);
		Disk.Safe(path);
		if (!(Disk.Hash(path) == p.Files.Single((PackageFile f) => f.Name == name).Hash))
		{
			DownloadPackage(p, progress);
			if (cacheDirectory != null && !string.Equals(Path.GetFullPath(cacheDirectory), Path.GetFullPath(DirectoryFor(p)), StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("Custom payload cache is incomplete.");
			}
		}
	}

	internal static RuntimePackage ExtractSourceArchive(RuntimePackage p, string archivePath, string staging)
	{
		using (ZipArchive zipArchive = ZipFile.OpenRead(archivePath))
		{
			if (zipArchive.Entries.Count > 100000)
			{
				throw new IOException("Source ZIP contains too many files.");
			}
			Dictionary<string, PackageFile> dictionary = p.Files.ToDictionary<PackageFile, string>((PackageFile f) => f.Path, StringComparer.Ordinal);
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			string text = null;
			foreach (ZipArchiveEntry entry in zipArchive.Entries)
			{
				int num = entry.FullName.IndexOf('/');
				if (num < 1)
				{
					continue;
				}
				string text2 = entry.FullName.Substring(num + 1);
				if (!dictionary.TryGetValue(text2, out var value))
				{
					continue;
				}
				string text3 = entry.FullName.Substring(0, num);
				if (text == null)
				{
					text = text3;
				}
				if (text != text3 || text3 == ".." || text3 == "." || text3.Contains('\\') || !hashSet.Add(text2) || ((entry.ExternalAttributes >> 16) & 0xF000) == 40960 || entry.Length <= 0 || entry.Length > 134217728)
				{
					throw new IOException("Unsafe or duplicate runtime entry.");
				}
				byte[] bytes;
				using (Stream stream = entry.Open())
				{
					using MemoryStream memoryStream = new MemoryStream();
					byte[] array = new byte[81920];
					int num2;
					while ((num2 = stream.Read(array, 0, array.Length)) > 0)
					{
						if (memoryStream.Length + num2 > 134217728)
						{
							throw new IOException("Runtime file too large.");
						}
						memoryStream.Write(array, 0, num2);
					}
					bytes = memoryStream.ToArray();
				}
				VerifyDownload(p, value.Name, bytes);
				value.Hash = Disk.HashBytes(bytes);
				string text4 = FileFor(p, value.Name, staging);
				Disk.Write(text4, bytes);
				if (value.Name.EndsWith(".dll") && !Discovery.IsX64(text4))
				{
					throw new IOException("Runtime is not an x64 DLL: " + value.Name);
				}
			}
			if (hashSet.Count != dictionary.Count)
			{
				throw new IOException("Source ZIP is missing required runtime files.");
			}
		}
		Validate(p);
		return p;
	}

	private static RuntimePackage DownloadPackage(RuntimePackage p, Action<string> progress)
	{
		string text = NewStage();
		string text2 = Path.Combine(DataRoot, ".source-" + Guid.NewGuid().ToString("N") + ".zip");
		Disk.Safe(text2);
		try
		{
			progress("dlssg_for_sm86 " + p.Version + " · source ZIP 다운로드 중…");
			using (FileStream output = new FileStream(text2, FileMode.CreateNew))
			{
				DownloadTo("https://api.github.com/repos/sdli1995/dlssg_for_sm86/zipball/" + p.Revision, output, 1073741824L);
			}
			ExtractSourceArchive(p, text2, text);
			Publish(p, text);
			return p;
		}
		finally
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
			RemoveCache(text);
		}
	}

	internal static string ExtractEnglishNotes(string body)
	{
		if (string.IsNullOrWhiteSpace(body)) return "No release notes were provided.";
		List<string> list = new List<string>();
		foreach (string raw in body.Replace("\r", "").Split('\n'))
		{
			string text = raw.Trim();
			if (text.Length == 0)
			{
				if (list.Count != 0 && list[list.Count - 1].Length != 0) list.Add("");
				continue;
			}
			if (Regex.IsMatch(text, "[\\u3400-\\u9fff\\u3040-\\u30ff\\uac00-\\ud7af]")) continue;
			text = Regex.Replace(text, "!\\[[^\\]]*\\]\\([^)]+\\)", "");
			text = Regex.Replace(text, "\\[([^\\]]+)\\]\\([^)]+\\)", "$1");
			text = Regex.Replace(text, "^\\s*#{1,6}\\s*", "").Replace("**", "").Replace("`", "").Trim();
			if (text.Equals("English", StringComparison.OrdinalIgnoreCase) || text.Equals("EN", StringComparison.OrdinalIgnoreCase)) continue;
			if (text.Length != 0) list.Add(text);
		}
		string result = string.Join(Environment.NewLine, list).Trim();
		return result.Length == 0 ? "See the upstream release page for details." : result;
	}

	public static RuntimeReleaseNotes FetchReleaseNotes()
	{
		Dictionary<string, object> d = Json(Api + "releases/latest");
		string tag = Get(d, "tag_name") as string;
		object draft = Get(d, "draft");
		object prerelease = Get(d, "prerelease");
		if (!(draft is bool) || (bool)draft || !(prerelease is bool) || (bool)prerelease || !Regex.IsMatch(tag ?? "", "\\Av?\\d+\\.\\d+\\.\\d+\\z"))
		{
			throw new IOException("Unsupported upstream release version.");
		}
		string version = tag.TrimStart('v');
		string url = Get(d, "html_url") as string;
		if (string.IsNullOrWhiteSpace(url)) url = "https://github.com/sdli1995/dlssg_for_sm86/releases/tag/" + Uri.EscapeDataString(tag);
		return new RuntimeReleaseNotes(version, url, ExtractEnglishNotes(Get(d, "body") as string));
	}

	public static RuntimePackage FetchLatest(RuntimePackage current, Action<string> progress)
	{
		progress("dlssg_for_sm86 최신 릴리스 확인 중…");
		Dictionary<string, object> d = Json("https://api.github.com/repos/sdli1995/dlssg_for_sm86/releases/latest");
		string text = Get(d, "tag_name") as string;
		object obj = Get(d, "draft");
		if (obj is bool && !(bool)obj)
		{
			obj = Get(d, "prerelease");
			if (obj is bool && !(bool)obj && Regex.IsMatch(text ?? "", "\\Av?\\d+\\.\\d+\\.\\d+\\z"))
			{
				string version = text.TrimStart('v');
				if (new Version(version) < new Version(current.Version))
				{
					throw new IOException("Refusing an older upstream runtime.");
				}
				string text2 = Get(Json("https://api.github.com/repos/sdli1995/dlssg_for_sm86/commits/" + Uri.EscapeDataString(text)), "sha") as string;
				if (!HashLike(text2, 40))
				{
					throw new IOException("Invalid release commit.");
				}
				if (text2 == current.Revision && Published(current))
				{
					return current;
				}
				Dictionary<string, object> d2 = Json("https://api.github.com/repos/sdli1995/dlssg_for_sm86/git/trees/" + text2 + "?recursive=1");
				obj = Get(d2, "truncated");
				if (!(obj is bool) || (bool)obj || !(Get(d2, "tree") is object[] source))
				{
					throw new IOException("Incomplete release tree.");
				}
				List<Dictionary<string, object>> source2 = source.OfType<Dictionary<string, object>>().ToList();
				RuntimePackage runtimePackage = new RuntimePackage
				{
					Revision = text2,
					Version = version,
					Channel = "dlssg_for_sm86",
					Files = new List<PackageFile>()
				};
				foreach (string name in Names)
				{
					string path = RemotePath(name);
					string text3 = Get(source2.SingleOrDefault((Dictionary<string, object> e) => Get(e, "path") as string == path && Get(e, "type") as string == "blob" && Get(e, "mode") as string == "100644"), "sha") as string;
					if (!HashLike(text3, 40))
					{
						throw new IOException("Unsupported release layout: " + path);
					}
					runtimePackage.Files.Add(new PackageFile
					{
						Name = name,
						Path = path,
						Blob = text3
					});
				}
				return DownloadPackage(runtimePackage, progress);
			}
		}
		throw new IOException("Unsupported upstream release version.");
	}

	public static UpdateResult CheckAndApply(IEnumerable<Game> known, Action<string> progress)
	{
		UpdateResult updateResult = new UpdateResult();
		RuntimePackage selected = Current();
		try
		{
			selected = FetchLatest(selected, progress);
		}
		catch (Exception ex)
		{
			updateResult.Summary = "Runtime update check failed; installed games unchanged.";
			updateResult.Details.Add(updateResult.Summary + " " + ex.Message);
			return updateResult;
		}
		int num = 0;
		int num2 = 0;
		foreach (Game item in from g in known.Where((Game g) => g != null && !string.IsNullOrEmpty(g.Exe)).GroupBy<Game, string>((Game g) => Path.GetDirectoryName(g.Exe), StringComparer.OrdinalIgnoreCase)
			select g.First())
		{
			try
			{
				Installer installer = new Installer(Path.GetDirectoryName(item.Exe));
				Journal journal = installer.Load();
				if (journal != null && !(journal.Phase == "disabled"))
				{
					if (!File.Exists(item.Exe))
					{
						throw new IOException("Game executable is missing.");
					}
					if (journal.Phase != "enabled")
					{
						throw new IOException("Restore the interrupted operation first.");
					}
					if (!(journal.RuntimeRevision == selected.Revision) || !journal.Files.Where((Entry e) => e.Name.EndsWith(".dll")).All((Entry e) => selected.Files.Any((PackageFile f) => f.Name == e.Name && f.Hash == e.Installed)))
					{
						progress(item.Name + " · restore, then install " + selected.Version);
						installer.Upgrade(selected);
						num++;
					}
				}
			}
			catch (Exception ex2)
			{
				num2++;
				updateResult.Details.Add(item.Name + " · update deferred: " + ex2.Message);
			}
		}
		updateResult.Summary = "dlssg_for_sm86 " + selected.Version + " · " + num + " updated, " + num2 + " deferred";
		updateResult.Details.Insert(0, updateResult.Summary);
		return updateResult;
	}
}
