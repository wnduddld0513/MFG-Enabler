using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace MfgEnabler;

public sealed partial class MainWindow : Window
{
    readonly string dataDir = AppPaths.DataDir;
    List<Game> games = new();
    DesktopSettings settings = new();
    bool loading = true, busy, updateChecked, initialized, settingsVisible;
    bool retireSpoofOnce;
    string scanDetails = "";
    // DPI-aware window sizing (logical DIPs, NVIDIA-App-like). AppWindow uses
    // physical pixels, so a fixed 1120x780 looks tiny on 150%/200% monitors.
    const double DesiredLogicalWidth = 1120;
    const double DesiredLogicalHeight = 780;
    const double MinLogicalWidth = 980;
    const double MinLogicalHeight = 640;
    const double MaxWorkWidthRatio = 0.88;
    const double MaxWorkHeightRatio = 0.90;
    IntPtr hwnd = IntPtr.Zero;
    ulong lastDisplayValue;
    uint lastDpi;
    double appliedLogW = DesiredLogicalWidth, appliedLogH = DesiredLogicalHeight;
    bool adjusting;
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    string LibraryFile => Path.Combine(dataDir, "games.json");
    string SettingsFile => Path.Combine(dataDir, "settings.json");
    string L(string en, string ko) => settings.Language == "ko" ? ko : en;
    Game Selected => GameList.SelectedItem as Game;
    public MainWindow()
    {
        InitializeComponent();
        Title = "MFG Enabler";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MFG-Enabler.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        EnsureOverlapped();
        ApplyAdaptiveWindowSize(center: true);
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Colors.LightGray;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.Closing += (_, e) => { if (busy) e.Cancel = true; };
        ProxyCombo.ItemsSource = Payload.Proxies;
        ProxyCombo.SelectedIndex = 0;
        Root.Loaded += async (_, _) => { if (initialized) return; initialized = true; await InitializeAsync(); };
    }
    void EnsureOverlapped()
    {
        if (AppWindow.Presenter is OverlappedPresenter p) { p.IsResizable = true; p.IsMaximizable = true; p.IsMinimizable = true; }
        else { try { AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped); } catch { } }
    }
    uint CurrentDpi()
    {
        try { uint d = GetDpiForWindow(hwnd); if (d != 0) return d; } catch { }
        try { return GetDpiForSystem(); } catch { }
        return 96;
    }
    DisplayArea CurrentDisplay()
    {
        try { return DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary); } catch { return null; }
    }
    void ApplyAdaptiveWindowSize(bool center)
    {
        if (hwnd == IntPtr.Zero) return;
        var area = CurrentDisplay();
        if (area == null) return;
        uint dpi = CurrentDpi();
        if (dpi == 0) dpi = 96;
        double scale = dpi / 96.0;
        var work = area.WorkArea;
        if (work.Width <= 0 || work.Height <= 0) return;
        double workLogW = work.Width / scale, workLogH = work.Height / scale;
        double maxW = workLogW * MaxWorkWidthRatio, maxH = workLogH * MaxWorkHeightRatio;
        double w = Math.Min(DesiredLogicalWidth, maxW);
        double h = Math.Min(DesiredLogicalHeight, maxH);
        w = Math.Max(w, Math.Max(320, Math.Min(MinLogicalWidth, maxW)));
        h = Math.Max(h, Math.Max(240, Math.Min(MinLogicalHeight, maxH)));
        appliedLogW = w; appliedLogH = h; lastDpi = dpi; lastDisplayValue = area.DisplayId.Value;
        int physW = Math.Max(1, (int)Math.Round(w * scale));
        int physH = Math.Max(1, (int)Math.Round(h * scale));
        physW = Math.Min(physW, Math.Max(1, work.Width));
        physH = Math.Min(physH, Math.Max(1, work.Height));
        adjusting = true;
        try
        {
            if (center)
            {
                int x = work.X + Math.Max(0, (work.Width - physW) / 2);
                int y = work.Y + Math.Max(0, (work.Height - physH) / 2);
                AppWindow.MoveAndResize(new RectInt32(x, y, physW, physH));
            }
            else AppWindow.Resize(new SizeInt32(physW, physH));
        }
        finally { adjusting = false; }
    }
    void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs e)
    {
        if (adjusting || hwnd == IntPtr.Zero) return;
        try
        {
            if (sender.Presenter is OverlappedPresenter op && op.State != OverlappedPresenterState.Restored) return;
        }
        catch { }
        if (!e.DidPositionChange && !e.DidSizeChange) return;
        uint dpi = CurrentDpi();
        if (dpi == 0) dpi = 96;
        var area = CurrentDisplay();
        if (area == null) return;
        bool displayChanged = area.DisplayId.Value != lastDisplayValue;
        bool dpiChanged = dpi != lastDpi;
        if (!displayChanged && !dpiChanged) return; // user resize: respect it
        double scale = dpi / 96.0;
        var work = area.WorkArea;
        double workLogW = work.Width / scale, workLogH = work.Height / scale;
        double w = Math.Min(appliedLogW, workLogW * MaxWorkWidthRatio);
        double h = Math.Min(appliedLogH, workLogH * MaxWorkHeightRatio);
        w = Math.Max(w, Math.Max(320, Math.Min(MinLogicalWidth, workLogW * MaxWorkWidthRatio)));
        h = Math.Max(h, Math.Max(240, Math.Min(MinLogicalHeight, workLogH * MaxWorkHeightRatio)));
        int physW = Math.Max(1, (int)Math.Round(w * scale));
        int physH = Math.Max(1, (int)Math.Round(h * scale));
        var cur = sender.Size;
        lastDpi = dpi; lastDisplayValue = area.DisplayId.Value; appliedLogW = w; appliedLogH = h;
        if (Math.Abs(cur.Width - physW) <= 1 && Math.Abs(cur.Height - physH) <= 1) return;
        adjusting = true;
        try { sender.Resize(new SizeInt32(physW, physH)); }
        finally { adjusting = false; }
    }
    async Task InitializeAsync()
    {
        string error = null;
        bool hadSettings = File.Exists(SettingsFile);
        try
        {
            Directory.CreateDirectory(dataDir);
            if (File.Exists(SettingsFile)) settings = Disk.Read<DesktopSettings>(SettingsFile) ?? new();
            if (settings.Language != "ko") settings.Language = "en";
            if (!settings.Migrated11)
            {
                settings.AutoUpdate = true;
                settings.Migrated11 = true;
                retireSpoofOnce = true;
                Disk.Save(SettingsFile, settings);
            }
            if (File.Exists(LibraryFile)) games = Disk.Read<List<Game>>(LibraryFile) ?? new();
        }
        catch (Exception e) { error = e.Message; }
        LanguageCombo.SelectedIndex = settings.Language == "ko" ? 1 : 0;
        AutoSwitch.IsOn = settings.AutoUpdate;
        if (settings.AppChannel != "main" && settings.AppChannel != "beta")
            settings.AppChannel = ReleaseNumber.Parse(AppUpdates.CurrentVersion)?.Beta != null ? "beta" : "main";
        AppChannelCombo.SelectedIndex = settings.AppChannel == "beta" ? 1 : 0;
        settings.Global ??= new();
        settings.Global.Normalize();
        settings.NvidiaGlobal ??= new();
        settings.NvidiaPrograms = new Dictionary<string, NvidiaOverridePolicy>(settings.NvidiaPrograms ?? new(), StringComparer.OrdinalIgnoreCase);
        ApplyLanguage(); ShowPage(false);
        loading = false;
        InitializeGlobal();
        if (error != null) await ShowError(error);
        LoadingOverlay.Visibility = Visibility.Visible;
        try { await Scan(); }
        finally { LoadingOverlay.Visibility = Visibility.Collapsed; }
        await UpdateOnce();
        if (!Environment.GetCommandLineArgs().Contains("--startup")) { if (hadSettings) await ShowWhatsNewOnce(); await CheckAppUpdate(false); }
    }
    void ApplyLanguage()
    {
        Root.Language = settings.Language == "ko" ? "ko-KR" : "en-US";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MoreButton, L("More options", "추가 옵션"));
        GraphicsNavText.Text = L("Graphics", "그래픽"); SettingsNavText.Text = L("Settings", "설정");
        PageTitle.Text = settingsVisible ? L("Settings", "설정") : L("Graphics", "그래픽");
        ProgramTab.Content = L("Program settings", "프로그램 설정"); GlobalTab.Content = L("Global settings", "글로벌 설정");
        ApplyGlobalLanguage();
        Search.PlaceholderText = L("Search programs", "프로그램 검색");
        RefreshButton.Content = RefreshMenu.Text = L("Refresh", "새로 고침");
        NvidiaButton.Content = NvidiaMenu.Text = L("Open NVIDIA App", "NVIDIA App 열기");
        AddProgramMenu.Text = L("Add program", "프로그램 추가");
        DeleteProgramMenu.Text = L("Delete program", "프로그램 삭제");
        DetailsButton.Content = DetailsMenu.Text = L("Detection details", "감지 상세");
        EmptyTitle.Text = L("Select a program to get started", "프로그램을 선택하세요");
        EmptyDescription.Text = L("Scan for games in NVIDIA App, then refresh this list. Only detected frame-generation candidates and managed recovery entries appear here.", "NVIDIA App에서 게임을 검색한 뒤 목록을 새로 고치세요. 프레임 생성 후보와 기존 복구 항목이 표시됩니다.");
        DriverTitle.Text = L("Program settings", "프로그램 설정"); RestoreButton.Content = L("Restore", "복구");
        ExeLabel.Text = L("Detected executable · choose the rendering EXE", "감지된 실행 파일 · 렌더링 EXE 선택");
        ProxyLabel.Text = L("Installation proxy", "설치 프록시");
        ProxyDetectButton.Content = L("Find recommended", "추천 프록시 찾기");
        MfgLabel.Text = L("Enable MFG", "MFG 활성화");
        MfgDescription.Text = L("Installs dlssg_for_sm86.", "dlssg_for_sm86을 설치합니다.");
        FolderButton.Content = L("Open game folder", "게임 폴더 열기");
        GeneralTitle.Text = L("General", "일반"); LanguageLabel.Text = L("Language", "언어");
        UpdatesTitle.Text = L("Updates", "업데이트"); AutoLabel.Text = L("dlssg_for_sm86 automatic updates", "dlssg_for_sm86 자동 업데이트");
        AutoDescription.Text = L("Automatically checks for dlssg_for_sm86 updates.", "자동으로 dlssg_for_sm86 업데이트를 확인합니다.");
        RuntimeNotesButton.Content = L("View dlssg_for_sm86 release notes", "dlssg_for_sm86 릴리스 노트 보기");
        AboutTitle.Text = L("About", "정보");
         AppVersionText.Text = "MFG-Enabler " + AppUpdates.CurrentVersion;
        ApplyAppUpdateLanguage();
        AboutDescription.Text = L("MFG activation tool for RTX 20/30/40 series.", "RTX 20/30/40 시리즈용 mfg 활성화 툴 입니다.");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MfgSwitch, MfgLabel.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AutoSwitch, AutoLabel.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LanguageCombo, LanguageLabel.Text);
        LoadingText.Text = L("Loading…", "불러오는 중…");
        ApplyIniLanguage();
        ApplyNvidiaLanguage();
        RefreshCount(); UpdateState();
    }
    void ShowPage(bool showSettings)
    {
        GlobalPage.Visibility = Visibility.Collapsed;
        GraphicsTabs.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        settingsVisible = showSettings;
        GraphicsRail.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        SettingsRail.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        GraphicsPage.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        SettingsPage.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        ProgramUnderline.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        GlobalUnderline.Visibility = Visibility.Collapsed;
        var graphicsBrush = new SolidColorBrush(showSettings ? Colors.LightGray : ColorHelper.FromArgb(255,118,185,0));
        var settingsBrush = new SolidColorBrush(showSettings ? ColorHelper.FromArgb(255,118,185,0) : Colors.LightGray);
        GraphicsNav.Foreground = graphicsBrush;
        SettingsNav.Foreground = settingsBrush;
        GraphicsNavIcon.Foreground = GraphicsNavText.Foreground = graphicsBrush;
        SettingsNavIcon.Foreground = SettingsNavText.Foreground = settingsBrush;
        PageTitle.Text = showSettings ? L("Settings", "설정") : L("Graphics", "그래픽");
    }
    void GraphicsClick(object sender, RoutedEventArgs e) => ShowPage(false);
    void SettingsClick(object sender, RoutedEventArgs e) => ShowPage(true);
    async void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        var previous = settings.Language;
        settings.Language = LanguageCombo.SelectedIndex == 1 ? "ko" : "en";
        try { Disk.Save(SettingsFile, settings); ApplyLanguage(); }
        catch (Exception error)
        {
            settings.Language = previous; loading = true;
            LanguageCombo.SelectedIndex = previous == "ko" ? 1 : 0; loading = false;
            await ShowError(error.Message);
        }
    }
    void SaveLibrary() => Disk.Save(LibraryFile, games);
    void RefreshCount() { CountText.Text = (GameList.Items?.Count ?? 0) + L(" programs", "개 프로그램"); }
    void RefreshList()
    {
        var selected = Selected;
        GameList.ItemsSource = games.Where(g => g.Name.IndexOf(Search.Text, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
        GameList.SelectedItem = selected != null && GameList.Items.Contains(selected) ? selected : GameList.Items.FirstOrDefault();
        RefreshCount(); SelectGame();
    }
    void SearchChanged(object sender, TextChangedEventArgs e) { if (!loading) RefreshList(); }
    void GameChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SelectGame);
    }
    void GameItemPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (loading || e.OriginalSource is not DependencyObject current) return;
        while (current != null && current is not ListViewItem) current = VisualTreeHelper.GetParent(current);
        if (current is ListViewItem item && item.Content is Game game && !ReferenceEquals(GameList.SelectedItem, game))
            GameList.SelectedItem = game;
    }
    void SelectGame()
    {
        loading = true;
        var game = Selected;
        ExeCombo.ItemsSource = game?.Executables ?? new List<string>();
        ExeCombo.SelectedItem = game?.Exe;
        GameTitle.Text = game?.Name ?? "";
        GamePath.Text = game?.Root ?? "";
        DetailPanel.Visibility = game == null ? Visibility.Collapsed : Visibility.Visible;
        EmptyPanel.Visibility = game == null ? Visibility.Visible : Visibility.Collapsed;
        loading = false; UpdateState();
    }
    async void ExeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || Selected == null) return;
        string previous = Selected.Exe;
        try { Selected.Exe = ExeCombo.SelectedItem as string; SaveLibrary(); }
        catch (Exception error) { Selected.Exe = previous; loading = true; ExeCombo.SelectedItem = previous; loading = false; await ShowError(error.Message); }
        UpdateState();
    }
    Installer CurrentTarget() => new Installer(Path.GetDirectoryName(ExeCombo.SelectedItem as string ?? throw new IOException(L("Select a rendering executable.", "렌더링 실행 파일을 선택하세요."))));
    string StatusLabel(string value) => value switch
    {
        "적용됨" => L("Applied", value), "미적용" => L("Off", value), "복구 필요" => L("Recovery required", value),
        "파일 변경 감지" => L("File changes detected", value), _ => L("Check recovery records", value)
    };
    void UpdateState()
    {
        if (MfgSwitch == null) return;
        bool previous = loading; loading = true;
        try
        {
            bool available = Selected != null && ExeCombo.SelectedItem != null;
            var target = available ? CurrentTarget() : null;
            string status = available ? target.Status() : "미적용";
            string spoof = available ? new Installer(CurrentTarget().Target, true).Status() : "미적용";
            bool eligible = Selected?.CanEnable == true;
            if (available)
            {
                Journal journal = target.Load();
                string installedProxy = journal != null && journal.Phase == "enabled" ? journal.Proxy : null;
                if (!String.IsNullOrEmpty(installedProxy) && Payload.Proxies.Contains(installedProxy))
                    ProxyCombo.SelectedItem = installedProxy;
                else if (status == "미적용")
                {
                    var recommendation = ProxyRecommendations.Get(Selected);
                    if (!String.IsNullOrEmpty(recommendation?.Proxy) && Payload.Proxies.Contains(recommendation.Proxy))
                        ProxyCombo.SelectedItem = recommendation.Proxy;
                    else if (ProxyCombo.SelectedItem == null) ProxyCombo.SelectedIndex = 0;
                }
            }
            MfgSwitch.IsOn = status == "적용됨";
            MfgSwitch.IsEnabled = !busy && available && (status == "적용됨" || status == "미적용" && eligible);
            RestoreButton.IsEnabled = !busy && available && Installer.HasAny(CurrentTarget().Target);
            ProxyCombo.IsEnabled = !busy && available && eligible && (status == "미적용" || status == "적용됨");
            ProxyDetectButton.IsEnabled = !busy && available && eligible && (status == "미적용" || status == "적용됨");
            FolderButton.IsEnabled = available;
            string ineligibleNote = eligible ? "" : "\n" + (String.IsNullOrEmpty(Selected?.Evidence) ? L("Recovery only. This program is no longer an eligible detected candidate.", "복구 전용 · 현재 감지된 적용 후보가 아닙니다.") : LocalizeCore(Selected.Evidence));
            StateText.Text = available ? "MFG: " + StatusLabel(status) + ineligibleNote :
                (!eligible && !String.IsNullOrEmpty(Selected?.Evidence) ? LocalizeCore(Selected.Evidence) :
                L("Choose the rendering executable recorded by NVIDIA App.", "NVIDIA App이 기록한 렌더링 실행 파일을 선택하세요."));
        }
        catch (Exception error) { MfgSwitch.IsEnabled = RestoreButton.IsEnabled = ProxyCombo.IsEnabled = ProxyDetectButton.IsEnabled = false; StateText.Text = L("Cannot read this target. ", "대상 확인 실패. ") + error.Message; }
        finally { loading = previous; RefreshIniEditor(); RefreshNvidiaProgram(); }
    }
    void SetBusy(bool value)
    {
        busy = value; GameList.IsEnabled = Search.IsEnabled = ExeCombo.IsEnabled = RefreshButton.IsEnabled = RefreshMenu.IsEnabled = NvidiaButton.IsEnabled = NvidiaMenu.IsEnabled = AutoSwitch.IsEnabled = !value;
        AppUpdateButton.IsEnabled = AppChannelCombo.IsEnabled = LanguageCombo.IsEnabled = RuntimeNotesButton.IsEnabled = !value;
        GlobalSwitch.IsEnabled = GlobalConfigure.IsEnabled = !value;
        SetNvidiaBusy(value);
        UpdateState();
        if (!value) ResumeStorageScan();
    }
    void ProgressText(string text) { }
    string LocalizeCore(string text) => settings.Language == "ko" ? text : CoreText.English(text);
    async Task Scan()
    {
        if (busy) return;
        SetBusy(true);
        var saved = games.ToList();
        try
        {
            var report = await Task.Run(() => Discovery.Scan());
            games = await Task.Run(() => Discovery.MergeLibrary(saved, report.Games));
            SaveLibrary(); RefreshList();
            await ApplyGlobalOverrides();
            scanDetails = report.Summary + "\n" + report.StorageFile + "\n" + report.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            if (retireSpoofOnce)
            {
                retireSpoofOnce = false;
                int retiredSpoof = RetireSpoofSlots();
                if (retiredSpoof > 0) await ShowDialog(L("Legacy FakeNvAPI installs retired", "기존 FakeNvAPI 설치 정리됨"), settings.Language == "ko" ? $"적용 중이던 FakeNvAPI {retiredSpoof}건을 복구했습니다. 드라이버 오류 예방을 위해 신규 설치는 제공하지 않습니다." : $"Retired {retiredSpoof} active FakeNvAPI install(s). New installs are no longer offered to avoid driver issues.");
            }
        }
        catch (Exception error)
        {
            scanDetails = error.Message + "\n\n" + Discovery.DefaultStorage;
            games = Discovery.MergeLibrary(saved, new List<Game>()); RefreshList();
            try { SaveLibrary(); } catch (Exception saveError) { scanDetails += "\n" + saveError.Message; }
        }
        finally { SetBusy(false); }
    }
    int RetireSpoofSlots()
    {
        int retired = 0;
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in games)
        {
            if (g == null || String.IsNullOrEmpty(g.Exe)) continue;
            string folder = null;
            try { folder = Path.GetDirectoryName(g.Exe); } catch { folder = null; }
            if (String.IsNullOrEmpty(folder) || !folders.Add(folder)) continue;
            try
            {
                var spoof = new Installer(folder, true);
                if (spoof.Status() == "적용됨") { spoof.Restore(); retired++; }
            }
            catch { }
        }
        return retired;
    }
    async void RefreshClick(object sender, RoutedEventArgs e) => await Scan();
    async void NvidiaClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(Discovery.NvidiaAppPath)) throw new IOException(L("NVIDIA App is not installed at the expected location.", "NVIDIA App 설치 경로를 찾지 못했습니다."));
            Process.Start(new ProcessStartInfo(Discovery.NvidiaAppPath) { UseShellExecute = true });
        }
        catch (Exception error) { await ShowError(error.Message); }
    }
    async void AddProgramClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".exe");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            string exe = Path.GetFullPath(file.Path);
            string folder = Path.GetDirectoryName(exe);
            if (String.IsNullOrEmpty(folder) || !Directory.Exists(folder)) throw new IOException(L("Choose an executable inside a valid game folder.", "유효한 게임 폴더 안의 실행 파일을 선택하세요."));
            string normRoot = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normRoot.Length <= 3) throw new IOException(L("Choose an executable inside a valid game folder.", "유효한 게임 폴더 안의 실행 파일을 선택하세요."));
            Disk.Safe(folder);
            if (!Discovery.IsX64(exe)) throw new IOException(L("Select an x64 game executable.", "x64 게임 실행 파일을 선택하세요."));
            var existing = games.FirstOrDefault(g => g != null && (String.Equals(g.Exe, exe, StringComparison.OrdinalIgnoreCase) || String.Equals(g.Root, folder, StringComparison.OrdinalIgnoreCase)));
            Search.Text = "";
            RefreshList();
            if (existing != null)
            {
                GameList.SelectedItem = GameList.Items.Cast<Game>().FirstOrDefault(g => g != null && (String.Equals(g.Exe, existing.Exe, StringComparison.OrdinalIgnoreCase) || String.Equals(g.Root, existing.Root, StringComparison.OrdinalIgnoreCase)));
                return;
            }
            string name = Path.GetFileNameWithoutExtension(exe);
            if (String.IsNullOrEmpty(name)) name = Path.GetFileName(folder);
            games.Add(new Game { Name = name, Root = folder, Exe = exe, Source = "수동 추가", Manual = true, CanEnable = true, Executables = Discovery.ManualExecutables(folder, exe), Evidence = "사용자가 직접 추가한 프로그램입니다." });
            SaveLibrary();
            RefreshList();
            GameList.SelectedItem = GameList.Items.Cast<Game>().FirstOrDefault(g => g != null && String.Equals(g.Exe, exe, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) { await ShowError(error.Message); }
    }
    async void DeleteProgramClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var game = Selected;
        if (game == null) { await ShowDialog(L("Nothing to delete", "삭제할 항목 없음"), L("Select a program from the list first.", "목록에서 프로그램을 먼저 선택하세요.")); return; }
        if (!game.Manual) { await ShowDialog(L("Cannot delete", "삭제할 수 없음"), L("Only manually added programs can be deleted. NVIDIA-detected programs reappear on refresh.", "직접 추가한 프로그램만 삭제할 수 있습니다. NVIDIA 감지 항목은 새로고침하면 다시 나타납니다.")); return; }
        string folder = null;
        try { if (!String.IsNullOrEmpty(game.Exe)) folder = Path.GetDirectoryName(Path.GetFullPath(game.Exe)); } catch { folder = null; }
        if (folder != null && Installer.HasAny(folder)) { await ShowDialog(L("Restore first", "먼저 복구"), L("This program has applied changes. Restore it first, then delete.", "적용 중인 항목입니다. 먼저 복구한 뒤 삭제하세요.")); return; }
        var confirm = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = L("Delete program", "프로그램 삭제"), Content = game.Name, PrimaryButtonText = L("Delete", "삭제"), CloseButtonText = L("Cancel", "취소"), DefaultButton = ContentDialogButton.Close };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        games.Remove(game);
        try { SaveLibrary(); }
        catch (Exception error) { await ShowError(error.Message); }
        RefreshList();
    }
    async void FolderClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { CurrentTarget().Target }, UseShellExecute = true }); }
        catch (Exception error) { await ShowError(error.Message); }
    }
    async Task Run(Action action)
    {
        if (busy) return;
        SetBusy(true);
        try { await Task.Run(action); }
        catch (Exception error) { await ShowError(error.Message); }
        finally { SetBusy(false); }
    }
    async void MfgToggled(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        if (busy) { UpdateState(); return; }
        try
        {
            bool enable = MfgSwitch.IsOn; var target = CurrentTarget(); var game = Selected;
            string proxy = (string)ProxyCombo.SelectedItem, exe = (string)ExeCombo.SelectedItem;
            if (!enable) ExcludeFromGlobal(game);
            await Run(() =>
            {
                if (enable)
                {
                    Discovery.ValidateForEnable(game, exe);
                    Payload.Ensure(proxy, ProgressText);
                    Discovery.ValidateForEnable(game, exe);
                    target.Enable(proxy, Payload.Cache);
                }
                else Installer.RestoreAll(target.Target);
            });
        }
        catch (Exception error) { UpdateState(); await ShowError(error.Message); }
    }
    async void RestoreClick(object sender, RoutedEventArgs e)
    {
        try { ExcludeFromGlobal(Selected); string target = CurrentTarget().Target; await Run(() => Installer.RestoreAll(target)); }
        catch (Exception error) { await ShowError(error.Message); }
    }
    async void AutoToggled(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        bool previous = settings.AutoUpdate;
        try { settings.AutoUpdate = AutoSwitch.IsOn; Disk.Save(SettingsFile, settings); }
        catch (Exception error) { settings.AutoUpdate = previous; loading = true; AutoSwitch.IsOn = previous; loading = false; await ShowError(error.Message); return; }
        await UpdateOnce();
    }
    async Task UpdateOnce(bool force = false)
    {
        if (busy || (!settings.AutoUpdate && !force) || (updateChecked && !force)) return;
        updateChecked = true; SetBusy(true);
        var known = games.ToList();
        try { await Task.Run(() => Updates.CheckAndApply(known, ProgressText)); }
        catch { }
        finally { SetBusy(false); }
    }
    async void ProxyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || busy || Selected == null || ProxyCombo.SelectedItem is not string proxy) return;
        try
        {
            var target = CurrentTarget();
            if (target.Status() != "적용됨") return;
            Journal journal = target.Load();
            if (journal == null || String.Equals(journal.Proxy, proxy, StringComparison.OrdinalIgnoreCase)) return;
            var game = Selected;
            string exe = (string)ExeCombo.SelectedItem;
            await Run(() =>
            {
                Installer.RestoreAll(target.Target);
                Discovery.ValidateForEnable(game, exe);
                Payload.Ensure(proxy, ProgressText);
                Discovery.ValidateForEnable(game, exe);
                target.Enable(proxy, Payload.Cache);
            });
        }
        catch (Exception error) { UpdateState(); await ShowError(error.Message); }
    }
    async void ProxyDetectClick(object sender, RoutedEventArgs e)
    {
        if (loading || busy || Selected == null) return;
        try
        {
            var game = Selected;
            var target = CurrentTarget();
            string owned = null;
            Journal journal = target.Load();
            if (journal != null && journal.Phase == "enabled") owned = journal.Proxy;
            ProxyRecommendationRecord recommendation = null;
            await Run(() => recommendation = ProxyRecommendations.DetectAndStore(game, true, owned));
            if (!String.IsNullOrEmpty(recommendation?.Proxy))
            {
                if (target.Status() == "미적용")
                {
                    bool previousLoading = loading; loading = true;
                    ProxyCombo.SelectedItem = recommendation.Proxy;
                    loading = previousLoading;
                }
                StateText.Text = L("Recommended proxy: ", "추천 프록시: ") + recommendation.Proxy;
            }
            else StateText.Text = recommendation?.Result?.StartsWith("ambiguous:") == true
                ? L("Proxy recommendation is ambiguous: ", "추천 프록시가 동률입니다: ") + recommendation.Result.Substring("ambiguous:".Length)
                : L("No suitable proxy DLL was detected.", "적합한 프록시 DLL을 찾지 못했습니다.");
        }
        catch (Exception error) { UpdateState(); await ShowError(error.Message); }
    }
    async void RuntimeNotesClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        RuntimeNotesButton.IsEnabled = false;
        try
        {
            var notes = await Task.Run(Updates.FetchReleaseNotes);
            await ShowDialog("dlssg_for_sm86 " + notes.Version, notes.EnglishText + "\n\n" + notes.Url);
        }
        catch (Exception error) { await ShowError(error.Message); }
        finally { RuntimeNotesButton.IsEnabled = !busy; }
    }
    bool dialogOpen;
    async Task ShowDialog(string title, string text)
    {
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = title,
                CloseButtonText = L("Close", "닫기"), Content = new ScrollViewer { MaxHeight = 420,
                HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock { Text = text, Margin = new Thickness(0, 0, 20, 0), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } } };
            await dialog.ShowAsync();
        }
        finally { dialogOpen = false; ResumeStorageScan(); }
    }
    Task ShowError(string error) => ShowDialog(L("Unable to complete the operation", "작업을 완료하지 못했습니다"), LocalizeCore(error));
    async void DetailsClick(object sender, RoutedEventArgs e) => await ShowDialog(L("Detection details", "감지 상세"), LocalizeCore(scanDetails));
    async Task ShowWhatsNewOnce()
    {
        string version = AppUpdates.CurrentVersion;
        if (string.Equals(settings.SeenWhatsNewVersion, version, StringComparison.OrdinalIgnoreCase)) return;

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Title = L("What's new · ", "새 기능 · ") + version,
            CloseButtonText = "OK",
            Content = new TextBlock
            {
                Text = L("• MFG override: automatic runtime DLL installation for detected DX12 frame-generation games\n• Automatic DLL selection: finds the best-matching DLL for each game and uses it to apply MFG\n• NVIDIA Profile Inspector-style FG controls: global / per-game presets, fixed / dynamic multipliers and target FPS, completed in 1.4\n• Background tray: checks new games when NVIDIA App updates its library\n• Compact controls and corrected dialog scrollbar spacing",
                    "• MFG 오버라이드: 감지된 DX12 프레임 생성 게임에 런타임 DLL 자동 설치\n• 게임에 맞는 DLL 자동 선택: 가장 적절한 DLL 파일을 자동으로 찾아 MFG 적용에 사용\n• NVIDIA 인스펙터 방식 FG 설정: 전역·게임별 프리셋, 고정·동적 배수, 목표 FPS 설정을 1.4에서 완성\n• 백그라운드 트레이: NVIDIA App 목록 갱신 시 새 게임 확인\n• 버튼 크기 정리 및 설정창 스크롤바 겹침 수정"),
                TextWrapping = TextWrapping.Wrap
            }
        };
        dialogOpen = true;
        try { await dialog.ShowAsync(); }
        finally { dialogOpen = false; ResumeStorageScan(); }
        settings.SeenWhatsNewVersion = version;
        try { Disk.Save(SettingsFile, settings); } catch { }
    }
}


