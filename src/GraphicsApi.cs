using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;

namespace MfgEnabler;

[Flags]
public enum GraphicsApi { Unknown = 0, DirectX12 = 1, Vulkan = 2 }

public static class GraphicsApiDetector
{
    // Only inspect rendering binaries, never injected proxy DLLs or a whole install tree.
    public static GraphicsApi Detect(string executable)
    {
        var result = Imports(executable);
        string engine = Path.Combine(Path.GetDirectoryName(executable), "UnityPlayer.dll");
        if (File.Exists(engine)) result |= Imports(engine);
        return result;
    }

    internal static GraphicsApi Imports(string path)
    {
        try
        {
            Disk.Safe(path);
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var header = pe.PEHeaders.PEHeader;
            if (header == null) return GraphicsApi.Unknown;
            using var reader = new BinaryReader(stream);
            GraphicsApi result = GraphicsApi.Unknown;
            long Offset(int rva)
            {
                var section = pe.PEHeaders.SectionHeaders.FirstOrDefault(s => rva >= s.VirtualAddress && (long)rva < (long)s.VirtualAddress + s.SizeOfRawData);
                if (section.SizeOfRawData == 0) throw new IOException("Invalid PE import RVA.");
                long value = (long)section.PointerToRawData + rva - section.VirtualAddress;
                if (value < 0 || value >= stream.Length) throw new IOException("Invalid PE import offset.");
                return value;
            }
            void ReadTable(DirectoryEntry table, bool delay)
            {
                if (table.RelativeVirtualAddress == 0) return;
                int stride = delay ? 32 : 20;
                long start = Offset(table.RelativeVirtualAddress);
                int count = Math.Min(table.Size / stride, 4096);
                for (int index = 0; index < count; index++)
                {
                    stream.Position = start + index * stride;
                    uint first = reader.ReadUInt32();
                    if (delay && (first & 1) == 0) break; // Modern PE32+ delay imports use RVAs.
                    stream.Position = start + index * stride + (delay ? 4 : 12);
                    int nameRva = reader.ReadInt32();
                    if (nameRva == 0) break;
                    stream.Position = Offset(nameRva);
                    var name = new System.Text.StringBuilder();
                    for (int n = 0; n < 256; n++) { byte c = reader.ReadByte(); if (c == 0) break; name.Append((char)c); }
                    string dll = name.ToString();
                    if (dll.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase) || dll.Equals("d3d12core.dll", StringComparison.OrdinalIgnoreCase)) result |= GraphicsApi.DirectX12;
                    if (dll.Equals("vulkan-1.dll", StringComparison.OrdinalIgnoreCase)) result |= GraphicsApi.Vulkan;
                }
            }
            ReadTable(header.ImportTableDirectory, false);
            ReadTable(header.DelayImportTableDirectory, true);
            if (!result.HasFlag(GraphicsApi.DirectX12) && HasDynamicDx12Evidence(stream, pe)) result |= GraphicsApi.DirectX12;
            return result;
        }
        catch (Exception error) when (error is IOException || error is BadImageFormatException || error is UnauthorizedAccessException || error is ArgumentException)
        { return GraphicsApi.Unknown; }
    }

    // Some engines resolve D3D12 exports with LoadLibrary/GetProcAddress instead of
    // importing d3d12.dll. Require CreateDevice plus another D3D12 runtime export so
    // incidental text such as a command-line option cannot classify a game as DX12.
    static bool HasDynamicDx12Evidence(Stream stream, PEReader pe)
    {
        byte[][] patterns =
        {
            Encoding.ASCII.GetBytes("D3D12CreateDevice"),
            Encoding.ASCII.GetBytes("D3D12GetDebugInterface"),
            Encoding.ASCII.GetBytes("D3D12SerializeRootSignature"),
            Encoding.ASCII.GetBytes("D3D12SerializeVersionedRootSignature")
        };
        bool[] found = new bool[patterns.Length];
        byte[] buffer = new byte[64 * 1024];
        int overlap = patterns.Max(x => x.Length) - 1;

        foreach (var section in pe.PEHeaders.SectionHeaders)
        {
            if (section.SizeOfRawData <= 0 || (section.SectionCharacteristics & SectionCharacteristics.ContainsInitializedData) == 0) continue;
            long start = section.PointerToRawData;
            long end = Math.Min(stream.Length, start + section.SizeOfRawData);
            if (start < 0 || start >= end) continue;

            stream.Position = start;
            int kept = 0;
            while (stream.Position < end)
            {
                int read = stream.Read(buffer, kept, (int)Math.Min(buffer.Length - kept, end - stream.Position));
                if (read <= 0) break;
                int length = kept + read;
                for (int p = 0; p < patterns.Length; p++)
                    if (!found[p] && Contains(buffer, length, patterns[p])) found[p] = true;
                if (found[0] && (found[1] || found[2] || found[3])) return true;

                kept = Math.Min(overlap, length);
                Buffer.BlockCopy(buffer, length - kept, buffer, 0, kept);
            }
        }
        return false;
    }

    static bool Contains(byte[] buffer, int length, byte[] pattern)
    {
        for (int i = 0; i <= length - pattern.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && buffer[i + j] == pattern[j]) j++;
            if (j == pattern.Length) return true;
        }
        return false;
    }
}

public sealed class ProxyEvidence
{
    public string Proxy { get; init; }
    public string Module { get; init; }
    public string Route { get; init; }
    public int Depth { get; init; }
    public int ImportOrder { get; init; }
    public int ImportFunctionCount { get; init; }
    public int Score { get; init; }
}

public sealed class ProxyCandidate
{
    public string Name { get; init; }
    public int Score { get; init; }
    public bool ExistingFileConflict { get; init; }
    public bool Usable => !ExistingFileConflict;
    public IReadOnlyList<ProxyEvidence> Evidence { get; init; }
}

public sealed class ProxyDetectionResult
{
    public string Selected { get; init; }
    public IReadOnlyList<string> Ambiguous { get; init; }
    public IReadOnlyList<ProxyCandidate> Candidates { get; init; }
}

public static class ProxyDetector
{
    static readonly string[] Primary = { "version.dll", "winmm.dll", "dbghelp.dll", "dinput8.dll" };
    static readonly string[] Fallback = { "dxgi.dll", "d3d12.dll" };
    static readonly HashSet<string> All = new(Primary.Concat(Fallback), StringComparer.OrdinalIgnoreCase);

    enum RouteStrength { Delay = 1, Dynamic = 2, Startup = 3 }
    sealed record ImportRef(string Name, bool Delay, int Order, int FunctionCount);

    static int EvidenceScore(RouteStrength route, int depth, int order, int functionCount)
    {
        int routePart = (int)route * 500_000_000;
        int depthPart = (100 - Math.Min(Math.Max(depth, 0), 100)) * 4_500_000;
        int orderPart = (4096 - Math.Min(Math.Max(order, 0), 4096)) * 1000;
        int countPart = Math.Min(Math.Max(functionCount, 0), 999);
        return routePart + depthPart + orderPart + countPart;
    }

    public static ProxyDetectionResult Detect(string executable, string ownedProxy = null)
    {
        Disk.Safe(executable);
        if (!GraphicsApiDetector.Detect(executable).HasFlag(GraphicsApi.DirectX12))
            return new ProxyDetectionResult
            {
                Selected = null,
                Ambiguous = Array.Empty<string>(),
                Candidates = Array.Empty<ProxyCandidate>()
            };

        string root = Path.GetDirectoryName(Path.GetFullPath(executable));
        var local = Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(path => !All.Contains(Path.GetFileName(path)))
            .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var evidence = All.ToDictionary(name => name, _ => new List<ProxyEvidence>(), StringComparer.OrdinalIgnoreCase);
        var visited = new Dictionary<string, RouteStrength>(StringComparer.OrdinalIgnoreCase);

        void Visit(string module, RouteStrength route, int depth)
        {
            string full = Path.GetFullPath(module);
            if (visited.TryGetValue(full, out var previous) && previous >= route) return;
            visited[full] = route;

            var imports = ReadImports(full);
            foreach (var import in imports)
            {
                RouteStrength effective = import.Delay
                    ? RouteStrength.Delay
                    : route == RouteStrength.Delay && depth > 0
                        ? RouteStrength.Dynamic
                        : route;
                int score = EvidenceScore(effective, depth, import.Order, import.FunctionCount);
                if (All.Contains(import.Name))
                {
                    evidence[import.Name].Add(new ProxyEvidence
                    {
                        Proxy = import.Name,
                        Module = full,
                        Route = effective.ToString(),
                        Depth = depth,
                        ImportOrder = import.Order,
                        ImportFunctionCount = import.FunctionCount,
                        Score = score
                    });
                }

                if (local.TryGetValue(import.Name, out var child)) Visit(child, effective, depth + 1);
            }
        }

        Visit(executable, RouteStrength.Startup, 0);

        var rootImports = ReadImports(executable).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var strings = DllStrings(executable);
        foreach (var pair in local.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            if (strings.Contains(pair.Key) && !rootImports.Contains(pair.Key)) Visit(pair.Value, RouteStrength.Dynamic, 1);

        var candidates = All.Select(name =>
        {
            var hits = evidence[name].OrderByDescending(x => x.Score).ToArray();
            bool owned = string.Equals(name, ownedProxy, StringComparison.OrdinalIgnoreCase);
            bool conflict = File.Exists(Path.Combine(root, name)) && !owned;
            return new ProxyCandidate
            {
                Name = name,
                Score = hits.Length == 0 ? 0 : hits[0].Score,
                ExistingFileConflict = conflict,
                Evidence = hits
            };
        }).OrderByDescending(x => x.Score).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        (string selected, string[] ambiguous) Choose(IEnumerable<ProxyCandidate> source)
        {
            var usable = source.Where(x => x.Usable && x.Score > 0).OrderByDescending(x => x.Score).ToArray();
            if (usable.Length == 0) return (null, Array.Empty<string>());
            int best = usable[0].Score;
            string[] tied = usable.Where(x => x.Score == best).Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            return tied.Length == 1 ? (tied[0], Array.Empty<string>()) : (null, tied);
        }

        var choice = Choose(candidates.Where(x => Primary.Contains(x.Name, StringComparer.OrdinalIgnoreCase)));
        if (choice.selected == null && choice.ambiguous.Length == 0)
            choice = Choose(candidates.Where(x => Fallback.Contains(x.Name, StringComparer.OrdinalIgnoreCase)));

        return new ProxyDetectionResult { Selected = choice.selected, Ambiguous = choice.ambiguous, Candidates = candidates };
    }

    static List<ImportRef> ReadImports(string path)
    {
        var result = new List<ImportRef>();
        try
        {
            Disk.Safe(path);
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var header = pe.PEHeaders.PEHeader;
            if (header == null) return result;
            using var reader = new BinaryReader(stream, Encoding.ASCII, true);

            long Offset(int rva)
            {
                var section = pe.PEHeaders.SectionHeaders.FirstOrDefault(s =>
                    rva >= s.VirtualAddress && (long)rva < (long)s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData));
                if (section.SizeOfRawData == 0) throw new IOException("Invalid PE import RVA.");
                long value = (long)section.PointerToRawData + rva - section.VirtualAddress;
                if (value < 0 || value >= stream.Length) throw new IOException("Invalid PE import offset.");
                return value;
            }

            int CountThunks(int rva)
            {
                if (rva == 0) return 0;
                long start = Offset(rva);
                int width = header.Magic == PEMagic.PE32Plus ? 8 : 4;
                int count = 0;
                for (; count < 65535; count++)
                {
                    long entry = start + (long)count * width;
                    if (entry < 0 || entry + width > stream.Length) break;
                    stream.Position = entry;
                    ulong value = width == 8 ? reader.ReadUInt64() : reader.ReadUInt32();
                    if (value == 0) break;
                }
                return count;
            }

            void Table(DirectoryEntry table, bool delay)
            {
                if (table.RelativeVirtualAddress == 0) return;
                int stride = delay ? 32 : 20;
                long start = Offset(table.RelativeVirtualAddress);
                int count = table.Size > 0 ? Math.Min(table.Size / stride, 4096) : 4096;
                for (int index = 0; index < count; index++)
                {
                    long entry = start + (long)index * stride;
                    if (entry < 0 || entry + stride > stream.Length) break;
                    stream.Position = entry;
                    uint first = reader.ReadUInt32();
                    if (delay && (first & 1) == 0) break;
                    stream.Position = entry + (delay ? 4 : 12);
                    int nameRva = reader.ReadInt32();
                    if (nameRva == 0) break;
                    int thunkRva;
                    if (delay)
                    {
                        stream.Position = entry + 16;
                        thunkRva = reader.ReadInt32();
                        if (thunkRva == 0)
                        {
                            stream.Position = entry + 12;
                            thunkRva = reader.ReadInt32();
                        }
                    }
                    else
                    {
                        thunkRva = unchecked((int)first);
                        if (thunkRva == 0)
                        {
                            stream.Position = entry + 16;
                            thunkRva = reader.ReadInt32();
                        }
                    }
                    int functionCount = CountThunks(thunkRva);
                    stream.Position = Offset(nameRva);
                    var name = new StringBuilder();
                    for (int n = 0; n < 260; n++)
                    {
                        byte c = reader.ReadByte();
                        if (c == 0) break;
                        name.Append((char)c);
                    }
                    string dll = name.ToString();
                    if (dll.Length > 0) result.Add(new ImportRef(dll, delay, index, functionCount));
                }
            }

            Table(header.ImportTableDirectory, false);
            Table(header.DelayImportTableDirectory, true);
        }
        catch (Exception error) when (error is IOException || error is BadImageFormatException || error is UnauthorizedAccessException || error is ArgumentException)
        {
        }
        return result;
    }

    static HashSet<string> DllStrings(string path)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Disk.Safe(path);
            using var stream = File.OpenRead(path);
            var text = new StringBuilder(260);
            void Flush()
            {
                if (text.Length >= 5 && text.Length <= 260)
                {
                    string value = text.ToString();
                    if (value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && value.IndexOfAny(new[] { '\\', '/' }) < 0)
                        result.Add(value);
                }
                text.Clear();
            }
            int value;
            while ((value = stream.ReadByte()) >= 0)
            {
                if (value >= 32 && value <= 126)
                {
                    if (text.Length < 260) text.Append((char)value);
                    else Flush();
                }
                else Flush();
            }
            Flush();
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
        {
        }
        return result;
    }
}
