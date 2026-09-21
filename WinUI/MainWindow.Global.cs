using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace MfgEnabler;
public sealed partial class MainWindow
{
    bool globalSync;
    const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static string TrayExecutable => Path.Combine(AppContext.BaseDirectory, "MFG-Enabler.Tray.exe");
    void ApplyGlobalLanguage()
    {
        GlobalTitle.Text = L("MFG override", "MFG 오버라이드");
        GlobalDescription.Text = L("Automatically applies MFG to detected DX12 frame-generation games. Vulkan and unidentified APIs are excluded by default. New games are checked when NVIDIA App updates its library. Disabling this restores all automatically managed games.", "감지된 DX12 프레임 생성 지원 게임에 MFG를 자동 적용합니다. 기본값에서 Vulkan과 API 확인 불가 게임은 제외합니다. NVIDIA App 목록이 갱신되면 새 게임을 확인하며, 끄면 자동 관리하던 모든 게임을 복구합니다.");
        GlobalSwitch.Header = L("Automatic MFG override", "MFG 자동 적용");
        GlobalConfigure.Content = L("Exclusions and startup options", "제외 게임 및 실행 옵션");
        globalSync = true; GlobalSwitch.IsOn = settings.Global?.Enabled == true; globalSync = false;
    }
    void GlobalClick(object sender, RoutedEventArgs e)
    {
        ShowPage(false);
        GraphicsPage.Visibility = Visibility.Collapsed; GlobalPage.Visibility = Visibility.Visible;
        ProgramUnderline.Visibility = Visibility.Collapsed; GlobalUnderline.Visibility = Visibility.Visible;
    }
    void InitializeGlobal()
    {
        InitializeStorageWatcher();
        AppWindow.Changed += (_, e) =>
        {
            if (settings.Global.Enabled && settings.Global.Tray &&
                AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p &&
                p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) Close();
        };
        try { ConfigureGlobalRuntime(); }
        catch (Exception error) { GlobalStatus.Text = error.Message; }
    }
    void ConfigureGlobalRuntime()
    {
        ConfigureStorageWatcher();
        if (AppPaths.IsIsolated) return;
        if (settings.Global.Enabled && settings.Global.Tray)
            StartTray();
        else
            StopTray();
        SetStartup(settings.Global.Enabled && settings.Global.Startup, settings.Global.Tray);
    }
    static void StartTray()
    {
        if (!File.Exists(TrayExecutable))
            throw new FileNotFoundException("MFG-Enabler.Tray.exe is missing.", TrayExecutable);
        Process.Start(new ProcessStartInfo
        {
            FileName = TrayExecutable,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
    static void StopTray()
    {
        if (!File.Exists(TrayExecutable)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = TrayExecutable,
            Arguments = "--quit",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
    static void SetStartup(bool enabled, bool useTray)
    {
        if (AppPaths.IsIsolated) return;
        using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
        if (enabled)
        {
            string executable = useTray ? TrayExecutable : Environment.ProcessPath;
            string arguments = useTray ? "" : " --startup";
            key.SetValue("MFG-Enabler", "\"" + executable + "\"" + arguments);
        }
        else key.DeleteValue("MFG-Enabler", false);
    }
    async void GlobalToggled(object sender, RoutedEventArgs e)
    {
        if (loading || globalSync) return;
        if (busy || dialogOpen) { ApplyGlobalLanguage(); return; }
        if (GlobalSwitch.IsOn) { await ConfigureGlobalDialog(true); return; }
        var old = settings.Global;
        SetBusy(true);
        try
        {
            // Stop new installations before restoring any game. Persist OFF even on partial failure.
            old.Enabled = old.FutureGames = old.Startup = old.Tray = false;
            Disk.Save(SettingsFile, settings);
            SetStartup(false, false);
            ConfigureGlobalRuntime();
            var restore = await RestoreGlobalManaged(old);
            GlobalStatus.Text = restore.failed.Count == 0
                ? L($"Automatic application stopped. Restored {restore.restored} managed game(s).", $"자동 적용을 중지했습니다. 관리 중이던 게임 {restore.restored}개를 복구했습니다.")
                : L($"Automatic application stopped. Restored {restore.restored}; {restore.failed.Count} need manual recovery.", $"자동 적용을 중지했습니다. {restore.restored}개 복구, {restore.failed.Count}개는 수동 복구가 필요합니다.");
        }
        catch (Exception error) { await ShowError(error.Message); }
        finally { SetBusy(false); }
        ApplyGlobalLanguage();
    }
    Task<(int restored, List<string> failed)> RestoreGlobalManaged(GlobalPolicy policy) =>
        Task.Run(() => GlobalApply.Restore(policy, () => Disk.Save(SettingsFile, settings)));
    async void GlobalConfigureClick(object sender, RoutedEventArgs e) => await ConfigureGlobalDialog(settings.Global.Enabled);
    async Task ConfigureGlobalDialog(bool enable)
    {
        if (busy || dialogOpen) { ApplyGlobalLanguage(); return; }
        var old = settings.Global;
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(0, 0, 20, 0) };
        body.Children.Add(new TextBlock { Text = L("Online games may block modified DLLs or penalize your account through anti-cheat. Online status cannot be reliably detected. Select games to EXCLUDE below before enabling. Future games can also include online games.", "온라인 게임에서는 DLL 변경으로 안티치트 차단이나 계정 제재가 발생할 수 있습니다. 온라인 게임 여부를 확실하게 자동 판별할 수 없습니다. 아래에서 적용하지 않을 게임을 체크하세요. 앞으로 추가되는 게임에도 온라인 게임이 포함될 수 있습니다."), TextWrapping = TextWrapping.Wrap });
        var future = new CheckBox { Content = L("Automatically apply to future detected games", "앞으로 발견되는 게임에도 자동 적용"), IsChecked = enable && (!old.Enabled || old.FutureGames) };
        var startup = new CheckBox { Content = L("Start with Windows (only while override is enabled)", "Windows 로그인 시 실행 (오버라이드 사용 중에만)"), IsChecked = enable && (!old.Enabled || old.Startup) };
        var tray = new CheckBox { Content = L("Keep running in tray; start in tray at login", "닫기·최소화 시 트레이에서 실행 / 로그인 시 트레이로 시작"), IsChecked = enable && (!old.Enabled || old.Tray) };
        var vulkan = new CheckBox { Content = L("Include Vulkan / mixed-API games (experimental; runtime compatibility is not guaranteed)", "Vulkan·혼합 API 게임 포함 (실험적 · 런타임 호환 보장 없음)"), IsChecked = old.AllowVulkan, IsEnabled = enable };
        body.Children.Add(new TextBlock { Text = L("DX12 is enabled by default. Unknown APIs are never installed automatically.", "DX12는 기본 적용됩니다. API 확인 불가 게임은 자동 설치하지 않습니다."), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = L("(Idle tray, 30-second local measurement: CPU 0.00%, RAM about 10–15 MiB. Usage varies by system and rises during scans / installation.)", "(트레이 대기 30초 실측: CPU 0.00%, RAM 약 10~15MiB · 환경에 따라 다르며 검색·설치 중에는 증가할 수 있습니다.)"), FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(vulkan);
        future.IsEnabled = startup.IsEnabled = tray.IsEnabled = enable;
        body.Children.Add(future); body.Children.Add(startup); body.Children.Add(tray);
        body.Children.Add(new TextBlock { Text = L("Excluded games (checked = excluded)", "제외할 게임 (체크 = 적용 제외)"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var candidates = games.Where(g => !g.Manual).ToList();
        var boxes = new List<(Game game, CheckBox box)>();
        foreach (var game in candidates)
        {
            var box = new CheckBox { Content = game.Name, IsChecked = old.Excluded.Contains(GlobalPolicy.Key(game), StringComparer.OrdinalIgnoreCase) };
            boxes.Add((game, box)); body.Children.Add(box);
        }
        if (boxes.Count == 0) body.Children.Add(new TextBlock { Text = L("No detected games yet.", "현재 감지된 게임이 없습니다.") });
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = ElementTheme.Dark,
            Title = L("MFG override options", "MFG 오버라이드 설정"), Content = new ScrollViewer { MaxHeight = 440, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body },
            PrimaryButtonText = enable ? L("Accept and enable", "위험 확인 후 활성화") : L("Save", "저장"), CloseButtonText = L("Cancel", "취소"), DefaultButton = ContentDialogButton.Close };
        dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { dialogOpen = false; ResumeStorageScan(); }
        if (result != ContentDialogResult.Primary) { ApplyGlobalLanguage(); return; }
        var next = new GlobalPolicy { Enabled = enable, FutureGames = future.IsChecked == true, Startup = startup.IsChecked == true, Tray = tray.IsChecked == true, AllowVulkan = vulkan.IsChecked == true,
            Approved = new List<string>(old.Approved), Excluded = new List<string>(old.Excluded),
            ManagedFolders = new List<string>(old.ManagedFolders) };
        foreach (var (game, box) in boxes)
        {
            string key = GlobalPolicy.Key(game);
            next.Excluded.RemoveAll(x => String.Equals(x, key, StringComparison.OrdinalIgnoreCase));
            if (box.IsChecked == true) next.Excluded.Add(key);
            else if (!next.Approved.Contains(key, StringComparer.OrdinalIgnoreCase)) next.Approved.Add(key);
        }
        try { settings.Global = next; Disk.Save(SettingsFile, settings); ConfigureGlobalRuntime(); }
        catch (Exception error)
        {
            settings.Global = old;
            try { Disk.Save(SettingsFile, settings); ConfigureGlobalRuntime(); } catch { }
            await ShowError(error.Message);
        }
        ApplyGlobalLanguage();
        if (settings.Global.Enabled) await Scan();
    }
    void ExcludeFromGlobal(Game game)
    {
        if (game == null || !settings.Global.Enabled) return;
        string key = GlobalPolicy.Key(game);
        if (!settings.Global.Excluded.Contains(key, StringComparer.OrdinalIgnoreCase)) settings.Global.Excluded.Add(key);
        Disk.Save(SettingsFile, settings);
    }
    async Task ApplyGlobalOverrides()
    {
        if (settings.Global?.Enabled != true) return;
        var policy = settings.Global;
        var snapshot = games.ToList();
        var results = await Task.Run(() => GlobalApply.Apply(policy, snapshot, () => Disk.Save(SettingsFile, settings)));
        GlobalStatus.Text = DateTime.Now.ToString("HH:mm:ss") + " · " + L("Check complete", "확인 완료") +
            (results.Count == 0 ? "" : "\n" + String.Join("\n", results));
    }
}
