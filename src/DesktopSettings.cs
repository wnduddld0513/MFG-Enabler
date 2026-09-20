using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
namespace MfgEnabler;
[DataContract]
public sealed class DesktopSettings
{
    [DataMember] public bool AutoUpdate = true;
    [DataMember] public GlobalPolicy Global = new();
    [DataMember] public bool Migrated11;
    [DataMember] public string Language = "en";
    [DataMember] public string AppChannel = ReleaseNumber.Parse(AppUpdates.CurrentVersion)?.Beta != null ? "beta" : "main";
    [DataMember] public string DismissedStableVersion;
    [DataMember] public string DismissedBetaVersion;
    [DataMember] public string SeenWhatsNewVersion;
    [DataMember] public NvidiaOverridePolicy NvidiaGlobal = new();
    [DataMember] public Dictionary<string, NvidiaOverridePolicy> NvidiaPrograms = new(StringComparer.OrdinalIgnoreCase);
}
