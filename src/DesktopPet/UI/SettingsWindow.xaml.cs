using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPet.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
// UseWindowsForms adds a global `using System.Windows.Forms`, whose ComboBox collides with WPF's.
using ComboBox = System.Windows.Controls.ComboBox;

namespace DesktopPet.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings     _settings;
    private readonly SettingsService _service;
    private readonly Action?         _onChanged;
    private readonly Action?         _onForgetMemory;
    private readonly Func<CancellationToken, Task<string>>? _onTestAi;
    private readonly Func<Task<string>>? _onCheckUpdate;
    private readonly Action?         _onResetPosition;
    private readonly Action<string, string>? _onTriggerDevEvent;
    private readonly string _initialPet;
    private bool _loaded;

    public SettingsWindow(AppSettings settings, SettingsService service,
                          Action? onChanged = null, Action? onForgetMemory = null,
                          Func<CancellationToken, Task<string>>? onTestAi = null,
                          Func<Task<string>>? onCheckUpdate = null,
                          Action? onResetPosition = null,
                          Action<string, string>? onTriggerDevEvent = null)
    {
        InitializeComponent();
        _settings          = settings;
        _service           = service;
        _onChanged         = onChanged;
        _onForgetMemory    = onForgetMemory;
        _onTestAi          = onTestAi;
        _onCheckUpdate     = onCheckUpdate;
        _onResetPosition   = onResetPosition;
        _onTriggerDevEvent = onTriggerDevEvent;
        _initialPet        = settings.ActivePet;

        // Appearance
        SpeedSlider.Value            = settings.Speed;
        OpacitySlider.Value          = settings.Opacity;

        // Behavior
        AutonomousWanderBox.IsChecked= settings.EnableAutonomousWandering;
        WindowWalkingBox.IsChecked   = settings.EnableWindowWalking;
        CursorChaseBox.IsChecked     = settings.EnableCursorChase;
        RandomReactionsBox.IsChecked = settings.EnableRandomReactions;
        SleepBox.IsChecked           = settings.EnableSleep;
        ScrollPlayBox.IsChecked      = settings.EnableScrollPlay;
        MoodsBox.IsChecked           = settings.EnableMoods;
        SystemReactionsBox.IsChecked = settings.EnableSystemReactions;

        // Performance
        ReducedMotionBox.IsChecked     = settings.ReducedMotion;
        BatteryConsciousBox.IsChecked  = settings.BatteryConscious;
        DisableMonitoringBox.IsChecked = settings.DisableOptionalMonitoring;
        QuietModeBox.IsChecked         = settings.EnableQuietMode;
        HidePresentBox.IsChecked       = settings.HideWhenPresenting;
        SoftwareRenderingBox.IsChecked = settings.UseSoftwareRendering;

        // Developer
        DevEventsBox.IsChecked = settings.EnableDeveloperEvents;
        DevPortBox.Text        = settings.DeveloperPort.ToString();
        DevPanel.Visibility    = settings.EnableDeveloperEvents ? Visibility.Visible : Visibility.Collapsed;

        // System
        AutostartBox.IsChecked     = AutostartService.IsEnabled();
        ClickThroughBox.IsChecked  = settings.ClickThrough;
        AutoUpdateBox.IsChecked    = settings.EnableAutoUpdate;
        RememberStateBox.IsChecked = settings.RememberPetState;

        // AI
        AiEnableBox.IsChecked    = settings.EnableAiCompanion;
        AiToolsBox.IsChecked     = settings.AiEnableTools;
        AiChatterBox.IsChecked   = settings.AiProactiveChatter;
        AiStreamingBox.IsChecked = settings.AiStreaming;
        AiHotkeyBox.IsChecked    = settings.AiHotkeyEnabled;
        HotkeyCaptureBox.Text    = FormatHotkey((ModifierKeys)settings.AiHotkeyModifiers, (Key)settings.AiHotkeyKey);
        AiMemoryBox.IsChecked    = settings.AiEnableMemory;
        AiKeyBox.Password        = settings.AiApiKey;
        AiModelBox.Text          = settings.AiModel;
        OllamaUrlBox.Text        = settings.OllamaUrl;
        OllamaModelBox.Text      = settings.OllamaModel;
        AiPersonaBox.Text        = settings.AiPersona;

        VersionText.Text = $"Neku {AppVersion.Display}  •  settings in %AppData%\\Neku";

        SelectByTag(StretchIntervalBox, settings.StretchIntervalMinutes.ToString(), fallbackIndex: 0);
        SelectByTag(AiProviderBox, settings.AiProvider, fallbackIndex: 0);
        SelectByTag(FocusDurationBox, settings.FocusDurationMinutes.ToString(), fallbackIndex: 2);
        SelectByTag(BreakDurationBox, settings.BreakDurationMinutes.ToString(), fallbackIndex: 1);


        PopulateCompanionPacks(settings.ActivePet);

        // Size is a double, so match on closeness rather than exact text.
        foreach (ComboBoxItem item in SizeBox.Items)
        {
            if (double.TryParse(item.Tag?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double s)
                && Math.Abs(s - settings.SizeScale) < 0.05)
            {
                SizeBox.SelectedItem = item;
                break;
            }
        }
        if (SizeBox.SelectedItem == null)
        {
            var customItem = new ComboBoxItem
            {
                Content = $"Custom ({Math.Round(settings.SizeScale * 100)}%)",
                Tag = settings.SizeScale.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            SizeBox.Items.Add(customItem);
            SizeBox.SelectedItem = customItem;
        }

        UpdateProviderPanels();

        _loaded = true;
        WireUp();
    }

    /// <summary>Hook every control to <see cref="Apply"/>. Done after load so setting the
    /// initial values doesn't trigger a save-and-reapply storm on open.</summary>
    private void WireUp()
    {
        SpeedSlider.ValueChanged   += (_, _) => Apply();
        OpacitySlider.ValueChanged += (_, _) => Apply();

        foreach (var box in new[]
                 {
                     AutonomousWanderBox, WindowWalkingBox, CursorChaseBox, RandomReactionsBox,
                     SleepBox, ScrollPlayBox, MoodsBox, SystemReactionsBox,
                     ReducedMotionBox, BatteryConsciousBox, DisableMonitoringBox,
                     QuietModeBox, HidePresentBox, DevEventsBox,
                     AutoUpdateBox, RememberStateBox, AutostartBox, ClickThroughBox,
                     AiEnableBox, AiToolsBox, AiChatterBox, AiStreamingBox, AiHotkeyBox, AiMemoryBox,
                 })
        {
            box.Checked   += (_, _) => Apply();
            box.Unchecked += (_, _) => Apply();
        }

        foreach (var combo in new[] { SizeBox, CatColorBox, StretchIntervalBox, AiProviderBox, FocusDurationBox, BreakDurationBox })
            combo.SelectionChanged += (_, _) => Apply();


        DevPortBox.TextChanged    += (_, _) => Apply();
        AiKeyBox.PasswordChanged  += (_, _) => Apply();
        AiPersonaBox.TextChanged  += (_, _) => Apply();
        AiModelBox.TextChanged    += (_, _) => Apply();
        OllamaUrlBox.TextChanged  += (_, _) => Apply();
        OllamaModelBox.TextChanged+= (_, _) => Apply();
    }

    private static void SelectByTag(ComboBox box, string tag, int fallbackIndex)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.SelectedIndex = fallbackIndex;
    }

    private void PopulateCompanionPacks(string currentPet)
    {
        CatColorBox.Items.Clear();
        var packs = AssetSource.DiscoverPacks();
        foreach (var pack in packs)
        {
            var item = new ComboBoxItem
            {
                Content = pack.DisplayName,
                Tag = pack.Id,
                ToolTip = pack.IsValid ? null : $"Warning: {pack.Error}"
            };
            if (!pack.IsValid)
            {
                item.Foreground = System.Windows.Media.Brushes.IndianRed;
            }
            CatColorBox.Items.Add(item);
        }

        SelectByTag(CatColorBox, currentPet, fallbackIndex: 0);
        UpdatePackWarning();
    }

    private readonly Dictionary<string, ImageSource?> _previewCache = new(StringComparer.OrdinalIgnoreCase);

    private void UpdatePreview(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
        {
            CharacterPreviewImage.Source = null;
            return;
        }

        if (!_previewCache.TryGetValue(packId, out var img))
        {
            img = AssetSource.GetPackPreview(packId);
            _previewCache[packId] = img;
        }
        CharacterPreviewImage.Source = img;
    }

    private void UpdatePackWarning()
    {
        if (CatColorBox.SelectedItem is ComboBoxItem item && item.Tag is string packId)
        {
            UpdatePreview(packId);
            var (isValid, error) = AssetSource.ValidatePack(packId);
            if (!isValid)
            {
                PackWarningText.Text = $"Warning: This pack has errors ({error}). Neku will fall back to the default companion if loaded.";
                PackWarningText.Visibility = Visibility.Visible;
                return;
            }
        }
        else
        {
            CharacterPreviewImage.Source = null;
        }
        PackWarningText.Visibility = Visibility.Collapsed;
    }

    private static string TagOf(ComboBox box) =>
        box.SelectedItem is ComboBoxItem ci ? ci.Tag?.ToString() ?? "" : "";

    private void UpdateProviderPanels()
    {
        bool ollama = TagOf(AiProviderBox).Equals("ollama", StringComparison.OrdinalIgnoreCase);
        GeminiPanel.Visibility = ollama ? Visibility.Collapsed : Visibility.Visible;
        OllamaPanel.Visibility = ollama ? Visibility.Visible   : Visibility.Collapsed;
    }

    private void Apply()
    {
        if (!_loaded) return;

        _settings.Speed                      = SpeedSlider.Value;
        _settings.Opacity                    = OpacitySlider.Value;
        _settings.EnableAutonomousWandering  = AutonomousWanderBox.IsChecked == true;
        _settings.EnableWindowWalking        = WindowWalkingBox.IsChecked    == true;
        _settings.EnableCursorChase          = CursorChaseBox.IsChecked      == true;
        _settings.EnableRandomReactions      = RandomReactionsBox.IsChecked  == true;
        _settings.EnableSleep                = SleepBox.IsChecked            == true;
        _settings.EnableScrollPlay           = ScrollPlayBox.IsChecked       == true;
        _settings.EnableMoods                = MoodsBox.IsChecked            == true;
        _settings.EnableSystemReactions      = SystemReactionsBox.IsChecked  == true;
        _settings.ReducedMotion              = ReducedMotionBox.IsChecked    == true;
        _settings.BatteryConscious           = BatteryConsciousBox.IsChecked == true;
        _settings.DisableOptionalMonitoring  = DisableMonitoringBox.IsChecked == true;
        _settings.EnableQuietMode            = QuietModeBox.IsChecked        == true;
        _settings.HideWhenPresenting         = HidePresentBox.IsChecked      == true;
        _settings.UseSoftwareRendering       = SoftwareRenderingBox.IsChecked == true;
        _settings.EnableDeveloperEvents      = DevEventsBox.IsChecked        == true;
        if (int.TryParse(DevPortBox.Text.Trim(), out int port) && port > 1024 && port < 65535)
            _settings.DeveloperPort = port;

        DevPanel.Visibility = _settings.EnableDeveloperEvents ? Visibility.Visible : Visibility.Collapsed;

        _settings.ClickThrough               = ClickThroughBox.IsChecked     == true;
        _settings.EnableAutoUpdate           = AutoUpdateBox.IsChecked       == true;
        _settings.RememberPetState           = RememberStateBox.IsChecked    == true;

        _settings.EnableAiCompanion  = AiEnableBox.IsChecked    == true;
        _settings.AiEnableTools      = AiToolsBox.IsChecked     == true;
        _settings.AiProactiveChatter = AiChatterBox.IsChecked   == true;
        _settings.AiStreaming        = AiStreamingBox.IsChecked == true;
        _settings.AiHotkeyEnabled    = AiHotkeyBox.IsChecked    == true;
        _settings.AiEnableMemory     = AiMemoryBox.IsChecked    == true;
        _settings.AiApiKey           = AiKeyBox.Password;

        if (!string.IsNullOrWhiteSpace(AiPersonaBox.Text))   _settings.AiPersona   = AiPersonaBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(AiModelBox.Text))     _settings.AiModel     = AiModelBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(OllamaUrlBox.Text))   _settings.OllamaUrl   = OllamaUrlBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(OllamaModelBox.Text)) _settings.OllamaModel = OllamaModelBox.Text.Trim();

        _settings.AiProvider = TagOf(AiProviderBox) is { Length: > 0 } p ? p : "gemini";
        UpdateProviderPanels();

        bool autostart = AutostartBox.IsChecked == true;
        _settings.Autostart = autostart;
        AutostartService.Set(autostart);

        if (int.TryParse(TagOf(StretchIntervalBox), out int mins)) _settings.StretchIntervalMinutes = mins;
        if (int.TryParse(TagOf(FocusDurationBox), out int focusMins) && focusMins > 0) _settings.FocusDurationMinutes = focusMins;
        if (int.TryParse(TagOf(BreakDurationBox), out int breakMins) && breakMins > 0) _settings.BreakDurationMinutes = breakMins;

        if (double.TryParse(TagOf(SizeBox), System.Globalization.CultureInfo.InvariantCulture, out double size))

            _settings.SizeScale = size;

        string pet = TagOf(CatColorBox);
        if (pet.Length > 0 && !string.Equals(pet, _settings.ActivePet, StringComparison.OrdinalIgnoreCase))
        {
            if (App.CurrentApp != null)
            {
                App.CurrentApp.TrySwitchPet(pet);
            }
            else
            {
                _settings.ActivePet = pet;
            }
            ColorRestartNote.Visibility = Visibility.Collapsed;
            UpdatePackWarning();
        }

        _service.Save();
        _onChanged?.Invoke();   // apply live (e.g. resize or change opacity)
    }

    /// <summary>Capture a new shortcut: record the first non-modifier key pressed with a modifier held.</summary>
    private void HotkeyCaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key k = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore bare modifier presses — wait for an actual key.
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
              or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None)
            return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return;

        _settings.AiHotkeyModifiers = (int)mods;
        _settings.AiHotkeyKey       = (int)k;
        HotkeyCaptureBox.Text       = FormatHotkey(mods, k);
        Apply();
    }

    private static string FormatHotkey(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    // Developer Test Buttons
    private void DevTestBuildSuccess_Click(object sender, RoutedEventArgs e)
        => _onTriggerDevEvent?.Invoke("build_success", "Build succeeded! 🎉");

    private void DevTestBuildFail_Click(object sender, RoutedEventArgs e)
        => _onTriggerDevEvent?.Invoke("build_failure", "Build failed! 💥");

    private void DevTestPass_Click(object sender, RoutedEventArgs e)
        => _onTriggerDevEvent?.Invoke("test_pass", "All tests green! ✨");

    private void DevTestFail_Click(object sender, RoutedEventArgs e)
        => _onTriggerDevEvent?.Invoke("test_fail", "Tests failed! 😱");

    private void ResetPositionButton_Click(object sender, RoutedEventArgs e)
        => _onResetPosition?.Invoke();

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Restore all Neku settings to their defaults?",
            "Restore Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            var def = new AppSettings();
            _settings.Speed                     = def.Speed;
            _settings.Opacity                   = def.Opacity;
            _settings.SizeScale                 = def.SizeScale;
            _settings.EnableAutonomousWandering = def.EnableAutonomousWandering;
            _settings.EnableWindowWalking       = def.EnableWindowWalking;
            _settings.EnableCursorChase         = def.EnableCursorChase;
            _settings.EnableRandomReactions     = def.EnableRandomReactions;
            _settings.EnableSleep               = def.EnableSleep;
            _settings.EnableScrollPlay          = def.EnableScrollPlay;
            _settings.ReducedMotion             = def.ReducedMotion;
            _settings.BatteryConscious          = def.BatteryConscious;
            _settings.DisableOptionalMonitoring = def.DisableOptionalMonitoring;
            _settings.EnableQuietMode           = def.EnableQuietMode;
            _settings.HideWhenPresenting        = def.HideWhenPresenting;
            _settings.EnableDeveloperEvents     = def.EnableDeveloperEvents;
            _settings.ClickThrough              = def.ClickThrough;
            _settings.StretchIntervalMinutes    = def.StretchIntervalMinutes;

            SpeedSlider.Value            = _settings.Speed;
            OpacitySlider.Value          = _settings.Opacity;
            AutonomousWanderBox.IsChecked= _settings.EnableAutonomousWandering;
            WindowWalkingBox.IsChecked   = _settings.EnableWindowWalking;
            CursorChaseBox.IsChecked     = _settings.EnableCursorChase;
            RandomReactionsBox.IsChecked = _settings.EnableRandomReactions;
            SleepBox.IsChecked           = _settings.EnableSleep;
            ScrollPlayBox.IsChecked      = _settings.EnableScrollPlay;
            ReducedMotionBox.IsChecked   = _settings.ReducedMotion;
            BatteryConsciousBox.IsChecked= _settings.BatteryConscious;
            DisableMonitoringBox.IsChecked = _settings.DisableOptionalMonitoring;
            QuietModeBox.IsChecked       = _settings.EnableQuietMode;
            HidePresentBox.IsChecked     = _settings.HideWhenPresenting;
            DevEventsBox.IsChecked       = _settings.EnableDeveloperEvents;
            ClickThroughBox.IsChecked    = _settings.ClickThrough;

            SelectByTag(SizeBox, "1.0", 1);
            SelectByTag(StretchIntervalBox, "30", 2);

            Apply();
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_onTestAi == null) return;
        TestButton.IsEnabled = false;
        TestResult.Text = "Asking Neku…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TestResult.Text = await _onTestAi(cts.Token);
        }
        catch (Exception ex)
        {
            TestResult.Text = ex.Message;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private async void UpdateCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_onCheckUpdate == null) return;
        UpdateCheckButton.IsEnabled = false;
        UpdateResult.Text = "Checking…";
        try   { UpdateResult.Text = await _onCheckUpdate(); }
        catch (Exception ex) { UpdateResult.Text = ex.Message; }
        finally { UpdateCheckButton.IsEnabled = true; }
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Path.GetTempPath()) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    private void ForgetButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Make Neku forget everything it remembers about you and your past chats?",
            "Forget memory", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            _onForgetMemory?.Invoke();
    }

    private void OpenStudioButton_Click(object sender, RoutedEventArgs e)
    {
        App.CurrentApp?.OpenCompanionStudio();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
