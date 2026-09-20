using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MfgEnabler;

public class Installer
{
	public readonly string Target;

	public readonly string Store;

	public Action<int> Fault;

	public readonly bool IsSpoof;

	private string StateFile => Path.Combine(Store, "state.json");

	public Installer(string target, bool spoof = false)
	{
		Target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
		IsSpoof = spoof;
		Store = (spoof ? Path.Combine(Target, ".mfg-enabler", "nvapi") : Path.Combine(Target, ".mfg-enabler"));
	}

	private void MigrateStore()
	{
		try
		{
			string text = Path.Combine(Target, ".mfg-enabler", "spoof5080");
			if (!string.Equals(Store, text, StringComparison.OrdinalIgnoreCase) && Directory.Exists(text) && !Directory.Exists(Store))
			{
				Disk.Safe(text);
				Disk.Safe(Store);
				Directory.Move(text, Store);
			}
		}
		catch
		{
		}
	}

   public Journal Load() {
    if(IsSpoof)MigrateStore();
    Disk.Safe(Store); if(!File.Exists(StateFile)) return null; var j=Disk.Read<Journal>(StateFile);
   if(j==null || !String.Equals(j.Target,Target,StringComparison.OrdinalIgnoreCase) || j.Files==null) throw new IOException("복구 기록이 유효하지 않습니다.");
    var expected=IsSpoof?new[]{"nvapi64.dll"}:new[]{j.Proxy,"dlssg_sm86.ini"};
    bool proxyOk=IsSpoof?j.Proxy=="nvapi64.dll":(Payload.Proxies.Contains(j.Proxy)||j.Proxy=="winhttp.dll");
    if(!proxyOk || j.Files.Count!=expected.Length || j.Files.Any(e=>e==null) || j.Files.Select(e=>e.Name).Distinct().Count()!=expected.Length || j.Files.Any(e=>!expected.Contains(e.Name))) throw new IOException("복구 파일 목록이 유효하지 않습니다.");
   foreach(var e in j.Files) { if(e.Backup==null || e.Backup!=Path.GetFileName(e.Backup) || e.Backup.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || e.Backup=="." || e.Backup=="..") throw new IOException("잘못된 백업 경로"); Disk.Safe(Path.Combine(Store,e.Backup)); Disk.Safe(Path.Combine(Target,e.Name)); }
   foreach(var e in j.Files){if(e.Previous!=null&&(e.Previous.Length!=64||e.UpgradeBackup==null||e.UpgradeBackup!=Path.GetFileName(e.UpgradeBackup)||e.UpgradeBackup.IndexOfAny(Path.GetInvalidFileNameChars())>=0))throw new IOException("잘못된 업데이트 복구 기록");if(e.UpgradeBackup!=null)Disk.Safe(Path.Combine(Store,e.UpgradeBackup));}
   if(!new[]{"installing","enabled","restoring","disabled","updating"}.Contains(j.Phase)) throw new IOException("알 수 없는 복구 상태"); return j;
  }
	public string Status()
	{
		try
		{
			Journal journal = Load();
			if (journal == null || journal.Phase == "disabled")
			{
				return "미적용";
			}
			if (journal.Phase != "enabled")
			{
				return "복구 필요";
			}
			return journal.Files.All((Entry e) => Disk.Hash(Path.Combine(Target, e.Name)) == e.Installed) ? "적용됨" : "파일 변경 감지";
		}
		catch
		{
			return "기록 확인 필요";
		}
	}

	public static void CheckRunning(string target)
	{
		Process[] processes = Process.GetProcesses();
		foreach (Process process in processes)
		{
			using (process)
			{
				try
				{
					if (process.MainModule.FileName.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					{
						throw new IOException("게임 또는 해당 폴더의 프로그램을 종료하세요: " + process.ProcessName);
					}
				}
				catch (Win32Exception)
				{
				}
				catch (InvalidOperationException)
				{
				}
			}
		}
	}

	public void Enable(string proxy, string payload)
	{
		if (IsSpoof)
		{
			throw new IOException("RTX 5080 설치는 EnableSpoof를 사용하세요.");
		}
		if (!Payload.Proxies.Contains(proxy))
		{
			throw new IOException("지원하지 않는 프록시");
		}
		Disk.Safe(Target);
		CheckRunning(Target);
		Journal journal = Load();
		if (journal != null && journal.Phase != "disabled")
		{
			throw new IOException("먼저 기존 적용을 해제하거나 복구하세요.");
		}
		string[] proxies = Payload.Proxies;
		foreach (string text in proxies)
		{
			if (File.Exists(Path.Combine(Target, text)) && Disk.Hash(Path.Combine(Target, text)) == Payload.Specs()[text][2])
			{
				throw new IOException("수동 설치된 DLSSG 프록시가 있습니다: " + text + ". 먼저 수동 설치를 정리하세요.");
			}
		}
		RuntimePackage package = Updates.Current();
		Dictionary<string, byte[]> dictionary = new string[2] { proxy, "dlssg_sm86.ini" }.ToDictionary((string n) => n, (string n) => File.ReadAllBytes(Updates.FileFor(package, n, payload)));
		foreach (KeyValuePair<string, byte[]> item in dictionary)
		{
			Updates.ValidateInstallBytes(package, item.Key, item.Value);
		}
		InstallFiles(proxy, dictionary);
	}

	private void InstallFiles(string proxy, Dictionary<string, byte[]> files, RuntimePackage package = null)
	{
		Disk.Safe(Target);
		CheckRunning(Target);
		Journal journal = Load();
		if (journal != null && journal.Phase != "disabled")
		{
			throw new IOException("먼저 해당 기능을 해제하거나 복구하세요.");
		}
		if (journal != null) Restore();
		Disk.Safe(Store);
		// Check every destination before making backups or changing a game file.
		foreach (string name in files.Keys)
		{
			string destination = Path.Combine(Target, name);
			string backup = Path.Combine(Store, Path.ChangeExtension(name, ".backup"));
			Disk.Safe(destination);
			Disk.Safe(backup);
			if (Directory.Exists(destination) || File.Exists(backup) || Directory.Exists(backup))
				throw new IOException("설치 경로 또는 기존 백업을 먼저 확인하세요: " + name);
		}
		Directory.CreateDirectory(Store);
		Journal journal2 = new Journal
		{
			Target = Target,
			Phase = "installing",
			Proxy = proxy
		};
		foreach (KeyValuePair<string, byte[]> file in files)
		{
			string key = file.Key;
			string path = Path.Combine(Target, key);
			Disk.Safe(path);
			if (Directory.Exists(path))
			{
				throw new IOException("파일 경로에 폴더가 있습니다: " + key);
			}
			Entry entry = new Entry
			{
				Name = key,
				Original = Disk.Hash(path),
				Installed = Disk.HashBytes(file.Value),
				Backup = Path.ChangeExtension(key, ".backup")
			};
			if (entry.Original != null)
			{
				Disk.Write(Path.Combine(Store, entry.Backup), File.ReadAllBytes(path));
				if (Disk.Hash(Path.Combine(Store, entry.Backup)) != entry.Original)
				{
					throw new IOException("백업 검증 실패");
				}
			}
			journal2.Files.Add(entry);
		}
		Disk.Save(StateFile, journal2);
		try
		{
			int num = 0;
			foreach (Entry file2 in journal2.Files)
			{
				if (Disk.Hash(Path.Combine(Target, file2.Name)) != file2.Original)
				{
					throw new IOException("설치 중 원본 변경 감지");
				}
				Disk.Write(Path.Combine(Target, file2.Name), files[file2.Name]);
				if (Fault != null)
				{
					Fault(++num);
				}
			}
			if (journal2.Files.Any((Entry e) => Disk.Hash(Path.Combine(Target, e.Name)) != e.Installed))
			{
				throw new IOException("설치 후 검증 실패");
			}
			journal2.Phase = "enabled";
			if (!IsSpoof)
			{
				RuntimePackage runtimePackage = package ?? Updates.Current();
				journal2.RuntimeVersion = runtimePackage.Version;
				journal2.RuntimeRevision = runtimePackage.Revision;
			}
			Disk.Save(StateFile, journal2);
		}
		catch
		{
			try
			{
				Restore();
			}
			catch
			{
			}
			throw;
		}
	}

	private static bool IsIni(Entry e)
	{
		return e.Name == "dlssg_sm86.ini";
	}

	private bool CanRestore(Entry e, string hash, string phase)
	{
		if (!IsIni(e) && !(hash == e.Installed) && !(hash == e.Original) && hash != null)
		{
			if (phase == "updating" || phase == "restoring")
			{
				return hash == e.Previous;
			}
			return false;
		}
		return true;
	}

	private string OriginalAfterRestore(Entry e)
	{
		if (!IsIni(e))
		{
			return e.Original;
		}
		return null;
	}

	private void VerifyLogCleanup()
	{
		if (IsSpoof)
		{
			return;
		}
		string[] array = new string[2] { "dlssg_sm86.log", "dlssg_sm86_loader.log" };
		foreach (string text in array)
		{
			string path = Path.Combine(Target, text);
			Disk.Safe(path);
			if (Directory.Exists(path))
			{
				throw new IOException("A directory blocks log cleanup: " + text);
			}
		}
		string text2 = Path.Combine(Target, "dlssg_sm86", "logs");
		Disk.Safe(text2);
		if (File.Exists(text2))
		{
			throw new IOException("A file blocks log cleanup.");
		}
		if (Directory.Exists(text2))
		{
			VerifyLogTree(text2);
		}
	}

	private static void VerifyLogTree(string folder)
	{
		Disk.Safe(folder);
		string[] files = Directory.GetFiles(folder);
		for (int i = 0; i < files.Length; i++)
		{
			Disk.Safe(files[i]);
		}
		files = Directory.GetDirectories(folder);
		foreach (string obj in files)
		{
			Disk.Safe(obj);
			VerifyLogTree(obj);
		}
	}

	private void CleanupLogs()
	{
		if (IsSpoof)
		{
			return;
		}
		VerifyLogCleanup();
		string[] array = new string[2] { "dlssg_sm86.log", "dlssg_sm86_loader.log" };
		foreach (string path in array)
		{
			string path2 = Path.Combine(Target, path);
			if (File.Exists(path2))
			{
				File.Delete(path2);
			}
		}
		string text = Path.Combine(Target, "dlssg_sm86");
		string text2 = Path.Combine(text, "logs");
		if (Directory.Exists(text2))
		{
			DeleteLogTree(text2);
		}
		if (Directory.Exists(text) && !Directory.EnumerateFileSystemEntries(text).Any())
		{
			Directory.Delete(text);
		}
	}

	private static void DeleteLogTree(string folder)
	{
		Disk.Safe(folder);
		string[] files = Directory.GetFiles(folder);
		foreach (string path in files)
		{
			Disk.Safe(path);
			File.Delete(path);
		}
		files = Directory.GetDirectories(folder);
		for (int i = 0; i < files.Length; i++)
		{
			DeleteLogTree(files[i]);
		}
		Directory.Delete(folder);
	}

	public void VerifyRestore()
	{
		Disk.Safe(Target);
		CheckRunning(Target);
		Journal journal = Load();
		if (journal == null)
		{
			return;
		}
		VerifyLogCleanup();
		if (journal.Phase == "disabled")
		{
			foreach (Entry file in journal.Files.Where(e => !IsIni(e)))
				if (Disk.Hash(Path.Combine(Target, file.Name)) != file.Original)
					throw new IOException("원본 DLL을 확인한 뒤 백업을 정리하세요: " + file.Name);
			return;
		}
		foreach (Entry file in journal.Files)
		{
			string path = Path.Combine(Target, file.Name);
			if (Directory.Exists(path))
			{
				throw new IOException("A directory blocks restore: " + file.Name);
			}
			if (!CanRestore(file, Disk.Hash(path), journal.Phase))
			{
				throw new IOException("Installed DLL changed; preserve it elsewhere before restoring: " + file.Name);
			}
			if (!IsIni(file) && file.Original != null && Disk.Hash(Path.Combine(Store, file.Backup)) != file.Original)
			{
				throw new IOException("Original backup is damaged: " + file.Name);
			}
		}
	}

	public bool CanEditIni()
	{
		Journal journal = Load();
		if (!IsSpoof && journal?.Phase == "enabled" && Version.TryParse(journal.RuntimeVersion, out Version result) && result.Major == 0)
		{
			return result >= new Version("0.3.4");
		}
		return false;
	}

	public RuntimeIniSettings ReadIniSettings()
	{
		if (!CanEditIni())
		{
			throw new IOException("Install runtime 0.3.4 or newer before editing the INI.");
		}
		string path = Path.Combine(Target, "dlssg_sm86.ini");
		Disk.Safe(path);
		return RuntimeIni.Read(File.ReadAllBytes(path));
	}

	public void SaveIniSettings(RuntimeIniSettings settings)
	{
		settings.Validate();
		Disk.Safe(Target);
		CheckRunning(Target);
		if (!CanEditIni())
		{
			throw new IOException("Install runtime 0.3.4 or newer before editing the INI.");
		}
		Journal journal = Load();
		foreach (Entry item in journal.Files.Where((Entry e) => e.Name.EndsWith(".dll")))
		{
			if (Disk.Hash(Path.Combine(Target, item.Name)) != item.Installed)
			{
				throw new IOException("Installed DLL changed; restore it before editing settings.");
			}
		}
		Entry entry = journal.Files.Single((Entry e) => e.Name == "dlssg_sm86.ini");
		string path = Path.Combine(Target, entry.Name);
		Disk.Safe(path);
		byte[] bytes = File.ReadAllBytes(path);
		byte[] bytes2 = RuntimeIni.Apply(bytes, settings);
		string installed = entry.Installed;
		if (Disk.Hash(path) != Disk.HashBytes(bytes))
		{
			throw new IOException("INI changed while saving.");
		}
		try
		{
			Disk.Write(path, bytes2);
			if (Fault != null)
			{
				Fault(1);
			}
			entry.Installed = Disk.HashBytes(bytes2);
			Disk.Save(StateFile, journal);
		}
		catch
		{
			if (Disk.Hash(path) == Disk.HashBytes(bytes2))
			{
				Disk.Write(path, bytes);
			}
			entry.Installed = installed;
			throw;
		}
	}

	public void Upgrade(RuntimePackage package)
	{
		if (IsSpoof)
		{
			throw new IOException("Not a runtime installation.");
		}
		Updates.Validate(package);
		VerifyRestore();
		Journal journal = Load();
		if (journal == null || journal.Phase != "enabled")
		{
			throw new IOException("Only an enabled installation can be updated.");
		}
		string text = (Payload.Proxies.Contains(journal.Proxy) ? journal.Proxy : "version.dll");
		Dictionary<string, byte[]> dictionary = new Dictionary<string, byte[]>();
		string[] array = new string[2] { text, "dlssg_sm86.ini" };
		foreach (string text2 in array)
		{
			byte[] array2 = File.ReadAllBytes(Updates.FileFor(package, text2));
			Updates.ValidateInstallBytes(package, text2, array2);
			dictionary[text2] = array2;
		}
		Restore();
		InstallFiles(text, dictionary, package);
	}

	public static bool HasAny(string folder)
	{
		Installer installer = new Installer(folder);
		if (!(installer.Status() != "미적용") && !(new Installer(folder, spoof: true).Status() != "미적용"))
		{
			return installer.NeedsCleanup();
		}
		return true;
	}

	private bool NeedsCleanup()
	{
		return Load() != null || new Installer(Target, true).Load() != null;
	}

	private void CleanupStore(Journal journal)
	{
		// DLL verification must finish before any original backup can be deleted.
		foreach (Entry entry in journal.Files.Where(e => !IsIni(e)))
			if (Disk.Hash(Path.Combine(Target, entry.Name)) != entry.Original)
				throw new IOException("원본 DLL 복구 검증 실패: " + entry.Name);
		Disk.Safe(Store);
		var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "state.json" };
		foreach (Entry entry in journal.Files)
		{
			owned.Add(entry.Backup);
			if (entry.UpgradeBackup != null) owned.Add(entry.UpgradeBackup);
		}
		string[] files = Directory.GetFiles(Store);
		foreach (string file in files)
		{
			Disk.Safe(file);
			string name = Path.GetFileName(file);
			bool legacyBackup = (Path.GetExtension(name) == ".bak" || Path.GetExtension(name) == ".upg")
				&& Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _);
			if (!owned.Contains(name) && !legacyBackup)
				throw new IOException("복구는 완료했지만 알 수 없는 파일을 보존했습니다: " + file);
		}
		// Keep the disabled journal until backup cleanup succeeds, so a retry is possible.
		foreach (string file in files.Where(f => !string.Equals(f, StateFile, StringComparison.OrdinalIgnoreCase)))
			File.Delete(file);
		File.Delete(StateFile);
		if (!Directory.EnumerateFileSystemEntries(Store).Any()) Directory.Delete(Store);
		string root = Path.Combine(Target, ".mfg-enabler");
		Disk.Safe(root);
		if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
	}

	public static void RestoreAll(string folder)
	{
		Installer installer = new Installer(folder);
		Installer installer2 = new Installer(folder, spoof: true);
		installer.VerifyRestore();
		installer2.VerifyRestore();
		installer2.Restore();
		installer.Restore();
	}

	public void Restore()
	{
		VerifyRestore();
		Journal journal = Load();
		if (journal == null)
		{
			return;
		}
		if (journal.Phase == "disabled")
		{
			if (!IsSpoof)
			{
				string path = Path.Combine(Target, "dlssg_sm86.ini");
				Disk.Safe(path);
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				CleanupLogs();
			}
			CleanupStore(journal);
			return;
		}
		journal.Phase = "restoring";
		Disk.Save(StateFile, journal);
		int num = 0;
		foreach (Entry file in journal.Files.OrderBy(IsIni))
		{
			string path2 = Path.Combine(Target, file.Name);
			Disk.Safe(path2);
			if (!CanRestore(file, Disk.Hash(path2), journal.Phase))
			{
				throw new IOException("File changed during restore: " + file.Name);
			}
			if (OriginalAfterRestore(file) != null)
			{
				Disk.Write(path2, File.ReadAllBytes(Path.Combine(Store, file.Backup)));
			}
			else if (File.Exists(path2))
			{
				File.Delete(path2);
			}
			if (Fault != null)
			{
				Fault(++num);
			}
		}
		if (journal.Files.Any((Entry e) => Disk.Hash(Path.Combine(Target, e.Name)) != OriginalAfterRestore(e)))
		{
			throw new IOException("Restore verification failed.");
		}
		CleanupLogs();
		journal.Phase = "disabled";
		Disk.Save(StateFile, journal);
		CleanupStore(journal);
	}
}
