using System;
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
