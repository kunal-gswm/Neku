using DesktopPet.Engine;
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
using TextBox = System.Windows.Controls.TextBox;

namespace DesktopPet.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _service;
    private readonly Action? _onChanged;
    private readonly Func<Task<string>>? _onCheckUpdate;
    private readonly Action? _onResetPosition;
    private readonly Action<string, string>? _onTriggerDevEvent;
    private readonly string _initialPet;
    private readonly BackupService? _backupService;
    private bool _loaded;

    public SettingsWindow(AppSettings settings, SettingsService service,
                          Action? onChanged = null,
                          Func<Task<string>>? onCheckUpdate = null,
                          Action? onResetPosition = null,
                          Action<string, string>? onTriggerDevEvent = null,
                          BackupService? backupService = null)
    {
        InitializeComponent();
        _settings = settings;
        _service = service;
        _onChanged = onChanged;
        _onCheckUpdate = onCheckUpdate;
        _onResetPosition = onResetPosition;
        _onTriggerDevEvent = onTriggerDevEvent;
        _backupService = backupService;
        _initialPet = settings.ActivePet;

        // Behavior Profile (v1.0.5)
        SelectByTag(BehaviorProfileCombo, settings.BehaviorProfile.ToString(), fallbackIndex: 1);
        ProductivityReactionsBox.IsChecked = settings.EnableProductivityReactions;
        FocusSuppressionBox.IsChecked = settings.SuppressInteractionsDuringFocus;
        UpdateBehaviorProfileDescription();

        // Appearance
        SpeedSlider.Value = settings.Speed;
        OpacitySlider.Value = settings.Opacity;

        // Behavior
        AutonomousWanderBox.IsChecked = settings.EnableAutonomousWandering;
        WindowWalkingBox.IsChecked = settings.EnableWindowWalking;
        CursorChaseBox.IsChecked = settings.EnableCursorChase;
        RandomReactionsBox.IsChecked = settings.EnableRandomReactions;
        SleepBox.IsChecked = settings.EnableSleep;
        ScrollPlayBox.IsChecked = settings.EnableScrollPlay;
        MoodsBox.IsChecked = settings.EnableMoods;
        SystemReactionsBox.IsChecked = settings.EnableSystemReactions;
        TaskReactionsBox.IsChecked = settings.EnableTaskReactions;
        SmoothMovementBox.IsChecked = settings.EnableSmoothMovement;
        EdgeRestingBox.IsChecked = settings.EnableEdgeResting;
        ToyInteractionsBox.IsChecked = settings.EnableToyInteractions;

        // Dropzone
        DropzoneBox.IsChecked = settings.EnableDropzone;
        DropzonePanel.Visibility = settings.EnableDropzone ? Visibility.Visible : Visibility.Collapsed;
        SelectByTag(DropzoneActionBox, settings.DropzoneDefaultAction, fallbackIndex: 0);

        // Productivity Alerts
        ScheduledRemindersBox.IsChecked = settings.EnableScheduledReminders;
        QuietHoursBox.IsChecked = settings.EnableQuietHours;
        UpcomingTaskRemindersBox.IsChecked = settings.EnableUpcomingTaskReminders;
        OverdueTaskRemindersBox.IsChecked = settings.EnableOverdueTaskReminders;
        FocusCompletedRemindersBox.IsChecked = settings.EnableFocusCompletedReminders;
        BreakCompletedRemindersBox.IsChecked = settings.EnableBreakCompletedReminders;

        // Performance
        ReducedMotionBox.IsChecked = settings.ReducedMotion;
        BatteryConsciousBox.IsChecked = settings.BatteryConscious;
        DisableMonitoringBox.IsChecked = settings.DisableOptionalMonitoring;
        QuietModeBox.IsChecked = settings.EnableQuietMode;
        HidePresentBox.IsChecked = settings.HideWhenPresenting;
        SoftwareRenderingBox.IsChecked = settings.UseSoftwareRendering;

        // Developer
        DevEventsBox.IsChecked = settings.EnableDeveloperEvents;
        DevPortBox.Text = settings.DeveloperPort.ToString();
        DevPanel.Visibility = settings.EnableDeveloperEvents ? Visibility.Visible : Visibility.Collapsed;

        // Agent Companion
        AgentCompanionBox.IsChecked = settings.EnableAgentCompanion;
        AgentReactionsBox.IsChecked = settings.EnableAgentReactions;
        AgentStartedBox.IsChecked = settings.EnableAgentStartedReaction;
        AgentWaitingBox.IsChecked = settings.EnableAgentWaitingReaction;
        AgentCompletedBox.IsChecked = settings.EnableAgentCompletedReaction;
        AgentFailedBox.IsChecked = settings.EnableAgentFailedReaction;
        AgentIdleBox.IsChecked = settings.EnableAgentIdleReaction;
        AgentPanel.Visibility = settings.EnableAgentCompanion ? Visibility.Visible : Visibility.Collapsed;

        // DeskSense
        DeskSenseBox.IsChecked = settings.EnableDeskSense;
        DeskSenseForegroundBox.IsChecked = settings.EnableForegroundAppDetection;
        DeskSenseIdleBox.IsChecked = settings.EnableIdleDetection;
        DeskSenseIdleSlider.Value = settings.IdleThresholdSeconds;
        DeskSenseIdleValText.Text = $"{settings.IdleThresholdSeconds}s";
        DeskSenseReactionsBox.IsChecked = settings.EnableDeskSenseReactions;
        DeskSenseCodingBox.IsChecked = settings.EnableCodingStartedReaction;
        DeskSenseTerminalBox.IsChecked = settings.EnableTerminalActiveReaction;
        DeskSenseIdleReactionBox.IsChecked = settings.EnableUserIdleReaction;
        DeskSenseReturnedBox.IsChecked = settings.EnableUserReturnedReaction;
        DeskSensePresentationBox.IsChecked = settings.EnablePresentationStartedReaction;
        DeskSensePanel.Visibility = settings.EnableDeskSense ? Visibility.Visible : Visibility.Collapsed;

        // Radar
        RadarEnabledBox.IsChecked = settings.EnableDeveloperRadar;
        RadarHudBox.IsChecked = settings.EnableDeveloperHud;
        RadarPauseBox.IsChecked = settings.PauseRepositoryMonitoring;
        RadarPanel.Visibility = settings.EnableDeveloperRadar ? Visibility.Visible : Visibility.Collapsed;
        PopulateRadarSettingsRepos();

        // System Health
        HealthMonitoringBox.IsChecked = settings.EnableSystemHealthMonitoring;
        HealthCompanionAlertsBox.IsChecked = settings.EnableHealthCompanionAlerts;
        HealthHudBox.IsChecked = settings.EnableHealthHud;
        HealthCpuSlider.Value = settings.SystemHealthCpuThreshold;
        HealthCpuValText.Text = $"{settings.SystemHealthCpuThreshold}%";
        HealthRamSlider.Value = settings.SystemHealthRamThreshold;
        HealthRamValText.Text = $"{settings.SystemHealthRamThreshold}%";
        HealthPanel.Visibility = settings.EnableSystemHealthMonitoring ? Visibility.Visible : Visibility.Collapsed;

        // System
        AutostartBox.IsChecked = AutostartService.IsEnabled();
        ClickThroughBox.IsChecked = settings.ClickThrough;
        AutoUpdateBox.IsChecked = settings.EnableAutoUpdate;
        RememberStateBox.IsChecked = settings.RememberPetState;
        CommandPaletteHotkeyBox.IsChecked = settings.EnableCommandPaletteHotkey;
        PaletteHotkeyCaptureBox.Text = HotkeyService.FormatHotkey((ModifierKeys)settings.CommandPaletteHotkeyModifiers, (Key)settings.CommandPaletteHotkeyKey);
        UpdatePaletteHotkeyStatus();

        QuickCaptureHotkeyBox.IsChecked = settings.EnableQuickCaptureHotkey;
        QuickCaptureHotkeyCaptureBox.Text = HotkeyService.FormatHotkey((ModifierKeys)settings.QuickCaptureHotkeyModifiers, (Key)settings.QuickCaptureHotkeyKey);
        UpdateQuickCaptureHotkeyStatus();

        VersionText.Text = $"Neku {AppVersion.Display}  •  settings in %AppData%\\Neku";

        AutoBackupsBox.IsChecked = settings.EnableAutomaticBackups;
        RefreshBackupsList();

        SelectByTag(StretchIntervalBox, settings.StretchIntervalMinutes.ToString(), fallbackIndex: 0);
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

        _loaded = true;
        WireUp();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        DesktopPet.Native.Win32.EnableImmersiveDarkMode(handle);
    }

    /// <summary>Hook every control to <see cref="Apply"/>. Done after load so setting the
    /// initial values doesn't trigger a save-and-reapply storm on open.</summary>
    private void WireUp()
    {
        SpeedSlider.ValueChanged += (_, _) => Apply();
        OpacitySlider.ValueChanged += (_, _) => Apply();

        foreach (var box in new[]
                 {
                     AutonomousWanderBox, WindowWalkingBox, CursorChaseBox, RandomReactionsBox,
                     SleepBox, ScrollPlayBox, MoodsBox, SystemReactionsBox, TaskReactionsBox,
                     ReducedMotionBox, BatteryConsciousBox, DisableMonitoringBox,
                     QuietModeBox, HidePresentBox, DevEventsBox,
                     AutoUpdateBox, RememberStateBox, AutostartBox, ClickThroughBox, CommandPaletteHotkeyBox,
                     AgentCompanionBox, AgentReactionsBox, AgentStartedBox, AgentWaitingBox,
                     AgentCompletedBox, AgentFailedBox, AgentIdleBox,
                     DeskSenseBox, DeskSenseForegroundBox, DeskSenseIdleBox,
                     DeskSenseReactionsBox, DeskSenseCodingBox, DeskSenseTerminalBox,
                     DeskSenseIdleReactionBox, DeskSenseReturnedBox, DeskSensePresentationBox,
                     RadarEnabledBox, RadarHudBox, RadarPauseBox,
                     SmoothMovementBox, EdgeRestingBox, ToyInteractionsBox,
                     DropzoneBox,
                     HealthMonitoringBox, HealthCompanionAlertsBox, HealthHudBox,
                 })
        {
            box.Checked += (_, _) => Apply();
            box.Unchecked += (_, _) => Apply();
        }

        AgentCompanionBox.Checked += (_, _) => { AgentPanel.Visibility = Visibility.Visible; Apply(); };
        AgentCompanionBox.Unchecked += (_, _) => { AgentPanel.Visibility = Visibility.Collapsed; Apply(); };

        DeskSenseBox.Checked += (_, _) => { DeskSensePanel.Visibility = Visibility.Visible; Apply(); };
        DeskSenseBox.Unchecked += (_, _) => { DeskSensePanel.Visibility = Visibility.Collapsed; Apply(); };
        DeskSenseIdleSlider.ValueChanged += (_, _) =>
        {
            DeskSenseIdleValText.Text = $"{(int)DeskSenseIdleSlider.Value}s";
            Apply();
        };

        RadarEnabledBox.Checked += (_, _) => { RadarPanel.Visibility = Visibility.Visible; Apply(); };
        RadarEnabledBox.Unchecked += (_, _) => { RadarPanel.Visibility = Visibility.Collapsed; Apply(); };

        DropzoneBox.Checked += (_, _) => { DropzonePanel.Visibility = Visibility.Visible; Apply(); };
        DropzoneBox.Unchecked += (_, _) => { DropzonePanel.Visibility = Visibility.Collapsed; Apply(); };

        HealthMonitoringBox.Checked += (_, _) => { HealthPanel.Visibility = Visibility.Visible; Apply(); };
        HealthMonitoringBox.Unchecked += (_, _) => { HealthPanel.Visibility = Visibility.Collapsed; Apply(); };
        HealthCpuSlider.ValueChanged += (_, _) =>
        {
            HealthCpuValText.Text = $"{(int)HealthCpuSlider.Value}%";
            Apply();
        };
        HealthRamSlider.ValueChanged += (_, _) =>
        {
            HealthRamValText.Text = $"{(int)HealthRamSlider.Value}%";
            Apply();
        };

        foreach (var combo in new[] { SizeBox, CatColorBox, StretchIntervalBox, FocusDurationBox, BreakDurationBox, DropzoneActionBox })
            combo.SelectionChanged += (_, _) => Apply();

        DevPortBox.TextChanged += (_, _) => Apply();
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

    private void Apply()
    {
        if (!_loaded) return;

        _settings.Speed = SpeedSlider.Value;
        _settings.Opacity = OpacitySlider.Value;
        _settings.EnableAutonomousWandering = AutonomousWanderBox.IsChecked == true;
        _settings.EnableWindowWalking = WindowWalkingBox.IsChecked == true;
        _settings.EnableCursorChase = CursorChaseBox.IsChecked == true;
        _settings.EnableRandomReactions = RandomReactionsBox.IsChecked == true;
        _settings.EnableSleep = SleepBox.IsChecked == true;
        _settings.EnableScrollPlay = ScrollPlayBox.IsChecked == true;
        _settings.EnableMoods = MoodsBox.IsChecked == true;
        _settings.EnableSystemReactions = SystemReactionsBox.IsChecked == true;
        _settings.EnableTaskReactions = TaskReactionsBox.IsChecked == true;
        _settings.EnableProductivityReactions = ProductivityReactionsBox.IsChecked == true;
        _settings.SuppressInteractionsDuringFocus = FocusSuppressionBox.IsChecked == true;
        _settings.EnableAutomaticBackups = AutoBackupsBox.IsChecked == true;
        _settings.EnableSmoothMovement = SmoothMovementBox.IsChecked == true;
        _settings.EnableEdgeResting = EdgeRestingBox.IsChecked == true;
        _settings.EnableToyInteractions = ToyInteractionsBox.IsChecked == true;

        // Dropzone
        _settings.EnableDropzone = DropzoneBox.IsChecked == true;
        string dropAction = TagOf(DropzoneActionBox);
        if (!string.IsNullOrEmpty(dropAction))
        {
            _settings.DropzoneDefaultAction = dropAction;
        }
        DropzonePanel.Visibility = _settings.EnableDropzone ? Visibility.Visible : Visibility.Collapsed;

        _settings.EnableScheduledReminders = ScheduledRemindersBox.IsChecked == true;
        _settings.EnableQuietHours = QuietHoursBox.IsChecked == true;
        _settings.EnableUpcomingTaskReminders = UpcomingTaskRemindersBox.IsChecked == true;
        _settings.EnableOverdueTaskReminders = OverdueTaskRemindersBox.IsChecked == true;
        _settings.EnableFocusCompletedReminders = FocusCompletedRemindersBox.IsChecked == true;
        _settings.EnableBreakCompletedReminders = BreakCompletedRemindersBox.IsChecked == true;
        _settings.ReducedMotion = ReducedMotionBox.IsChecked == true;
        _settings.BatteryConscious = BatteryConsciousBox.IsChecked == true;
        _settings.DisableOptionalMonitoring = DisableMonitoringBox.IsChecked == true;
        _settings.EnableQuietMode = QuietModeBox.IsChecked == true;
        _settings.HideWhenPresenting = HidePresentBox.IsChecked == true;
        _settings.UseSoftwareRendering = SoftwareRenderingBox.IsChecked == true;
        _settings.EnableDeveloperEvents = DevEventsBox.IsChecked == true;
        if (int.TryParse(DevPortBox.Text.Trim(), out int port) && port > 1024 && port < 65535)
            _settings.DeveloperPort = port;

        DevPanel.Visibility = _settings.EnableDeveloperEvents ? Visibility.Visible : Visibility.Collapsed;

        // Agent Companion
        _settings.EnableAgentCompanion = AgentCompanionBox.IsChecked == true;
        _settings.EnableAgentReactions = AgentReactionsBox.IsChecked == true;
        _settings.EnableAgentStartedReaction = AgentStartedBox.IsChecked == true;
        _settings.EnableAgentWaitingReaction = AgentWaitingBox.IsChecked == true;
        _settings.EnableAgentCompletedReaction = AgentCompletedBox.IsChecked == true;
        _settings.EnableAgentFailedReaction = AgentFailedBox.IsChecked == true;
        _settings.EnableAgentIdleReaction = AgentIdleBox.IsChecked == true;

        // DeskSense
        _settings.EnableDeskSense = DeskSenseBox.IsChecked == true;
        _settings.EnableForegroundAppDetection = DeskSenseForegroundBox.IsChecked == true;
        _settings.EnableIdleDetection = DeskSenseIdleBox.IsChecked == true;
        _settings.IdleThresholdSeconds = (int)DeskSenseIdleSlider.Value;
        _settings.EnableDeskSenseReactions = DeskSenseReactionsBox.IsChecked == true;
        _settings.EnableCodingStartedReaction = DeskSenseCodingBox.IsChecked == true;
        _settings.EnableTerminalActiveReaction = DeskSenseTerminalBox.IsChecked == true;
        _settings.EnableUserIdleReaction = DeskSenseIdleReactionBox.IsChecked == true;
        _settings.EnableUserReturnedReaction = DeskSenseReturnedBox.IsChecked == true;
        _settings.EnablePresentationStartedReaction = DeskSensePresentationBox.IsChecked == true;
        DeskSensePanel.Visibility = _settings.EnableDeskSense ? Visibility.Visible : Visibility.Collapsed;

        // Radar
        _settings.EnableDeveloperRadar = RadarEnabledBox.IsChecked == true;
        _settings.EnableDeveloperHud = RadarHudBox.IsChecked == true;
        _settings.PauseRepositoryMonitoring = RadarPauseBox.IsChecked == true;
        RadarPanel.Visibility = _settings.EnableDeveloperRadar ? Visibility.Visible : Visibility.Collapsed;

        // System Health
        _settings.EnableSystemHealthMonitoring = HealthMonitoringBox.IsChecked == true;
        _settings.EnableHealthCompanionAlerts = HealthCompanionAlertsBox.IsChecked == true;
        _settings.EnableHealthHud = HealthHudBox.IsChecked == true;
        _settings.SystemHealthCpuThreshold = (int)HealthCpuSlider.Value;
        _settings.SystemHealthRamThreshold = (int)HealthRamSlider.Value;
        HealthPanel.Visibility = _settings.EnableSystemHealthMonitoring ? Visibility.Visible : Visibility.Collapsed;

        _settings.ClickThrough = ClickThroughBox.IsChecked == true;
        _settings.EnableAutoUpdate = AutoUpdateBox.IsChecked == true;
        _settings.RememberPetState = RememberStateBox.IsChecked == true;
        _settings.EnableCommandPaletteHotkey = CommandPaletteHotkeyBox.IsChecked == true;
        _settings.EnableQuickCaptureHotkey = QuickCaptureHotkeyBox.IsChecked == true;

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
        UpdatePaletteHotkeyStatus();
        UpdateQuickCaptureHotkeyStatus();
    }

    /// <summary>Capture a new Command Palette shortcut: record first non-modifier key with modifier held.</summary>
    private void PaletteHotkeyCaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key k = e.Key == Key.System ? e.SystemKey : e.Key;

        // Allow Tab and Shift+Tab to navigate away normally without getting trapped
        if (k == Key.Tab) return;

        // Allow Escape to cancel or close dialog
        if (k == Key.Escape) return;

        // Allow Backspace or Delete to reset to default
        if (k is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            ResetPaletteHotkeyButton_Click(sender, e);
            return;
        }

        // Ignore bare modifier keys
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
              or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None)
            return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return;

        e.Handled = true;
        _settings.CommandPaletteHotkeyModifiers = (int)mods;
        _settings.CommandPaletteHotkeyKey = (int)k;
        PaletteHotkeyCaptureBox.Text = HotkeyService.FormatHotkey(mods, k);
        Apply();
        UpdatePaletteHotkeyStatus();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        HandleKeyDown(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers, e);
    }

    internal void HandleKeyDown(Key key, ModifierKeys modifiers, KeyEventArgs? e = null)
    {
        if (key == Key.Escape)
        {
            if ((PaletteHotkeyCaptureBox.IsKeyboardFocusWithin || QuickCaptureHotkeyCaptureBox.IsKeyboardFocusWithin) || (FocusManager.GetFocusedElement(this) is TextBox))
            {
                Keyboard.ClearFocus();
                if (e != null) e.Handled = true;
                return;
            }

            Close();
            if (e != null) e.Handled = true;
            return;
        }

        if (key == Key.W && (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Close();
            if (e != null) e.Handled = true;
            return;
        }

        // Allow opening Command Palette via Ctrl+P, Ctrl+Shift+P, or Ctrl+Alt+P when not capturing hotkey
        if (!(PaletteHotkeyCaptureBox.IsKeyboardFocusWithin || QuickCaptureHotkeyCaptureBox.IsKeyboardFocusWithin) && key == Key.P)
        {
            if (modifiers == ModifierKeys.Control ||
                modifiers == (ModifierKeys.Control | ModifierKeys.Shift) ||
                modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
            {
                App.CurrentApp?.ToggleCommandPalette();
                if (e != null) e.Handled = true;
                return;
            }
        }

        // Ctrl+Tab / Ctrl+Shift+Tab cycling through tabs
        if (!(PaletteHotkeyCaptureBox.IsKeyboardFocusWithin || QuickCaptureHotkeyCaptureBox.IsKeyboardFocusWithin) && (modifiers & ModifierKeys.Control) == ModifierKeys.Control && key == Key.Tab)
        {
            int count = SettingsTabControl.Items.Count;
            if (count > 0)
            {
                bool reverse = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                int current = SettingsTabControl.SelectedIndex;
                if (current < 0) current = 0;
                int next = reverse ? (current - 1 + count) % count : (current + 1) % count;
                SettingsTabControl.SelectedIndex = next;
                if (e != null) e.Handled = true;
                return;
            }
        }

        // Ctrl + 1..6 direct tab navigation
        if (!(PaletteHotkeyCaptureBox.IsKeyboardFocusWithin || QuickCaptureHotkeyCaptureBox.IsKeyboardFocusWithin) && modifiers == ModifierKeys.Control)
        {
            int targetTab = key switch
            {
                Key.D1 or Key.NumPad1 => 0,
                Key.D2 or Key.NumPad2 => 1,
                Key.D3 or Key.NumPad3 => 2,
                Key.D4 or Key.NumPad4 => 3,
                Key.D5 or Key.NumPad5 => 4,
                Key.D6 or Key.NumPad6 => 5,
                _ => -1
            };

            if (targetTab >= 0 && targetTab < SettingsTabControl.Items.Count)
            {
                SettingsTabControl.SelectedIndex = targetTab;
                if (e != null) e.Handled = true;
                return;
            }
        }
    }

    private void ResetPaletteHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.CommandPaletteHotkeyModifiers = (int)(ModifierKeys.Control | ModifierKeys.Alt);
        _settings.CommandPaletteHotkeyKey = (int)Key.P;
        PaletteHotkeyCaptureBox.Text = HotkeyService.FormatHotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.P);
        Apply();
        UpdatePaletteHotkeyStatus();
    }

    /// <summary>Capture a new Quick Capture shortcut: record first non-modifier key with modifier held.</summary>
    private void QuickCaptureHotkeyCaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key k = e.Key == Key.System ? e.SystemKey : e.Key;

        if (k == Key.Tab) return;
        if (k == Key.Escape) return;

        if (k is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            ResetQuickCaptureHotkeyButton_Click(sender, e);
            return;
        }

        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
              or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None)
            return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return;

        e.Handled = true;
        _settings.QuickCaptureHotkeyModifiers = (int)mods;
        _settings.QuickCaptureHotkeyKey = (int)k;
        QuickCaptureHotkeyCaptureBox.Text = HotkeyService.FormatHotkey(mods, k);
        Apply();
        UpdateQuickCaptureHotkeyStatus();
    }

    private void ResetQuickCaptureHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.QuickCaptureHotkeyModifiers = (int)(ModifierKeys.Control | ModifierKeys.Alt);
        _settings.QuickCaptureHotkeyKey = (int)Key.C;
        QuickCaptureHotkeyCaptureBox.Text = HotkeyService.FormatHotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.C);
        Apply();
        UpdateQuickCaptureHotkeyStatus();
    }

    private void UpdateQuickCaptureHotkeyStatus()
    {
        if (QuickCaptureHotkeyStatusText == null) return;

        if (QuickCaptureHotkeyBox.IsChecked != true)
        {
            QuickCaptureHotkeyStatusText.Text = "Disabled";
            QuickCaptureHotkeyStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        QuickCaptureHotkeyStatusText.Text = "✓ Ready";
        QuickCaptureHotkeyStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
    }

    private void UpdatePaletteHotkeyStatus()
    {
        if (PaletteHotkeyStatusText == null) return;

        if (CommandPaletteHotkeyBox.IsChecked != true)
        {
            PaletteHotkeyStatusText.Text = "Disabled";
            PaletteHotkeyStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        if (App.CurrentApp == null)
        {
            PaletteHotkeyStatusText.Text = "✓ Ready";
            PaletteHotkeyStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            return;
        }

        if (App.CurrentApp.IsPaletteHotkeyRegistered)
        {
            string? active = App.CurrentApp.GetPaletteHotkeyLabel();
            PaletteHotkeyStatusText.Text = $"✓ Active ({active})";
            PaletteHotkeyStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        else
        {
            PaletteHotkeyStatusText.Text = "⚠ Conflict (in use by another app)";
            PaletteHotkeyStatusText.Foreground = System.Windows.Media.Brushes.Orange;
        }
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
            _settings.Speed = def.Speed;
            _settings.Opacity = def.Opacity;
            _settings.SizeScale = def.SizeScale;
            _settings.EnableAutonomousWandering = def.EnableAutonomousWandering;
            _settings.EnableWindowWalking = def.EnableWindowWalking;
            _settings.EnableCursorChase = def.EnableCursorChase;
            _settings.EnableRandomReactions = def.EnableRandomReactions;
            _settings.EnableSleep = def.EnableSleep;
            _settings.EnableScrollPlay = def.EnableScrollPlay;
            _settings.ReducedMotion = def.ReducedMotion;
            _settings.BatteryConscious = def.BatteryConscious;
            _settings.DisableOptionalMonitoring = def.DisableOptionalMonitoring;
            _settings.EnableQuietMode = def.EnableQuietMode;
            _settings.HideWhenPresenting = def.HideWhenPresenting;
            _settings.EnableDeveloperEvents = def.EnableDeveloperEvents;
            _settings.ClickThrough = def.ClickThrough;
            _settings.StretchIntervalMinutes = def.StretchIntervalMinutes;
            _settings.EnableCommandPaletteHotkey = def.EnableCommandPaletteHotkey;
            _settings.CommandPaletteHotkeyModifiers = def.CommandPaletteHotkeyModifiers;
            _settings.CommandPaletteHotkeyKey = def.CommandPaletteHotkeyKey;
            _settings.EnableSmoothMovement = def.EnableSmoothMovement;
            _settings.EnableEdgeResting = def.EnableEdgeResting;
            _settings.EnableToyInteractions = def.EnableToyInteractions;
            _settings.EnableDropzone = def.EnableDropzone;
            _settings.DropzoneDefaultAction = def.DropzoneDefaultAction;
            _settings.EnableSystemHealthMonitoring = def.EnableSystemHealthMonitoring;
            _settings.EnableHealthCompanionAlerts = def.EnableHealthCompanionAlerts;
            _settings.EnableHealthHud = def.EnableHealthHud;
            _settings.SystemHealthCpuThreshold = def.SystemHealthCpuThreshold;
            _settings.SystemHealthRamThreshold = def.SystemHealthRamThreshold;

            SpeedSlider.Value = _settings.Speed;
            OpacitySlider.Value = _settings.Opacity;
            AutonomousWanderBox.IsChecked = _settings.EnableAutonomousWandering;
            WindowWalkingBox.IsChecked = _settings.EnableWindowWalking;
            CursorChaseBox.IsChecked = _settings.EnableCursorChase;
            RandomReactionsBox.IsChecked = _settings.EnableRandomReactions;
            SleepBox.IsChecked = _settings.EnableSleep;
            ScrollPlayBox.IsChecked = _settings.EnableScrollPlay;
            SmoothMovementBox.IsChecked = _settings.EnableSmoothMovement;
            EdgeRestingBox.IsChecked = _settings.EnableEdgeResting;
            ToyInteractionsBox.IsChecked = _settings.EnableToyInteractions;
            DropzoneBox.IsChecked = _settings.EnableDropzone;
            SelectByTag(DropzoneActionBox, _settings.DropzoneDefaultAction, 0);

            HealthMonitoringBox.IsChecked = _settings.EnableSystemHealthMonitoring;
            HealthCompanionAlertsBox.IsChecked = _settings.EnableHealthCompanionAlerts;
            HealthHudBox.IsChecked = _settings.EnableHealthHud;
            HealthCpuSlider.Value = _settings.SystemHealthCpuThreshold;
            HealthCpuValText.Text = $"{_settings.SystemHealthCpuThreshold}%";
            HealthRamSlider.Value = _settings.SystemHealthRamThreshold;
            HealthRamValText.Text = $"{_settings.SystemHealthRamThreshold}%";

            ReducedMotionBox.IsChecked = _settings.ReducedMotion;
            BatteryConsciousBox.IsChecked = _settings.BatteryConscious;
            DisableMonitoringBox.IsChecked = _settings.DisableOptionalMonitoring;
            QuietModeBox.IsChecked = _settings.EnableQuietMode;
            HidePresentBox.IsChecked = _settings.HideWhenPresenting;
            DevEventsBox.IsChecked = _settings.EnableDeveloperEvents;
            ClickThroughBox.IsChecked = _settings.ClickThrough;
            CommandPaletteHotkeyBox.IsChecked = _settings.EnableCommandPaletteHotkey;
            PaletteHotkeyCaptureBox.Text = HotkeyService.FormatHotkey((ModifierKeys)_settings.CommandPaletteHotkeyModifiers, (Key)_settings.CommandPaletteHotkeyKey);

            QuickCaptureHotkeyBox.IsChecked = _settings.EnableQuickCaptureHotkey;
            QuickCaptureHotkeyCaptureBox.Text = HotkeyService.FormatHotkey((ModifierKeys)_settings.QuickCaptureHotkeyModifiers, (Key)_settings.QuickCaptureHotkeyKey);

            SelectByTag(SizeBox, "1.0", 1);
            SelectByTag(StretchIntervalBox, "30", 2);

            Apply();
        }
    }

    private async void UpdateCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_onCheckUpdate == null) return;
        UpdateCheckButton.IsEnabled = false;
        UpdateResult.Text = "Checking…";
        try { UpdateResult.Text = await _onCheckUpdate(); }
        catch (Exception ex) { UpdateResult.Text = ex.Message; }
        finally { UpdateCheckButton.IsEnabled = true; }
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Path.GetTempPath()) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    private void OpenStudioButton_Click(object sender, RoutedEventArgs e)
    {
        App.CurrentApp?.OpenCompanionStudio();
    }

    private void PopulateRadarSettingsRepos()
    {
        RadarSettingsRepoListBox.Items.Clear();
        foreach (var path in _settings.MonitoredRepositoryPaths)
        {
            RadarSettingsRepoListBox.Items.Add(path);
        }
    }

    private void RadarSettingsAddRepoBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Git Repository Folder",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            string path = dialog.FolderName;
            if (!_settings.MonitoredRepositoryPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _settings.MonitoredRepositoryPaths.Add(path);
                PopulateRadarSettingsRepos();
                Apply();
            }
        }
    }

    private void RadarSettingsRemoveRepoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (RadarSettingsRepoListBox.SelectedItem is string selectedPath)
        {
            _settings.MonitoredRepositoryPaths.RemoveAll(p => string.Equals(p, selectedPath, StringComparison.OrdinalIgnoreCase));
            PopulateRadarSettingsRepos();
            Apply();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void UpdateBehaviorProfileDescription()
    {
        BehaviorProfileDescriptionText.Text = _settings.BehaviorProfile switch
        {
            CompanionBehaviorProfile.Quiet => "Quiet: Minimal unsolicited antics, restrained movement, focus-friendly.",
            CompanionBehaviorProfile.Playful => "Playful: Expressive idle antics, frequent remarks, playful celebrations.",
            _ => "Balanced: Occasional interactions and subtle reactions to productive milestones."
        };
    }

    private void BehaviorProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        string tag = TagOf(BehaviorProfileCombo);
        if (Enum.TryParse<CompanionBehaviorProfile>(tag, true, out var profile))
        {
            Personality.ApplyBehaviorProfile(_settings, profile);
            RandomReactionsBox.IsChecked = _settings.EnableRandomReactions;
            ScrollPlayBox.IsChecked = _settings.EnableScrollPlay;
            ReducedMotionBox.IsChecked = _settings.ReducedMotion;
            FocusSuppressionBox.IsChecked = _settings.SuppressInteractionsDuringFocus;
            UpdateBehaviorProfileDescription();
            Apply();
        }
    }

    private void RefreshBackupsList()
    {
        if (_backupService == null) return;
        var backups = _backupService.GetAvailableBackups();
        BackupsListBox.ItemsSource = backups;
        RestoreBackupButton.IsEnabled = false;
        DeleteBackupButton.IsEnabled = false;
    }

    private void BackupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = BackupsListBox.SelectedItem as BackupMetadata;
        RestoreBackupButton.IsEnabled = selected != null && selected.IsValid;
        DeleteBackupButton.IsEnabled = selected != null;
    }

    private void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backupService == null) return;
        var result = _backupService.CreateBackup("Manual backup from Settings");
        if (result.Success)
        {
            RefreshBackupsList();
            BackupStatusText.Text = "Backup created successfully! 💾";
        }
        else
        {
            MessageBox.Show("Backup failed:\n" + result.ErrorMessage, "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenBackupsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backupService != null && Directory.Exists(_backupService.BackupsDirectory))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _backupService.BackupsDirectory,
                UseShellExecute = true
            });
        }
    }

    private void RefreshBackupsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshBackupsList();
        BackupStatusText.Text = "Backups refreshed! 🔄";
    }

    private void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backupService == null) return;
        if (BackupsListBox.SelectedItem is not BackupMetadata meta) return;

        var confirm = MessageBox.Show(
            $"Restore selected backup from {meta.DisplayTime}?\n\n" +
            "This will safely replace your current tasks, routines, schedules, notes, and settings with the backed up data.\n\n" +
            "An automatic safety snapshot of your current data will be created first before anything is replaced.\n\n" +
            "Do you want to proceed?",
            "Confirm Restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = _backupService.RestoreBackup(meta.FilePath);
        if (result.Success)
        {
            RefreshBackupsList();
            _onChanged?.Invoke();
            BackupStatusText.Text = "Restore complete! 🔄";
            MessageBox.Show(
                "Data restored successfully!\n\nAll tasks, routines, notes, and preferences have been restored.",
                "Restore Succeeded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            string rollbackMsg = result.RolledBack ? "\n\nCurrent data was automatically rolled back safely." : "";
            MessageBox.Show(
                $"Restore failed: {result.ErrorMessage}{rollbackMsg}",
                "Restore Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DeleteBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backupService == null) return;
        if (BackupsListBox.SelectedItem is not BackupMetadata meta) return;

        var confirm = MessageBox.Show(
            $"Delete backup from {meta.DisplayTime}?\n\nThis cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        if (_backupService.DeleteBackup(meta.FilePath))
        {
            RefreshBackupsList();
            BackupStatusText.Text = "Backup deleted. 🗑️";
        }
    }
}
