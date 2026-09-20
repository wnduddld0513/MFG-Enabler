using System;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;

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
            return result;
        }
        catch (Exception error) when (error is IOException || error is BadImageFormatException || error is UnauthorizedAccessException || error is ArgumentException)
        { return GraphicsApi.Unknown; }
    }
}
