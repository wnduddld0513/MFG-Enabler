using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MfgEnabler;

public sealed partial class MainWindow
{
    void ApplyNvidiaLanguage()
    {
        if (ProgramNvidiaTitle == null) return;

        ProgramNvidiaTitle.Text = L("NVIDIA App Override", "NVIDIA App 오버라이드");
        GlobalNvidiaTitle.Text = L("NVIDIA App Override", "NVIDIA App 오버라이드");
        ProgramDlssSectionTitle.Text = GlobalDlssSectionTitle.Text = L("DLSS presets", "DLSS 프리셋");
        ProgramMfgSectionTitle.Text = GlobalMfgSectionTitle.Text = L("MFG settings", "MFG 설정");
        ProgramNvidiaHint.Text = L("Unset program values inherit the global NVIDIA driver setting.", "프로그램에서 설정하지 않은 값은 NVIDIA 글로벌 설정을 상속합니다.");
        GlobalNvidiaHint.Text = L("Writes the selected NVIDIA driver profile values. Default removes this app's override for that item.", "선택한 값을 NVIDIA 드라이버 프로필에 기록합니다. 기본값은 해당 항목의 이 앱 오버라이드를 제거합니다.");

        SetNvidiaLabels(false);
        SetNvidiaLabels(true);
        FillNvidiaChoices(false);
        FillNvidiaChoices(true);
        RefreshNvidiaProgram();
        RefreshNvidiaGlobal();
    }

    void SetNvidiaLabels(bool global)
    {
        TextBlock fg = global ? GlobalFgPresetLabel : ProgramFgPresetLabel;
        TextBlock sr = global ? GlobalSrPresetLabel : ProgramSrPresetLabel;
        TextBlock rr = global ? GlobalRrPresetLabel : ProgramRrPresetLabel;
        TextBlock mode = global ? GlobalFgModeLabel : ProgramFgModeLabel;
        TextBlock fixedCount = global ? GlobalFixedCountLabel : ProgramFixedCountLabel;
        TextBlock dynamicCount = global ? GlobalDynamicCountLabel : ProgramDynamicCountLabel;
        TextBlock target = global ? GlobalDynamicTargetLabel : ProgramDynamicTargetLabel;
        TextBlock smooth = global ? GlobalSmoothMotionLabel : ProgramSmoothMotionLabel;
        fg.Text = L("Frame Generation preset", "Frame Generation 프리셋");
        sr.Text = L("DLSS Super Resolution preset", "DLSS Super Resolution 프리셋");
        rr.Text = L("DLSS Ray Reconstruction preset", "DLSS Ray Reconstruction 프리셋");
        mode.Text = L("FG mode", "FG 모드");
        fixedCount.Text = L("Fixed MFG multiplier", "고정 MFG 배수");
        dynamicCount.Text = L("Dynamic MFG maximum multiplier", "동적 MFG 최대 배수");
        target.Text = L("Dynamic target FPS", "동적 목표 FPS");
        smooth.Text = L("Smooth Motion", "Smooth Motion");
        if (global)
        {
            GlobalNvidiaApply.Content = L("Apply NVIDIA settings", "NVIDIA 설정 적용");
            GlobalNvidiaRestore.Content = L("Restore NVIDIA settings", "NVIDIA 설정 복구");
        }
        else
        {
            ProgramNvidiaApply.Content = L("Apply NVIDIA settings", "NVIDIA 설정 적용");
            ProgramNvidiaRestore.Content = L("Restore NVIDIA settings", "NVIDIA 설정 복구");
        }
    }

    void FillNvidiaChoices(bool global)
    {
        string first = L("Default", "기본값");
        ComboBox fg = global ? GlobalFgPresetCombo : ProgramFgPresetCombo;
        ComboBox sr = global ? GlobalSrPresetCombo : ProgramSrPresetCombo;
        ComboBox rr = global ? GlobalRrPresetCombo : ProgramRrPresetCombo;
        ComboBox mode = global ? GlobalFgModeCombo : ProgramFgModeCombo;
        ComboBox fixedCount = global ? GlobalFixedCountCombo : ProgramFixedCountCombo;
        ComboBox dynamicCount = global ? GlobalDynamicCountCombo : ProgramDynamicCountCombo;
        ComboBox target = global ? GlobalDynamicTargetCombo : ProgramDynamicTargetCombo;
        ComboBox smooth = global ? GlobalSmoothMotionCombo : ProgramSmoothMotionCombo;

        fg.ItemsSource = PresetChoices(first, true);
        sr.ItemsSource = PresetChoices(first, false);
        rr.ItemsSource = PresetChoices(first, false);
        mode.ItemsSource = new[] { first, "N/A", L("Fixed", "고정"), L("Dynamic", "동적") };
        fixedCount.ItemsSource = FrameCountChoices(first);
        dynamicCount.ItemsSource = FrameCountChoices(first);
        var targets = new List<string> { first, L("Max refresh rate", "최대 주사율") };
        targets.AddRange(Enumerable.Range(60, 441).Select(x => x + " FPS"));
        target.ItemsSource = targets;
        smooth.ItemsSource = new[] { first, L("Off", "끄기"), L("On", "켜기") };
    }

    string[] PresetChoices(string first, bool fg)
    {
        var list = new List<string> { first };
        if (fg) list.Add(L("Recommended", "권장"));
        list.Add(fg ? L("Latest", "최신") : L("Recommended", "권장"));
        int count = fg ? 26 : 15;
        for (int i = 0; i < count; i++) list.Add(((char)('A' + i)).ToString());
        return list.ToArray();
    }

    string[] FrameCountChoices(string first) => new[] { first, "N/A", "2X", "3X", "4X", "5X", "6X" };

    static uint? PresetValue(ComboBox combo, bool fg)
    {
        int i = combo.SelectedIndex;
        if (i <= 0) return null;
        if (fg)
        {
            if (i == 1) return 0x00FFFFFE;
            if (i == 2) return 0x00FFFFFF;
            return (uint)(i - 2);
        }
        if (i == 1) return 0x00FFFFFF;
        return (uint)(i - 1);
    }

    static int PresetIndex(uint? value, bool fg)
    {
        if (!value.HasValue) return 0;
        if (fg && value == 0x00FFFFFE) return 1;
        if (value == 0x00FFFFFF) return fg ? 2 : 1;
        uint max = fg ? 26u : 15u;
        if (value >= 1 && value <= max) return fg ? (int)value + 2 : (int)value + 1;
        return 0;
    }

    static uint? ModeValue(ComboBox combo) => combo.SelectedIndex switch { 1 => 0u, 2 => 2u, 3 => 4u, _ => null };
    static int ModeIndex(uint? value) => value switch { 0u => 1, 2u => 2, 4u => 3, _ => 0 };
    static uint? CountValue(ComboBox combo) => combo.SelectedIndex <= 0 ? null : (uint)(combo.SelectedIndex - 1);
    static int CountIndex(uint? value) => value is <= 5u ? (int)value.Value + 1 : 0;
    static uint? SmoothValue(ComboBox combo) => combo.SelectedIndex switch { 1 => 0u, 2 => 1u, _ => null };
    static int SmoothIndex(uint? value) => value switch { 0u => 1, 1u => 2, _ => 0 };
    static uint? TargetValue(ComboBox combo) => combo.SelectedIndex switch
    {
        <= 0 => null,
        1 => 0x01000000,
        _ => (uint)(combo.SelectedIndex + 58)
    };
    static int TargetIndex(uint? value)
    {
        if (!value.HasValue) return 0;
        if (value == 0x01000000) return 1;
        return value is >= 60u and <= 500u ? (int)value.Value - 58 : 0;
    }

    NvidiaOverridePolicy ReadNvidiaPolicy(bool global)
    {
        return new NvidiaOverridePolicy
        {
            FgPreset = PresetValue(global ? GlobalFgPresetCombo : ProgramFgPresetCombo, true),
            SrPreset = PresetValue(global ? GlobalSrPresetCombo : ProgramSrPresetCombo, false),
            RrPreset = PresetValue(global ? GlobalRrPresetCombo : ProgramRrPresetCombo, false),
            FgMode = ModeValue(global ? GlobalFgModeCombo : ProgramFgModeCombo),
            FixedFrameCount = CountValue(global ? GlobalFixedCountCombo : ProgramFixedCountCombo),
            DynamicFrameCount = CountValue(global ? GlobalDynamicCountCombo : ProgramDynamicCountCombo),
            DynamicTargetFps = TargetValue(global ? GlobalDynamicTargetCombo : ProgramDynamicTargetCombo),
            SmoothMotion = SmoothValue(global ? GlobalSmoothMotionCombo : ProgramSmoothMotionCombo)
        };
    }

    void LoadNvidiaPolicy(bool global, NvidiaOverridePolicy p)
    {
        p ??= new NvidiaOverridePolicy();
        (global ? GlobalFgPresetCombo : ProgramFgPresetCombo).SelectedIndex = PresetIndex(p.FgPreset, true);
        (global ? GlobalSrPresetCombo : ProgramSrPresetCombo).SelectedIndex = PresetIndex(p.SrPreset, false);
        (global ? GlobalRrPresetCombo : ProgramRrPresetCombo).SelectedIndex = PresetIndex(p.RrPreset, false);
        (global ? GlobalFgModeCombo : ProgramFgModeCombo).SelectedIndex = ModeIndex(p.FgMode);
        (global ? GlobalFixedCountCombo : ProgramFixedCountCombo).SelectedIndex = CountIndex(p.FixedFrameCount);
        (global ? GlobalDynamicCountCombo : ProgramDynamicCountCombo).SelectedIndex = CountIndex(p.DynamicFrameCount);
        (global ? GlobalDynamicTargetCombo : ProgramDynamicTargetCombo).SelectedIndex = TargetIndex(p.DynamicTargetFps);
        (global ? GlobalSmoothMotionCombo : ProgramSmoothMotionCombo).SelectedIndex = SmoothIndex(p.SmoothMotion);
        UpdateNvidiaModeAvailability(global);
    }

    void NvidiaModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, ProgramFgModeCombo)) UpdateNvidiaModeAvailability(false);
        else if (ReferenceEquals(sender, GlobalFgModeCombo)) UpdateNvidiaModeAvailability(true);
    }

    void UpdateNvidiaModeAvailability(bool global)
    {
        ComboBox mode = global ? GlobalFgModeCombo : ProgramFgModeCombo;
        ComboBox fixedCount = global ? GlobalFixedCountCombo : ProgramFixedCountCombo;
        ComboBox dynamicCount = global ? GlobalDynamicCountCombo : ProgramDynamicCountCombo;
        ComboBox target = global ? GlobalDynamicTargetCombo : ProgramDynamicTargetCombo;
        Button apply = global ? GlobalNvidiaApply : ProgramNvidiaApply;
        if (mode == null || fixedCount == null || dynamicCount == null || target == null || apply == null) return;

        bool dynamic = mode.SelectedIndex == 3;
        bool enabled = apply.IsEnabled;
        if (dynamic && fixedCount.SelectedIndex != 1) fixedCount.SelectedIndex = 1;
        fixedCount.IsEnabled = enabled && !dynamic;
        dynamicCount.IsEnabled = enabled && dynamic;
        target.IsEnabled = enabled && dynamic;
    }

    string CurrentNvidiaExe()
    {
        string exe = ExeCombo?.SelectedItem as string;
        return String.IsNullOrWhiteSpace(exe) ? null : Path.GetFullPath(exe);
    }

    void RefreshNvidiaProgram()
    {
        if (ProgramNvidiaTitle == null) return;
        string exe = CurrentNvidiaExe();
        NvidiaOverridePolicy policy = null;
        if (exe != null) settings.NvidiaPrograms?.TryGetValue(exe, out policy);
        LoadNvidiaPolicy(false, policy ?? new NvidiaOverridePolicy());
        bool enabled = !busy && exe != null;
        SetProgramNvidiaEnabled(enabled);
        if (exe == null) ProgramNvidiaState.Text = "";
    }

    void RefreshNvidiaGlobal()
    {
        if (GlobalNvidiaTitle == null) return;
        LoadNvidiaPolicy(true, settings.NvidiaGlobal ?? new NvidiaOverridePolicy());
        SetGlobalNvidiaEnabled(!busy);
    }

    void SetNvidiaBusy(bool value)
    {
        if (ProgramNvidiaTitle == null) return;
        SetProgramNvidiaEnabled(!value && CurrentNvidiaExe() != null);
        SetGlobalNvidiaEnabled(!value);
    }

    void SetProgramNvidiaEnabled(bool value)
    {
        foreach (var c in new[] { ProgramFgPresetCombo, ProgramSrPresetCombo, ProgramRrPresetCombo, ProgramFgModeCombo, ProgramFixedCountCombo, ProgramDynamicCountCombo, ProgramDynamicTargetCombo, ProgramSmoothMotionCombo }) c.IsEnabled = value;
        ProgramNvidiaApply.IsEnabled = ProgramNvidiaRestore.IsEnabled = value;
        UpdateNvidiaModeAvailability(false);
    }

    void SetGlobalNvidiaEnabled(bool value)
    {
        foreach (var c in new[] { GlobalFgPresetCombo, GlobalSrPresetCombo, GlobalRrPresetCombo, GlobalFgModeCombo, GlobalFixedCountCombo, GlobalDynamicCountCombo, GlobalDynamicTargetCombo, GlobalSmoothMotionCombo }) c.IsEnabled = value;
        GlobalNvidiaApply.IsEnabled = GlobalNvidiaRestore.IsEnabled = value;
        UpdateNvidiaModeAvailability(true);
    }

    async Task ShowNvidiaAccessDenied()
    {
        await ShowDialog(L("NVIDIA settings access denied", "NVIDIA 설정 권한 거부"),
            L("NVIDIA driver settings access was denied. No verification or retry was attempted. Test the selected setting manually in NVIDIA App or the game.",
              "NVIDIA 드라이버 설정 접근 권한이 거부되었습니다. 검증이나 재시도는 하지 않습니다. 선택한 설정은 NVIDIA App 또는 게임에서 직접 테스트해 주세요."));
    }

    async void ProgramNvidiaApplyClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        string exe = CurrentNvidiaExe();
        if (exe == null) return;
        var policy = ReadNvidiaPolicy(false);
        SetBusy(true);
        try
        {
            await Task.Run(() => NvidiaOverrides.ApplyProgram(exe, policy));
            settings.NvidiaPrograms[exe] = policy;
            Disk.Save(SettingsFile, settings);
            ProgramNvidiaState.Text = L("NVIDIA settings saved. Test in game.", "NVIDIA 설정을 저장했습니다. 게임에서 테스트하세요.");
        }
        catch (Exception error)
        {
            if (NvidiaOverrides.IsAccessDenied(error)) await ShowNvidiaAccessDenied();
            else await ShowError(error.Message);
        }
        finally { SetBusy(false); }
    }

    async void ProgramNvidiaRestoreClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        string exe = CurrentNvidiaExe();
        if (exe == null) return;
        SetBusy(true);
        try
        {
            await Task.Run(() => NvidiaOverrides.RestoreProgram(exe));
            settings.NvidiaPrograms.Remove(exe);
            Disk.Save(SettingsFile, settings);
            RefreshNvidiaProgram();
            ProgramNvidiaState.Text = L("Managed NVIDIA program overrides removed.", "관리 중인 NVIDIA 프로그램 오버라이드를 제거했습니다.");
        }
        catch (Exception error)
        {
            if (NvidiaOverrides.IsAccessDenied(error)) await ShowNvidiaAccessDenied();
            else await ShowError(error.Message);
        }
        finally { SetBusy(false); }
    }

    async void GlobalNvidiaApplyClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var policy = ReadNvidiaPolicy(true);
        SetBusy(true);
        try
        {
            await Task.Run(() => NvidiaOverrides.ApplyGlobal(policy));
            settings.NvidiaGlobal = policy;
            Disk.Save(SettingsFile, settings);
            GlobalNvidiaState.Text = L("Global NVIDIA settings saved. Test in game.", "글로벌 NVIDIA 설정을 저장했습니다. 게임에서 테스트하세요.");
        }
        catch (Exception error)
        {
            if (NvidiaOverrides.IsAccessDenied(error)) await ShowNvidiaAccessDenied();
            else await ShowError(error.Message);
        }
        finally { SetBusy(false); }
    }

    async void GlobalNvidiaRestoreClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            await Task.Run(NvidiaOverrides.RestoreGlobal);
            settings.NvidiaGlobal = new NvidiaOverridePolicy();
            Disk.Save(SettingsFile, settings);
            RefreshNvidiaGlobal();
            GlobalNvidiaState.Text = L("Managed NVIDIA global overrides removed.", "관리 중인 NVIDIA 글로벌 오버라이드를 제거했습니다.");
        }
        catch (Exception error)
        {
            if (NvidiaOverrides.IsAccessDenied(error)) await ShowNvidiaAccessDenied();
            else await ShowError(error.Message);
        }
        finally { SetBusy(false); }
    }
}
