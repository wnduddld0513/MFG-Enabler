using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MfgEnabler;

public sealed partial class MainWindow
{
    AppRelease appRelease;
    string appUpdateState = "idle";
    void ApplyAppUpdateLanguage()
    {
        AppUpdateLabel.Text = L("MFG-Enabler updates", "MFG-Enabler 업데이트");
        AppUpdateButton.Content = appRelease == null ? L("Check for updates", "업데이트 확인") : L("Update", "업데이트");
        AppUpdateStatus.Text = appUpdateState switch
        {
            "checking" => L("Checking for updates…", "업데이트 확인 중…"),
            "installing" => L("Downloading and preparing the update. The app will restart…", "업데이트 다운로드 및 준비 중입니다. 완료되면 앱이 재시작됩니다…"),
            "failed" => L("Could not check for updates. Try again.", "업데이트를 확인하지 못했습니다. 다시 시도하세요."),
            "current" => L("No newer package is available for this channel.", "이 채널에 더 새로운 패키지가 없습니다."),
            _ => appRelease == null ? L("Checks the selected channel once at startup.", "실행 시 선택한 채널을 한 번 확인합니다.") : L("New version available: ", "새 버전 사용 가능: ") + appRelease.Version
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AppChannelCombo, L("Update channel", "업데이트 채널"));
    }
    async void AppChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || busy) return;
        string previous = settings.AppChannel;
        settings.AppChannel = AppChannelCombo.SelectedIndex == 1 ? "beta" : "main";
        try { Disk.Save(SettingsFile, settings); }
        catch (Exception error)
        {
            settings.AppChannel = previous; loading = true;
            AppChannelCombo.SelectedIndex = previous == "beta" ? 1 : 0; loading = false;
            await ShowError(error.Message); return;
        }
        appRelease = null;
        await CheckAppUpdate(true);
    }
    async void AppUpdateClick(object sender, RoutedEventArgs e)
    {
        if (busy || dialogOpen) return;
        if (appRelease == null) await CheckAppUpdate(true);
        else await OfferAppUpdate();
    }
    async Task CheckAppUpdate(bool manual)
    {
        if (busy) return;
        SetBusy(true); appUpdateState = "checking"; ApplyAppUpdateLanguage();
        try
        {
            appRelease = await AppUpdates.Check(settings.AppChannel);
            appUpdateState = appRelease == null ? "current" : "available";
        }
        catch (Exception error)
        {
            appRelease = null; appUpdateState = "failed";
            if (manual) await ShowError(L("Application update check failed.\n", "앱 업데이트 확인 실패.\n") + error.Message);
        }
        finally { SetBusy(false); ApplyAppUpdateLanguage(); }
        string dismissed = settings.AppChannel == "beta" ? settings.DismissedBetaVersion : settings.DismissedStableVersion;
        if (appRelease != null && (manual || dismissed != appRelease.Version)) await OfferAppUpdate();
    }
    async Task OfferAppUpdate()
    {
        if (dialogOpen || busy || appRelease == null) return;
        var release = appRelease;
        dialogOpen = true;
        ContentDialogResult choice;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = ElementTheme.Dark,
                Title = L("New update available", "새 업데이트가 있습니다"),
                Content = "MFG-Enabler " + release.Version + " · " + (ReleaseNumber.Parse(release.Version)?.Beta != null ? "Beta" : "Stable") + "\n\n" + L("Download and apply the update to this application folder, then restart.", "현재 앱 폴더에 업데이트를 다운로드하고 적용한 뒤 재시작합니다."),
                PrimaryButtonText = L("Update", "업데이트"),
                SecondaryButtonText = L("Don't show again", "다시 표시하지 않기"),
                CloseButtonText = L("Later", "나중에"), DefaultButton = ContentDialogButton.Close
            };
            choice = await dialog.ShowAsync();
        }
        finally { dialogOpen = false; ResumeStorageScan(); }
        if (choice == ContentDialogResult.Secondary)
        {
            string previous = release.Channel == "beta" ? settings.DismissedBetaVersion : settings.DismissedStableVersion;
            try
            {
                if (release.Channel == "beta") settings.DismissedBetaVersion = release.Version;
                else settings.DismissedStableVersion = release.Version;
                Disk.Save(SettingsFile, settings);
            }
            catch (Exception error)
            {
                if (release.Channel == "beta") settings.DismissedBetaVersion = previous; else settings.DismissedStableVersion = previous;
                await ShowError(error.Message);
            }
        }
        if (choice != ContentDialogResult.Primary) return;
        SetBusy(true); appUpdateState = "installing"; ApplyAppUpdateLanguage();
        try
        {
            string job = await AppUpdates.Prepare(release);
            await AppUpdates.StartHelper(job);
            File.WriteAllText(Path.Combine(job, "commit"), "apply");
            SetBusy(false);
            Close();
        }
        catch (Exception error)
        {
            appUpdateState = "available";
            await ShowError(L("Application update failed. The current installation is unchanged.\n", "앱 업데이트에 실패했습니다. 현재 설치는 변경되지 않았습니다.\n") + error.Message);
        }
        finally { SetBusy(false); ApplyAppUpdateLanguage(); }
    }
}
