using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MfgEnabler;
[DataContract]
public sealed class GlobalPolicy
{
    [DataMember] public bool Enabled;
    [DataMember] public bool FutureGames;
    [DataMember] public bool Startup;
    [DataMember] public bool Tray;
    [DataMember] public bool AllowVulkan;
    [DataMember] public List<string> Approved = new();
    [DataMember] public List<string> Excluded = new();
    [DataMember] public List<string> ManagedFolders = new();
    public void Normalize()
    {
        Approved = Clean(Approved); Excluded = Clean(Excluded); ManagedFolders = Clean(ManagedFolders);
    }
    static List<string> Clean(List<string> values) => values == null ? new() : new(new HashSet<string>(values.FindAll(x => !String.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase));
    public static string Key(Game game) => !String.IsNullOrEmpty(game.NvidiaId) ? "nvidia:" + game.NvidiaId : "root:" + game.Root;
    public bool Allows(Game game)
    {
        Approved ??= new(); Excluded ??= new(); ManagedFolders ??= new();
        if (game == null) return false;
        string key = Key(game);
        return Enabled && game.CanEnable && !game.Manual && !String.IsNullOrEmpty(game.Exe)
            && (game.Api.HasFlag(GraphicsApi.Vulkan) ? AllowVulkan : game.Api.HasFlag(GraphicsApi.DirectX12))
            && !Excluded.Exists(x => String.Equals(x, key, StringComparison.OrdinalIgnoreCase))
            && (FutureGames || Approved.Exists(x => String.Equals(x, key, StringComparison.OrdinalIgnoreCase)));
    }
}
