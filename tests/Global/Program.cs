using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MfgEnabler;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
void Reject(Action action, string name) { try { action(); } catch (IOException) { Check(true, name); return; } throw new Exception(name); }
string root = Path.Combine(Path.GetTempPath(), "MFG-global-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
byte[] Executable(params string[] imports)
{
    byte[] bytes = new byte[2048];
    using var writer = new BinaryWriter(new MemoryStream(bytes));
    writer.Write((ushort)0x5a4d); writer.BaseStream.Position = 0x3c; writer.Write(128);
    writer.BaseStream.Position = 128; writer.Write(0x4550); writer.Write((ushort)0x8664); writer.Write((ushort)1);
    writer.BaseStream.Position = 148; writer.Write((ushort)240); writer.Write((ushort)0x22);
    writer.Write((ushort)0x20b);
    writer.BaseStream.Position = 176; writer.Write(0x140000000L); writer.Write(4096); writer.Write(512);
    writer.BaseStream.Position = 208; writer.Write(8192); writer.Write(512);
    writer.BaseStream.Position = 260; writer.Write(16);
    writer.BaseStream.Position = 272; writer.Write(4096); writer.Write((imports.Length + 1) * 20);
    writer.BaseStream.Position = 392; writer.Write(Encoding.ASCII.GetBytes(".rdata\0\0")); writer.Write(1536); writer.Write(4096); writer.Write(1536); writer.Write(512);
    writer.BaseStream.Position = 428; writer.Write(0x40000040u); // initialized, readable data
    for (int i = 0; i < imports.Length; i++) {
        writer.BaseStream.Position = 512 + i * 20 + 12; writer.Write(4096 + 256 + i * 128);
        writer.BaseStream.Position = 768 + i * 128; writer.Write(Encoding.ASCII.GetBytes(imports[i] + "\0"));
    }
    return bytes;
}
byte[] WithMarkers(byte[] bytes, params string[] markers)
{
    using var writer = new BinaryWriter(new MemoryStream(bytes));
    writer.BaseStream.Position = 1280;
    foreach (string marker in markers) { writer.Write(Encoding.ASCII.GetBytes(marker)); writer.Write((byte)0); }
    return bytes;
}
Game Game(string name, params string[] imports)
{
    string dir = Path.Combine(root, name); Directory.CreateDirectory(dir);
    string exe = Path.Combine(dir, "game.exe"); File.WriteAllBytes(exe, Executable(imports));
    File.WriteAllBytes(Path.Combine(dir, "nvngx_dlssg.dll"), Executable());
    return new Game { Name = name, Root = dir, Exe = exe, Executables = new() { exe }, CanEnable = true, NvidiaId = name, Api = GraphicsApiDetector.Detect(exe) };
}
Game DynamicGame(string name, params string[] markers)
{
    string dir = Path.Combine(root, name); Directory.CreateDirectory(dir);
    string exe = Path.Combine(dir, "game.exe"); File.WriteAllBytes(exe, WithMarkers(Executable(), markers));
    File.WriteAllBytes(Path.Combine(dir, "nvngx_dlssg.dll"), Executable());
    return new Game { Name = name, Root = dir, Exe = exe, Executables = new() { exe }, CanEnable = true, NvidiaId = name, Api = GraphicsApiDetector.Detect(exe) };
}
var dx = Game("dx12", "D3D12.dll"); var vk = Game("vulkan", "vulkan-1.dll");
var mixed = Game("mixed", "d3d12.dll", "vulkan-1.dll"); var unknown = Game("unknown", "dxgi.dll");
var dynamicDx = DynamicGame("dynamic-dx12", "D3D12CreateDevice", "D3D12SerializeRootSignature");
var weakDynamicDx = DynamicGame("weak-dynamic-dx12", "D3D12CreateDevice");
Check(dx.Api == GraphicsApi.DirectX12, "Detect DX12 from PE imports");
Check(dynamicDx.Api == GraphicsApi.DirectX12, "Detect dynamically loaded DX12 from runtime exports");
Check(weakDynamicDx.Api == GraphicsApi.Unknown, "Require strong evidence for dynamically loaded DX12");
Check(vk.Api == GraphicsApi.Vulkan && mixed.Api == (GraphicsApi.Vulkan | GraphicsApi.DirectX12), "Detect Vulkan and mixed-API executables");
Check(unknown.Api == GraphicsApi.Unknown, "DXGI alone does not prove DX12");
File.WriteAllBytes(Path.Combine(unknown.Root, "d3d12.dll"), Executable("d3d12.dll"));
Check(GraphicsApiDetector.Detect(unknown.Exe) == GraphicsApi.Unknown, "Injected proxy cannot make a game appear DX12 compatible");
var policy = new GlobalPolicy { Enabled = true, FutureGames = true };
Check(policy.Allows(dx) && policy.Allows(dynamicDx) && !policy.Allows(vk) && !policy.Allows(mixed) && !policy.Allows(unknown), "Default automatic policy is DX12 only");
policy.AllowVulkan = true; Check(policy.Allows(vk) && policy.Allows(mixed) && !policy.Allows(unknown), "Vulkan requires explicit opt-in; unknown remains excluded");
policy.AllowVulkan = false; policy.Excluded.Add(GlobalPolicy.Key(dx)); Check(!policy.Allows(dx), "Exclusions override DX12 approval"); policy.Excluded.Clear();
policy.FutureGames = false; Check(!policy.Allows(dx), "Future-game consent is independent of renderer"); policy.Approved.Add(GlobalPolicy.Key(dx)); Check(policy.Allows(dx), "Known DX12 approval works without future-game consent");
Check(!policy.Allows(null), "Ignore missing game entries");
var legacy = Path.Combine(root, "settings.json"); File.WriteAllText(legacy, "{\"Global\":{\"Enabled\":true,\"FutureGames\":true},\"Language\":\"ko\"}");
var migrated = Disk.Read<DesktopSettings>(legacy); migrated.Global.Normalize();
Check(!migrated.Global.AllowVulkan && migrated.Global.Allows(dx) && !migrated.Global.Allows(vk), "Old settings migrate to DX12-only default");

string storage = Path.Combine(root, "ApplicationStorage.json");
File.WriteAllText(storage, JsonSerializer.Serialize(new { Applications = new[] { dx, dynamicDx, vk, mixed, unknown }.Select(g => new {
    LocalId = g.Name, Application = new { DisplayName = g.Name, InstallDirectory = g.Root, DriverProfile = "game.exe", DetectedFiles = new[] { "game.exe" }, IsCreativeApplication = false }
}) }));
var scan = Discovery.Scan(storage, Path.Combine(root, "no-fingerprint.db"));
Check(scan.Games.Count == 5 && scan.Games.Single(g => g.Name == "dynamic-dx12").Api == GraphicsApi.DirectX12, "Discovery supplies fresh API evidence");
policy = new GlobalPolicy { Enabled = true, FutureGames = true };
int saves = 0, prepares = 0, installs = 0;
var messages = GlobalApply.Apply(policy, scan.Games, () => saves++, () => prepares++, g => { }, g => installs++);
Check(installs == 2 && prepares == 1 && saves == 2 && policy.ManagedFolders.Contains(dx.Root) && policy.ManagedFolders.Contains(dynamicDx.Root), "Only DX12 installs, with ownership persisted first");
policy.ManagedFolders.Clear(); installs = 0;
messages = GlobalApply.Apply(policy, scan.Games, () => throw new IOException("cannot save ownership"), () => { }, g => { }, g => installs++);
Check(installs == 0 && messages.Any(x => x.Contains("cannot save ownership")), "Never install when ownership persistence fails");
var dx2 = Game("second-dx12", "d3d12.dll"); policy.ManagedFolders.Clear(); prepares = installs = 0;
GlobalApply.Apply(policy, new[] { dx, dx2, dx }, () => { }, () => prepares++, g => { }, g => installs++);
Check(prepares == 1 && installs == 2, "One payload preparation per batch; shared folders deduplicated");
prepares = installs = 0;
GlobalApply.Apply(policy, new[] { dx, dx2 }, () => { }, () => { prepares++; throw new IOException("offline"); }, g => { }, g => installs++);
Check(prepares == 1 && installs == 0, "A failed download is not repeated for every game in a batch");

foreach (uint tier in new uint[] { 1, 2, NvidiaOverrides.RecommendedFgPreset, NvidiaOverrides.RecommendedPreset }) new NvidiaOverridePolicy { FgPreset = tier }.Validate();
Check(true, "All exposed FG presets validate");
Reject(() => new NvidiaOverridePolicy { FgPreset = 3 }.Validate(), "Reject unused FG preset");
foreach (uint tier in new uint[] { 1, 6, 10, 13, NvidiaOverrides.RecommendedPreset }) new NvidiaOverridePolicy { SrPreset = tier }.Validate();
Check(true, "All exposed SR preset groups validate");
Reject(() => new NvidiaOverridePolicy { SrPreset = 7 }.Validate(), "Reject unused SR preset");
foreach (uint tier in new uint[] { 1, 6, NvidiaOverrides.RecommendedPreset }) new NvidiaOverridePolicy { RrPreset = tier }.Validate();
Check(true, "All exposed RR presets validate");
Reject(() => new NvidiaOverridePolicy { RrPreset = 10 }.Validate(), "Reject SR-only preset for RR");
Reject(() => new NvidiaOverridePolicy { DynamicTargetFps = 501 }.Validate(), "Reject invalid NVIDIA target FPS");
Reject(() => new NvidiaOverridePolicy { SmoothMotion = 2 }.Validate(), "Reject invalid Smooth Motion value");
var values = NvidiaOverrides.SettingsFor(new NvidiaOverridePolicy { FgMode = 4, FixedFrameCount = 5, DynamicFrameCount = 5, DynamicTargetFps = 144, FgPreset = 2 });
Check(values[NvidiaOverrides.FixedFrameCountId] == 0 && values[NvidiaOverrides.DynamicFrameCountId] == 5 && values[NvidiaOverrides.DynamicTargetFpsId] == 144, "Dynamic mode clears fixed multiplier and writes dynamic values");
Check(values[NvidiaOverrides.FgOverrideId] == 1 && values[NvidiaOverrides.SrOverrideId] == null, "Enable only the selected preset override");
values = NvidiaOverrides.SettingsFor(new NvidiaOverridePolicy { FgMode = 2, DynamicFrameCount = 5, DynamicTargetFps = 144 });
Check(values[NvidiaOverrides.DynamicFrameCountId] == null && values[NvidiaOverrides.DynamicTargetFpsId] == null, "Fixed mode clears stale dynamic overrides");
Check(NvidiaOverrides.SettingsFor(new NvidiaOverridePolicy()).Values.All(x => x == null), "Default removes overrides so programs inherit global settings");

if (args.Contains("--live-driver"))
{
    string exe = Path.Combine(root, "mfg-validation-" + Guid.NewGuid().ToString("N") + ".exe");
    File.WriteAllBytes(exe, Executable());
    try
    {
        NvidiaOverrides.ApplyProgram(exe, new NvidiaOverridePolicy { FixedFrameCount = 3, FgMode = 2 });
        using (var session = NvAPIWrapper.DRS.DriverSettingsSession.CreateAndLoad()) {
            var profile = session.FindApplicationProfile(exe);
            Check(Convert.ToUInt32(profile.GetSetting(NvidiaOverrides.FixedFrameCountId).CurrentValue) == 3, "Driver reads back isolated profile multiplier");
        }
        NvidiaOverrides.RestoreProgram(exe);
        using var restoredSession = NvAPIWrapper.DRS.DriverSettingsSession.CreateAndLoad();
        var restoredProfile = restoredSession.FindApplicationProfile(exe);
        Check(restoredProfile != null, "Program override restore preserves profile identity");
    }
    finally
    {
        using var session = NvAPIWrapper.DRS.DriverSettingsSession.CreateAndLoad();
        try { var profile = session.FindApplicationProfile(exe); profile.Delete(); session.Save(); }
        catch (NvAPIWrapper.Native.Exceptions.NVIDIAApiException e) when (e.Status == NvAPIWrapper.Native.General.Status.ExecutableNotFound) { }
    }
}
if (args.Contains("--probe-installed"))
{
    var live = Discovery.Scan();
    foreach (var game in live.Games.Where(g => g.CanEnable))
        Console.WriteLine($"PROBE {game.Name}: {game.Api} :: {game.Exe}");
}
Console.WriteLine($"{checks} checks passed. Fixtures: {root}");
