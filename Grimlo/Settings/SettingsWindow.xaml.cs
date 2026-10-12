using System;
using System.Diagnostics;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using Grimlo.Chat;
using Grimlo.Services;

namespace Grimlo.Settings;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly IHttpClientFactory _httpFactory;
    private SpeechSynthesizer? _testVoiceSynthesizer;
    private bool _isInitializing = true;

    // Design-time constructor
    public SettingsWindow() : this(null!, null!)
    {
    }

    public SettingsWindow(ISettingsService settingsService, IHttpClientFactory httpFactory)
    {
        _settingsService = settingsService;
        _httpFactory = httpFactory;

        InitializeComponent();

        Loaded += (_, _) => InitializeUi();
        Closed += (_, _) =>
        {
            try
            {
                _testVoiceSynthesizer?.SpeakAsyncCancelAll();
                _testVoiceSynthesizer?.Dispose();
            }
            catch { }
        };
    }

    private void InitializeUi()
    {
        if (_settingsService is null) return;

        string active = _settingsService.Settings.ActiveProvider?.ToLowerInvariant() ?? "gemini";
        foreach (ComboBoxItem item in ProviderCombo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), active, StringComparison.OrdinalIgnoreCase))
            {
                ProviderCombo.SelectedItem = item;
                break;
            }
        }

        if (ProviderCombo.SelectedItem is null && ProviderCombo.Items.Count > 0)
        {
            ProviderCombo.SelectedIndex = 0;
        }

        BlusterSlider.Value = _settingsService.Settings.BlusterMelancholyLevel;
        NormalcySlider.Value = _settingsService.Settings.NormalcyChaosLevel;
        CustomMicroInterestsBox.Text = _settingsService.Settings.CustomMicroInterests ?? string.Empty;
        CustomInstructionsBox.Text = _settingsService.Settings.CustomInstructions ?? string.Empty;
        UpdatePersonalityUi();

        // Populate installed Windows voices
        VoiceCombo.Items.Clear();
        string currentVoice = _settingsService.Settings.VoiceName ?? string.Empty;
        try
        {
            using var synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices();
            foreach (var v in voices)
            {
                if (v.Enabled)
                {
                    VoiceCombo.Items.Add(v.VoiceInfo.Name);
                }
            }
        }
        catch { }

        if (VoiceCombo.Items.Count > 0)
        {
            if (!string.IsNullOrEmpty(currentVoice) && VoiceCombo.Items.Contains(currentVoice))
            {
                VoiceCombo.SelectedItem = currentVoice;
            }
            else
            {
                VoiceCombo.SelectedIndex = 0;
            }
        }

        VoiceRateSlider.Value = Math.Clamp(_settingsService.Settings.VoiceRate, -10, 10);
        UpdateVoiceRateLabel();

        VoiceVolumeSlider.Value = Math.Clamp(_settingsService.Settings.VoiceVolume, 0, 100);
        UpdateVoiceVolumeLabel();

        // Retro Sound Effects (SFX)
        EnableSfxCheck.IsChecked = _settingsService.Settings.EnableSoundEffects;
        SfxVolumeSlider.Value = Math.Clamp(_settingsService.Settings.SfxVolume, 0, 100);
        UpdateSfxVolumeLabel();

        IdleChatterCheck.IsChecked = _settingsService.Settings.EnableIdleChatter;
        IdleIntervalSlider.Value = Math.Clamp(_settingsService.Settings.IdleChatterIntervalMinutes, 2, 20);
        UpdateIdleIntervalLabel();

        // Sleep & Drowse Cycles
        EnableSleepCheck.IsChecked = _settingsService.Settings.EnableSleepCycles;
        SleepIntervalSlider.Value = Math.Clamp(_settingsService.Settings.SleepInactivityMinutes, 3, 20);
        UpdateSleepIntervalLabel();

        // Check Windows Startup status directly from Registry / Settings
        StartWithWindowsCheck.IsChecked = StartupService.IsRunOnStartupEnabled();

        // Ambient Window Reactions
        WindowReactionsCheck.IsChecked = _settingsService.Settings.EnableWindowReactions;
        WindowIntervalSlider.Value = Math.Clamp(_settingsService.Settings.WindowReactionIntervalMinutes, 1, 15);
        UpdateWindowIntervalLabel();

        // Desktop Wander / Screen Edge Pacing
        DesktopWanderCheck.IsChecked = _settingsService.Settings.EnableDesktopWander;
        WanderIntervalSlider.Value = Math.Clamp(_settingsService.Settings.WanderIntervalMinutes, 5, 30);
        UpdateWanderIntervalLabel();

        _isInitializing = false;
        UpdateFormForSelectedProvider();
    }

    private string GetSelectedProviderTag()
    {
        if (ProviderCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag.ToLowerInvariant();
        }
        return "gemini";
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateFormForSelectedProvider();
    }

    private void UpdateFormForSelectedProvider()
    {
        string provider = GetSelectedProviderTag();
        ModelCombo.Items.Clear();

        switch (provider)
        {
            case "claude":
                ModelCombo.Items.Add("claude-sonnet-4-5");
                ModelCombo.Items.Add("claude-haiku-4-5");
                ModelCombo.Text = _settingsService.Settings.ClaudeModel ?? "claude-sonnet-4-5";
                break;

            case "openai":
                ModelCombo.Items.Add("gpt-4o-mini");
                ModelCombo.Items.Add("gpt-4o");
                ModelCombo.Text = _settingsService.Settings.OpenAiModel ?? "gpt-4o-mini";
                break;

            default: // gemini
                ModelCombo.Items.Add("gemini-flash-lite-latest");
                ModelCombo.Items.Add("gemini-3.8-flash");
                ModelCombo.Text = _settingsService.Settings.GeminiModel ?? "gemini-flash-lite-latest";
                break;
        }

        // Update API Key status indicator
        bool hasKey = _settingsService.HasApiKey(provider);
        if (hasKey)
        {
            KeyStatusLabel.Text = " (Key is securely saved)";
            KeyStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x81, 0xC7, 0x84)); // soft green
        }
        else
        {
            KeyStatusLabel.Text = " (No key saved)";
            KeyStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D)); // soft orange
        }

        ApiKeyBox.Password = string.Empty;
        TestStatusText.Text = string.Empty;
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            KeyStatusLabel.Text = " (New key entered)";
            KeyStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0xB5, 0xF6)); // soft blue
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        string provider = GetSelectedProviderTag();
        string? keyToTest = !string.IsNullOrWhiteSpace(ApiKeyBox.Password)
            ? ApiKeyBox.Password.Trim()
            : _settingsService.GetApiKey(provider);

        if (string.IsNullOrWhiteSpace(keyToTest))
        {
            TestStatusText.Text = "Please enter an API key to test.";
            TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73));
            return;
        }

        TestButton.IsEnabled = false;
        TestStatusText.Text = "Pinging provider...";
        TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));

        try
        {
            HttpClient http = _httpFactory.CreateClient("GrimloChat");
            string model = ModelCombo.Text.Trim();

            IChatProvider chatProvider = provider switch
            {
                "claude" => new ClaudeProvider(http, model),
                "openai" => new OpenAiProvider(http, model),
                _ => new GeminiProvider(http, model),
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string reply = await chatProvider.GetReplyAsync("Hello!", keyToTest, cts.Token);

            TestStatusText.Text = "Connected successfully!";
            TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x81, 0xC7, 0x84));
        }
        catch (Exception ex)
        {
            TestStatusText.Text = ex.Message;
            TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73));
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void PersonalityControl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        UpdatePersonalityUi();
    }

    private void PersonalityControl_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdatePersonalityUi();
    }

    private void UpdatePersonalityUi()
    {
        if (BlusterDescLabel is null || NormalcyDescLabel is null || LivePromptPreviewBox is null) return;

        int bluster = (int)BlusterSlider.Value;
        int normalcy = (int)NormalcySlider.Value;

        BlusterDescLabel.Text = GrimloPersonality.GetBlusterDescription(bluster);
        NormalcyDescLabel.Text = GrimloPersonality.GetNormalcyDescription(normalcy);

        string micro = CustomMicroInterestsBox.Text;
        string directives = CustomInstructionsBox.Text;

        LivePromptPreviewBox.Text = GrimloPersonality.BuildSystemPrompt(bluster, normalcy, micro, directives);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string provider = GetSelectedProviderTag();
        _settingsService.Settings.ActiveProvider = provider;

        string model = ModelCombo.Text.Trim();
        switch (provider)
        {
            case "claude":
                _settingsService.Settings.ClaudeModel = model;
                break;
            case "openai":
                _settingsService.Settings.OpenAiModel = model;
                break;
            default:
                _settingsService.Settings.GeminiModel = model;
                break;
        }

        if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            _settingsService.SetApiKey(provider, ApiKeyBox.Password.Trim());
        }

        // Save Master Control personality settings
        _settingsService.Settings.BlusterMelancholyLevel = (int)BlusterSlider.Value;
        _settingsService.Settings.NormalcyChaosLevel = (int)NormalcySlider.Value;
        _settingsService.Settings.CustomMicroInterests = CustomMicroInterestsBox.Text.Trim();
        _settingsService.Settings.CustomInstructions = CustomInstructionsBox.Text.Trim();

        // Save Idle Chatter settings
        _settingsService.Settings.EnableIdleChatter = IdleChatterCheck.IsChecked ?? true;
        _settingsService.Settings.IdleChatterIntervalMinutes = (int)IdleIntervalSlider.Value;

        // Save Sleep & Drowse settings
        _settingsService.Settings.EnableSleepCycles = EnableSleepCheck.IsChecked ?? true;
        _settingsService.Settings.SleepInactivityMinutes = (int)SleepIntervalSlider.Value;

        // Save Voice settings
        if (VoiceCombo.SelectedItem is string selectedVoice)
        {
            _settingsService.Settings.VoiceName = selectedVoice;
        }
        _settingsService.Settings.VoiceRate = (int)VoiceRateSlider.Value;
        _settingsService.Settings.VoiceVolume = (int)VoiceVolumeSlider.Value;

        // Save Sound Effects settings
        _settingsService.Settings.EnableSoundEffects = EnableSfxCheck.IsChecked ?? true;
        _settingsService.Settings.SfxVolume = (int)SfxVolumeSlider.Value;
        SoundEffectService.IsEnabled = _settingsService.Settings.EnableSoundEffects;
        SoundEffectService.Volume = _settingsService.Settings.SfxVolume / 100.0;

        // Save Windows Startup settings
        bool startWithWindows = StartWithWindowsCheck.IsChecked ?? false;
        _settingsService.Settings.StartWithWindows = startWithWindows;
        StartupService.SetRunOnStartup(startWithWindows);

        // Save Ambient Window Reaction settings
        _settingsService.Settings.EnableWindowReactions = WindowReactionsCheck.IsChecked ?? true;
        _settingsService.Settings.WindowReactionIntervalMinutes = (int)WindowIntervalSlider.Value;

        // Save Desktop Wander settings
        _settingsService.Settings.EnableDesktopWander = DesktopWanderCheck.IsChecked ?? true;
        _settingsService.Settings.WanderIntervalMinutes = (int)WanderIntervalSlider.Value;

        _settingsService.Save();
        Close();
    }

    private void EnableSfxCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        SoundEffectService.IsEnabled = EnableSfxCheck.IsChecked ?? true;
    }

    private void SfxVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        UpdateSfxVolumeLabel();
        SoundEffectService.Volume = SfxVolumeSlider.Value / 100.0;
    }

    private void UpdateSfxVolumeLabel()
    {
        if (SfxVolumeLabel is null || SfxVolumeSlider is null) return;
        int vol = (int)SfxVolumeSlider.Value;
        SfxVolumeLabel.Text = $"{vol}%";
    }

    private void TestSfxTapButton_Click(object sender, RoutedEventArgs e)
    {
        SoundEffectService.Volume = SfxVolumeSlider.Value / 100.0;
        SoundEffectService.IsEnabled = true;
        SoundEffectService.Play("tap_glass");
    }

    private void TestSfxPopButton_Click(object sender, RoutedEventArgs e)
    {
        SoundEffectService.Volume = SfxVolumeSlider.Value / 100.0;
        SoundEffectService.IsEnabled = true;
        SoundEffectService.Play("bubble_pop");
    }

    private void TestSfxDizzyButton_Click(object sender, RoutedEventArgs e)
    {
        SoundEffectService.Volume = SfxVolumeSlider.Value / 100.0;
        SoundEffectService.IsEnabled = true;
        SoundEffectService.Play("shake_dizzy");
    }

    private void VoiceRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        UpdateVoiceRateLabel();
    }

    private void UpdateVoiceRateLabel()
    {
        if (VoiceRateLabel is null || VoiceRateSlider is null) return;
        int rate = (int)VoiceRateSlider.Value;
        VoiceRateLabel.Text = rate switch
        {
            0 => "Normal (0)",
            > 0 => $"Fast (+{rate})",
            < 0 => $"Slow ({rate})",
        };
    }

    private void VoiceVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        UpdateVoiceVolumeLabel();
    }

    private void UpdateVoiceVolumeLabel()
    {
        if (VoiceVolumeLabel is null || VoiceVolumeSlider is null) return;
        int vol = (int)VoiceVolumeSlider.Value;
        VoiceVolumeLabel.Text = $"{vol}%";
    }

    private void TestVoiceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _testVoiceSynthesizer?.SpeakAsyncCancelAll();
            _testVoiceSynthesizer?.Dispose();
            _testVoiceSynthesizer = new SpeechSynthesizer();

            if (VoiceCombo.SelectedItem is string voiceName && !string.IsNullOrWhiteSpace(voiceName))
            {
                _testVoiceSynthesizer.SelectVoice(voiceName);
            }

            _testVoiceSynthesizer.Rate = (int)VoiceRateSlider.Value;
            _testVoiceSynthesizer.Volume = (int)VoiceVolumeSlider.Value;

            string[] testQuotes =
            {
                "I am Grimlo, the greatest digital gremlin ever trapped on your screen!",
                "Testing, testing! Can you hear the exquisite nuance of my voice?!",
                "Behold my majestic voice! Much better than anything from that ridiculous gremlins movie.",
                "Ah, testing my vocal cords. Pure sonic perfection, just like Spandau Ballet.",
            };

            string quote = testQuotes[Random.Shared.Next(testQuotes.Length)];
            _testVoiceSynthesizer.SpeakAsync(quote);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not play voice test: {ex.Message}", "Voice Test", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void IdleIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        UpdateIdleIntervalLabel();
    }

    private void UpdateIdleIntervalLabel()
    {
        if (IdleIntervalLabel is null || IdleIntervalSlider is null) return;
        int mins = (int)IdleIntervalSlider.Value;
        IdleIntervalLabel.Text = $"Every {mins} minute{(mins == 1 ? "" : "s")}";
    }

    private void SleepIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        UpdateSleepIntervalLabel();
    }

    private void UpdateSleepIntervalLabel()
    {
        if (SleepIntervalLabel is null || SleepIntervalSlider is null) return;
        int mins = (int)SleepIntervalSlider.Value;
        SleepIntervalLabel.Text = $"After {mins} minute{(mins == 1 ? "" : "s")}";
    }

    private void WindowIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        UpdateWindowIntervalLabel();
    }

    private void UpdateWindowIntervalLabel()
    {
        if (WindowIntervalLabel is null || WindowIntervalSlider is null) return;
        int mins = (int)WindowIntervalSlider.Value;
        WindowIntervalLabel.Text = $"Every {mins} minute{(mins == 1 ? "" : "s")}";
    }

    private void WanderIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        UpdateWanderIntervalLabel();
    }

    private void UpdateWanderIntervalLabel()
    {
        if (WanderIntervalLabel is null || WanderIntervalSlider is null) return;
        int mins = (int)WanderIntervalSlider.Value;
        WanderIntervalLabel.Text = $"Every {mins} minute{(mins == 1 ? "" : "s")}";
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { }
        e.Handled = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
