using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace MfgEnabler;

public sealed partial class MainWindow
{
	private void ApplyIniLanguage()
	{
		IniTitle.Text = L("INI settings", "INI 설정");
		IniOptimizedLabel.Text = L("Optimization tier", "최적화 단계");
		IniFramesLabel.Text = L("Frame-generation ceiling", "프레임 생성 최대 배율");
		IniPresetLabel.Text = L("Render preset", "렌더 프리셋");
		IniLoggingLabel.Text = L("Log level", "로그 수준");
		IniAdvanced.Header = L("Additional settings", "추가 설정");
		IniOptimizedCombo.ItemsSource = new string[4]
		{
			L("0 — Stock", "0 — 원본 처리"),
			L("1 — Exact image (default)", "1 — 원본과 동일한 화질 (기본값)"),
			L("2 — Faster, lossy", "2 — 빠름, 화질 손실"),
			L("3 — Fastest, lossy", "3 — 가장 빠름, 화질 손실")
		};
		IniFramesCombo.ItemsSource = new string[6]
		{
			L("0 — Runtime limit", "0 — 런타임 한도"),
			"1 — 2X",
			"2 — 3X",
			L("3 — 4X (default)", "3 — 4X (기본값)"),
			"4 — 5X",
			"5 — 6X"
		};
		IniPresetCombo.ItemsSource = new string[3]
		{
			L("Auto (default)", "Auto (기본값)"),
			L("A — UI recomposition off", "A — UI 재합성 끄기"),
			L("B — UI recomposition on", "B — UI 재합성 켜기")
		};
		IniLoggingCombo.ItemsSource = new string[4]
		{
			L("0 — Off", "0 — 끄기"),
			L("1 — Errors (default)", "1 — 오류만 (기본값)"),
			L("2 — Configuration", "2 — 구성 정보"),
			L("3 — Detailed", "3 — 상세 진단")
		};
		IniQualityHint.Text = L("Tier 1 keeps the original image. Tiers 2 and 3 trade image quality for speed.", "1단계는 원본 화질을 유지합니다. 2·3단계는 속도를 높이는 대신 화질 손실이 있습니다.");
		IniFramesHint.Text = L("This is a ceiling, not a forced multiplier. 6X requires a compatible game; older 4X plugins remain limited to 4X.", "강제 배율이 아닌 최대 한도입니다. 6X는 게임의 지원이 필요하며, 구형 4X 플러그인은 4X로 제한됩니다.");
		IniPresetHint.Text = L("Preset B only affects games that provide the required HUD/UI data.", "프리셋 B는 필요한 HUD/UI 데이터를 제공하는 게임에서만 효과가 있습니다.");
		IniHelp.Text = L("Close the game before saving. Changes apply to this installed game on its next launch. Runtime upgrades reset the INI to factory values.", "게임을 종료한 뒤 저장하세요. 이 게임을 다음에 실행할 때 적용됩니다. 런타임을 업데이트하면 INI가 기본값으로 초기화됩니다.");
		IniSaveButton.Content = L("Save settings", "설정 저장");
		IniDefaultsButton.Content = L("Reset shown settings", "표시된 설정 기본값");
		(ComboBox, string)[] array = new(ComboBox, string)[4]
		{
			(IniOptimizedCombo, IniOptimizedLabel.Text),
			(IniFramesCombo, IniFramesLabel.Text),
			(IniPresetCombo, IniPresetLabel.Text),
			(IniLoggingCombo, IniLoggingLabel.Text)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(ComboBox, string) tuple = array[i];
			AutomationProperties.SetName(tuple.Item1, tuple.Item2);
		}
	}

	private void FillIniControls(RuntimeIniSettings value)
	{
		IniOptimizedCombo.SelectedIndex = value.Optimized;
		IniFramesCombo.SelectedIndex = value.MaxGeneratedFrames;
		IniPresetCombo.SelectedIndex = ((!(value.Preset == "Auto")) ? ((value.Preset == "A") ? 1 : 2) : 0);
		IniLoggingCombo.SelectedIndex = value.LogLevel;
	}

	private void EnableIniControls(bool enabled)
	{
		ComboBox iniOptimizedCombo = IniOptimizedCombo;
		ComboBox iniFramesCombo = IniFramesCombo;
		ComboBox iniPresetCombo = IniPresetCombo;
		ComboBox iniLoggingCombo = IniLoggingCombo;
		bool flag = (IniSaveButton.IsEnabled = enabled);
		bool flag3 = (iniLoggingCombo.IsEnabled = flag);
		bool flag5 = (iniPresetCombo.IsEnabled = flag3);
		bool isEnabled = (iniFramesCombo.IsEnabled = flag5);
		iniOptimizedCombo.IsEnabled = isEnabled;
	}

	private void RefreshIniEditor()
	{
		if (IniTitle == null)
		{
			return;
		}
		EnableIniControls(enabled: false);
		IniDefaultsButton.IsEnabled = false;
		if (busy)
		{
			return;
		}
		FillIniControls(new RuntimeIniSettings());
		IniState.Text = "";
		if (Selected == null || ExeCombo.SelectedItem == null)
		{
			return;
		}
		try
		{
			Installer installer = CurrentTarget();
			if (installer.CanEditIni())
			{
				IniDefaultsButton.IsEnabled = true;
				FillIniControls(installer.ReadIniSettings());
				EnableIniControls(enabled: true);
				IniState.Text = L("Editing this game's dlssg_sm86.ini. Save to apply your changes.", "이 게임의 dlssg_sm86.ini를 편집합니다. 변경 후 저장 버튼을 누르세요.");
			}
		}
		catch (Exception ex)
		{
			IniState.Text = L("Could not read settings: ", "설정을 읽지 못했습니다: ") + ex.Message;
		}
	}

	private void IniDefaultsClick(object sender, RoutedEventArgs e)
	{
		if (!busy)
		{
			FillIniControls(new RuntimeIniSettings());
			EnableIniControls(enabled: true);
			IniState.Text = L("Defaults selected. Click Save settings to apply them.", "기본값을 선택했습니다. 설정 저장을 눌러 적용하세요.");
		}
	}

	private async void IniSaveClick(object sender, RoutedEventArgs e)
	{
		if (busy)
		{
			return;
		}
		try
		{
			Installer target = CurrentTarget();
			RuntimeIniSettings values = new RuntimeIniSettings
			{
				Optimized = IniOptimizedCombo.SelectedIndex,
				MaxGeneratedFrames = IniFramesCombo.SelectedIndex,
				Preset = ((IniPresetCombo.SelectedIndex == 0) ? "Auto" : ((IniPresetCombo.SelectedIndex == 1) ? "A" : ((IniPresetCombo.SelectedIndex == 2) ? "B" : ""))),
				LogLevel = IniLoggingCombo.SelectedIndex
			};
			SetBusy(value: true);
			try
			{
				await Task.Run(delegate
				{
					target.SaveIniSettings(values);
				});
			}
			finally
			{
				SetBusy(value: false);
			}
			IniState.Text = L("Saved. Settings apply the next time this game starts.", "저장했습니다. 다음 게임 실행부터 적용됩니다.");
		}
		catch (Exception ex)
		{
			await ShowError(ex.Message);
		}
	}

}
