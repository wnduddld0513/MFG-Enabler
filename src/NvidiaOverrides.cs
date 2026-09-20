using System;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using NvAPIWrapper.DRS;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.General;

namespace MfgEnabler;

public enum NvidiaDlssPresetKind
{
    FrameGeneration,
    SuperResolution,
    RayReconstruction
}

public readonly record struct NvidiaDlssPresetOption(uint Value, string Label);

[DataContract]
public sealed class NvidiaOverridePolicy
{
    [DataMember] public uint? FgPreset;
    [DataMember] public uint? SrPreset;
    [DataMember] public uint? RrPreset;
    [DataMember] public uint? FgMode;
    [DataMember] public uint? FixedFrameCount;
    [DataMember] public uint? DynamicFrameCount;
    [DataMember] public uint? DynamicTargetFps;
    [DataMember] public uint? SmoothMotion;

    public bool IsEmpty => FgPreset == null && SrPreset == null && RrPreset == null && FgMode == null &&
        FixedFrameCount == null && DynamicFrameCount == null && DynamicTargetFps == null && SmoothMotion == null;

    public void Validate()
    {
        if (!NvidiaOverrides.IsPresetValue(NvidiaDlssPresetKind.FrameGeneration, FgPreset)
            || !NvidiaOverrides.IsPresetValue(NvidiaDlssPresetKind.SuperResolution, SrPreset)
            || !NvidiaOverrides.IsPresetValue(NvidiaDlssPresetKind.RayReconstruction, RrPreset)
            || FgMode.HasValue && FgMode is not (0 or 2 or 4)
            || FixedFrameCount > 5 || DynamicFrameCount > 5 || SmoothMotion > 1
            || DynamicTargetFps.HasValue && DynamicTargetFps != 0x01000000 && DynamicTargetFps is not (>= 60 and <= 500))
            throw new IOException("Invalid NVIDIA override value.");
    }
}

public static class NvidiaOverrides
{
    public const uint RecommendedFgPreset = 0x00FFFFFE;
    public const uint RecommendedPreset = 0x00FFFFFF;

    static readonly NvidiaDlssPresetOption[] FgPresets =
    {
        new(RecommendedFgPreset, "Recommended"),
        new(RecommendedPreset, "Latest"),
        new(1, "A"),
        new(2, "B")
    };

    static readonly NvidiaDlssPresetOption[] SrPresets =
    {
        new(RecommendedPreset, "Recommended"),
        new(1, "A"),
        new(2, "B"),
        new(3, "C"),
        new(4, "D"),
        new(5, "E"),
        new(6, "F"),
        new(10, "J"),
        new(11, "K"),
        new(12, "L"),
        new(13, "M")
    };

    static readonly NvidiaDlssPresetOption[] RrPresets =
    {
        new(RecommendedPreset, "Recommended"),
        new(1, "A"),
        new(2, "B"),
        new(3, "C"),
        new(4, "D"),
        new(5, "E"),
        new(6, "F")
    };

    public static IReadOnlyList<NvidiaDlssPresetOption> PresetOptions(NvidiaDlssPresetKind kind) => kind switch
    {
        NvidiaDlssPresetKind.FrameGeneration => FgPresets,
        NvidiaDlssPresetKind.SuperResolution => SrPresets,
        NvidiaDlssPresetKind.RayReconstruction => RrPresets,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static bool IsPresetValue(NvidiaDlssPresetKind kind, uint? value)
    {
        if (!value.HasValue) return true;
        foreach (var option in PresetOptions(kind)) if (option.Value == value.Value) return true;
        return false;
    }

    public const uint FgPresetId = 0x10E41DF1, SrPresetId = 0x10E41DF3, RrPresetId = 0x10E41DF7;
    public const uint FgModeId = 0x10308298, FixedFrameCountId = 0x104D6667;
    public const uint DynamicTargetFpsId = 0x10CF4125, DynamicFrameCountId = 0x10562D0F, SmoothMotionId = 0xB0D384C0;
    public const uint FgOverrideId = 0x10E41E03, SrOverrideId = 0x10E41E01, RrOverrideId = 0x10E41E02;
    static readonly uint[] ManagedIds = { FgPresetId, SrPresetId, RrPresetId, FgModeId, FixedFrameCountId, DynamicFrameCountId, DynamicTargetFpsId, SmoothMotionId, FgOverrideId, SrOverrideId, RrOverrideId };

    public static bool IsAccessDenied(Exception error) => error is NVIDIAApiException nv && nv.Status == Status.AccessDenied;

    public static void ApplyGlobal(NvidiaOverridePolicy policy)
    {
        policy?.Validate();
        using var session = DriverSettingsSession.CreateAndLoad();
        ApplyPolicy(session.BaseProfile, policy ?? new NvidiaOverridePolicy());
        session.Save();
    }

    public static void RestoreGlobal()
    {
        using var session = DriverSettingsSession.CreateAndLoad();
        DeleteManaged(session.BaseProfile);
        session.Save();
    }

    public static void ApplyProgram(string executable, NvidiaOverridePolicy policy)
    {
        policy?.Validate();
        string exe = Normalize(executable);
        using var session = DriverSettingsSession.CreateAndLoad();
        var profile = FindProgramProfile(session, exe);
        policy ??= new NvidiaOverridePolicy();
        if (profile == null && policy.IsEmpty) return;
        profile ??= CreateProgramProfile(session, exe);
        ApplyPolicy(profile, policy);
        session.Save();
    }

    public static void RestoreProgram(string executable)
    {
        string exe = Normalize(executable);
        using var session = DriverSettingsSession.CreateAndLoad();
        var profile = FindProgramProfile(session, exe);
        if (profile == null) return;
        DeleteManaged(profile);
        session.Save();
    }

    static string Normalize(string executable) => Path.GetFullPath(executable ?? throw new ArgumentNullException(nameof(executable)));

    static DriverSettingsProfile FindProgramProfile(DriverSettingsSession session, string exe)
    {
        try { return session.FindApplicationProfile(exe); }
        catch (NVIDIAApiException e) when (e.Status == Status.ExecutableNotFound || e.Status == Status.ProfileNotFound) { return null; }
    }

    static DriverSettingsProfile CreateProgramProfile(DriverSettingsSession session, string exe)
    {
        string file = Path.GetFileName(exe);
        var profile = DriverSettingsProfile.CreateProfile(session, "MFG-Enabler - " + file + " - " + StableSuffix(exe), null);
        ProfileApplication.CreateApplication(profile, exe, Path.GetFileNameWithoutExtension(file), null, Array.Empty<string>(), false, null);
        return profile;
    }

    static string StableSuffix(string text)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToUpperInvariant())), 0, 4);
    }

    public static Dictionary<uint, uint?> SettingsFor(NvidiaOverridePolicy p)
    {
        p.Validate();
        return new() {
            [FgPresetId] = p.FgPreset, [SrPresetId] = p.SrPreset, [RrPresetId] = p.RrPreset,
            [FgOverrideId] = p.FgPreset.HasValue ? 1u : null,
            [SrOverrideId] = p.SrPreset.HasValue ? 1u : null,
            [RrOverrideId] = p.RrPreset.HasValue ? 1u : null,
            [FgModeId] = p.FgMode, [FixedFrameCountId] = p.FgMode == 4 ? 0u : p.FixedFrameCount,
            [DynamicFrameCountId] = p.FgMode == 4 ? p.DynamicFrameCount : null,
            [DynamicTargetFpsId] = p.FgMode == 4 ? p.DynamicTargetFps : null,
            [SmoothMotionId] = p.SmoothMotion
        };
    }
    static void ApplyPolicy(DriverSettingsProfile profile, NvidiaOverridePolicy p)
    { foreach (var setting in SettingsFor(p)) SetOrDelete(profile, setting.Key, setting.Value); }

    static void SetOrDelete(DriverSettingsProfile profile, uint id, uint? value)
    {
        if (value.HasValue) profile.SetSetting(id, value.Value); else DeleteSetting(profile, id);
    }

    static void DeleteManaged(DriverSettingsProfile profile) { foreach (uint id in ManagedIds) DeleteSetting(profile, id); }
    static void DeleteSetting(DriverSettingsProfile profile, uint id)
    {
        try { profile.DeleteSetting(id); }
        catch (NVIDIAApiException e) when (e.Status == Status.SettingNotFound) { }
    }
}
