using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.Engine;
using DesktopPet.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace DesktopPet.UI;

public partial class CompanionStudioWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _service;
    private readonly Action? _onChanged;

    private readonly List<CompanionPackItem> _packItems = new();
    private CompanionPackItem? _selectedPack;

    private SpriteAnimator? _previewAnimator;
    private readonly DispatcherTimer _previewTimer;
    private DateTime _lastTickTime;
    private double _previewSpeedMultiplier = 1.0;
    private bool _isPlaying = true;
    private bool _suppressEvents;
    private Button? _selectedAnimButton;

    public CompanionStudioWindow(AppSettings settings, SettingsService service, Action? onChanged = null)
    {
        _settings = settings;
        _service = service;
        _onChanged = onChanged;
        _suppressEvents = true;

        InitializeComponent();

        _previewTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
        };
        _previewTimer.Tick += OnPreviewTick;

        Loaded += CompanionStudioWindow_Loaded;
        Closed += CompanionStudioWindow_Closed;
    }

    private void CompanionStudioWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressEvents = true;

        PopulateLibrary();
        LoadSettingsValues();

        _suppressEvents = false;

        _lastTickTime = DateTime.UtcNow;
        _previewTimer.Start();
    }

    private void CompanionStudioWindow_Closed(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        _previewTimer.Tick -= OnPreviewTick;
        _previewAnimator = null;

        SizeSlider.ValueChanged -= SizeSlider_ValueChanged;
        SpeedSlider.ValueChanged -= SpeedSlider_ValueChanged;
        OpacitySlider.ValueChanged -= OpacitySlider_ValueChanged;

        EnableFocusReactionsCheckBox.Checked -= ReactionCheckBox_Changed;
        EnableFocusReactionsCheckBox.Unchecked -= ReactionCheckBox_Changed;
        EnableBreakReactionsCheckBox.Checked -= ReactionCheckBox_Changed;
        EnableBreakReactionsCheckBox.Unchecked -= ReactionCheckBox_Changed;
        EnablePettingReactionsCheckBox.Checked -= ReactionCheckBox_Changed;
        EnablePettingReactionsCheckBox.Unchecked -= ReactionCheckBox_Changed;
        EnableDevReactionsCheckBox.Checked -= ReactionCheckBox_Changed;
        EnableDevReactionsCheckBox.Unchecked -= ReactionCheckBox_Changed;
        ReducedMotionCheckBox.Checked -= AccessibilityCheckBox_Changed;
        ReducedMotionCheckBox.Unchecked -= AccessibilityCheckBox_Changed;
        QuietModeCheckBox.Checked -= AccessibilityCheckBox_Changed;
        QuietModeCheckBox.Unchecked -= AccessibilityCheckBox_Changed;
    }

    private void PopulateLibrary()
    {
        _packItems.Clear();
        var packs = AssetSource.DiscoverPacks();

        CompanionPackItem? activeItem = null;
        CompanionPackItem? firstItem = null;

        foreach (var pack in packs)
        {
            ImageSource? preview = AssetSource.GetPackPreview(pack.Id);
            bool isActive = string.Equals(pack.Id, _settings.ActivePet, StringComparison.OrdinalIgnoreCase);

            var item = new CompanionPackItem(pack, preview, isActive);
            _packItems.Add(item);

            if (firstItem == null) firstItem = item;
            if (isActive) activeItem = item;
        }

        PacksListBox.ItemsSource = null;
        PacksListBox.ItemsSource = _packItems;

        UpdateActivePetIndicator();

        // Select active pet, or first pack
        var toSelect = activeItem ?? firstItem;
        if (toSelect != null)
        {
            PacksListBox.SelectedItem = toSelect;
        }
    }

    private void UpdateActivePetIndicator()
    {
        string activeName = AssetSource.GetDefaultDisplayName(_settings.ActivePet);
        var activePack = _packItems.FirstOrDefault(p => string.Equals(p.Info.Id, _settings.ActivePet, StringComparison.OrdinalIgnoreCase));
        if (activePack != null)
        {
            activeName = activePack.Info.DisplayName;
        }

        ActivePetIndicatorText.Text = activeName;
    }

    private void LoadSettingsValues()
    {
        // Sliders
        SizeSlider.Value = _settings.SizeScale;
        SizeValueText.Text = $"{_settings.SizeScale:0.00}x";
        SizeSlider.ValueChanged += SizeSlider_ValueChanged;

        SpeedSlider.Value = _settings.Speed;
        SpeedValueText.Text = $"{_settings.Speed:0.0}x";
        SpeedSlider.ValueChanged += SpeedSlider_ValueChanged;

        OpacitySlider.Value = _settings.Opacity;
        OpacityValueText.Text = $"{Math.Round(_settings.Opacity * 100)}%";
        OpacitySlider.ValueChanged += OpacitySlider_ValueChanged;

        // Personality Preset
        SelectPresetItem(_settings.PersonalityPreset);
        PresetDescriptionText.Text = Personality.GetPresetDescription(_settings.PersonalityPreset);

        // Reactions
        EnableFocusReactionsCheckBox.IsChecked = _settings.EnableFocusReactions;
        EnableBreakReactionsCheckBox.IsChecked = _settings.EnableBreakReactions;
        EnablePettingReactionsCheckBox.IsChecked = _settings.EnablePettingReactions;
        EnableDevReactionsCheckBox.IsChecked = _settings.EnableDevBuildReactions && _settings.EnableDevTestReactions;

        // Accessibility
        ReducedMotionCheckBox.IsChecked = _settings.ReducedMotion;
        QuietModeCheckBox.IsChecked = _settings.EnableQuietMode;

        // Hook up checkbox event handlers
        EnableFocusReactionsCheckBox.Checked += ReactionCheckBox_Changed;
        EnableFocusReactionsCheckBox.Unchecked += ReactionCheckBox_Changed;

        EnableBreakReactionsCheckBox.Checked += ReactionCheckBox_Changed;
        EnableBreakReactionsCheckBox.Unchecked += ReactionCheckBox_Changed;

        EnablePettingReactionsCheckBox.Checked += ReactionCheckBox_Changed;
        EnablePettingReactionsCheckBox.Unchecked += ReactionCheckBox_Changed;

        EnableDevReactionsCheckBox.Checked += ReactionCheckBox_Changed;
        EnableDevReactionsCheckBox.Unchecked += ReactionCheckBox_Changed;

        ReducedMotionCheckBox.Checked += AccessibilityCheckBox_Changed;
        ReducedMotionCheckBox.Unchecked += AccessibilityCheckBox_Changed;

        QuietModeCheckBox.Checked += AccessibilityCheckBox_Changed;
        QuietModeCheckBox.Unchecked += AccessibilityCheckBox_Changed;
    }

    private void SelectPresetItem(string preset)
    {
        foreach (ComboBoxItem item in PersonalityPresetComboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), preset, StringComparison.OrdinalIgnoreCase))
            {
                PersonalityPresetComboBox.SelectedItem = item;
                return;
            }
        }
        PersonalityPresetComboBox.SelectedIndex = 2; // Default to Expressive
    }

    private void PacksListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PacksListBox.SelectedItem is not CompanionPackItem item) return;
        _selectedPack = item;

        // Update Info Card
        SelectedPackTitleText.Text = item.Info.DisplayName;
        SelectedPackDescriptionText.Text = item.Info.Description ?? "No description available.";
        SelectedPackDimensionsText.Text = $"{item.Info.CellWidth}x{item.Info.CellHeight} px";
        SelectedPackScaleText.Text = $"Scale: {item.Info.Scale:0.#}x";
        SelectedPackStatusText.Text = item.Info.IsCustom ? "Local Pack" : "Built-in Pack";

        if (!item.IsValid)
        {
            PackErrorText.Text = $"Warning: {item.Info.Error}";
            PackErrorText.Visibility = Visibility.Visible;
            SetActivePetButton.IsEnabled = false;
            SetActivePetButton.Content = "Pack Has Errors";
        }
        else
        {
            PackErrorText.Visibility = Visibility.Collapsed;
            bool isCurrentActive = string.Equals(item.Info.Id, _settings.ActivePet, StringComparison.OrdinalIgnoreCase);
            SetActivePetButton.IsEnabled = !isCurrentActive;
            SetActivePetButton.Content = isCurrentActive ? "✓ Active on Desktop" : "Set as Desktop Companion";
        }

        // Load preview animator
        LoadPreviewAnimator(item.Info.Id);
    }

    private void LoadPreviewAnimator(string packId)
    {
        _previewAnimator = AssetSource.CreateAnimator(packId);
        AnimationButtonsWrapPanel.Children.Clear();
        _selectedAnimButton = null;

        if (_previewAnimator == null)
        {
            StudioPreviewImage.Source = null;
            PlayingAnimationNameText.Text = "Animation: none";
            PlayingAnimationInfoText.Text = "Failed to load spritesheet";
            return;
        }

        StudioPreviewImage.Source = _previewAnimator.GetPreviewFrame("idle");
        PlayingAnimationNameText.Text = "Animation: idle";
        PlayingAnimationInfoText.Text = "Looping • Ready";

        // Build animation chips from genuine manifest definitions
        var animKeys = _previewAnimator.Manifest.Animations.Keys
            .OrderBy(k => k.Equals("idle", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var key in animKeys)
        {
            var def = _previewAnimator.Manifest.Animations[key];
            var btn = new Button
            {
                Content = key,
                Tag = key,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 11,
                ToolTip = $"{key} • {def.Frames.Length} frames • {def.Fps:0.#} FPS"
            };

            btn.Click += (s, _) =>
            {
                if (s is Button b && b.Tag is string animName)
                {
                    PlayAnimation(animName);
                    HighlightAnimButton(b);
                }
            };

            AnimationButtonsWrapPanel.Children.Add(btn);

            if (key.Equals("idle", StringComparison.OrdinalIgnoreCase))
            {
                HighlightAnimButton(btn);
            }
        }
    }

    private static readonly Brush InactiveBtnBg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 40, 35, 55));
    private static readonly Brush InactiveBtnBorder = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 72, 60, 102));
    private static readonly Brush ActiveBtnBg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 139, 92, 246));
    private static readonly Brush ActiveBtnBorder = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 159, 117, 255));

    private void HighlightAnimButton(Button activeBtn)
    {
        if (_selectedAnimButton != null)
        {
            _selectedAnimButton.Background = InactiveBtnBg;
            _selectedAnimButton.BorderBrush = InactiveBtnBorder;
        }

        _selectedAnimButton = activeBtn;
        _selectedAnimButton.Background = ActiveBtnBg;
        _selectedAnimButton.BorderBrush = ActiveBtnBorder;
    }

    private void PlayAnimation(string animName)
    {
        if (_previewAnimator == null) return;
        _previewAnimator.Play(animName);
        _isPlaying = true;
        PlayPauseButton.Content = "⏸ Pause";

        PlayingAnimationNameText.Text = $"Animation: {animName}";
        if (_previewAnimator.Manifest.Animations.TryGetValue(animName, out var def))
        {
            PlayingAnimationInfoText.Text = $"{(def.Loop ? "Looping" : "Single play")} • {def.Frames.Length} frames • {def.Fps:0.#} FPS";
        }
        else
        {
            PlayingAnimationInfoText.Text = "Fallback animation";
        }
    }

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastTickTime).TotalSeconds;
        _lastTickTime = now;

        // Cap dt in case of thread delays or window movement
        if (dt > 0.1) dt = 0.1;

        if (!_isPlaying || _previewAnimator == null) return;

        // If reduced motion is active, preview at half speed if desired
        double effectiveSpeed = _previewSpeedMultiplier;
        if (_settings.ReducedMotion)
        {
            effectiveSpeed = Math.Min(effectiveSpeed, 0.75);
        }

        var frame = _previewAnimator.Tick(dt * effectiveSpeed);
        StudioPreviewImage.Source = frame;

        if (_previewAnimator.Finished)
        {
            PlayingAnimationInfoText.Text = "Finished (last frame held)";
        }
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        _isPlaying = !_isPlaying;
        PlayPauseButton.Content = _isPlaying ? "⏸ Pause" : "▶ Play";
    }

    private void ResetIdleButton_Click(object sender, RoutedEventArgs e)
    {
        PlayAnimation("idle");
        foreach (var child in AnimationButtonsWrapPanel.Children)
        {
            if (child is Button b && b.Tag is "idle")
            {
                HighlightAnimButton(b);
                break;
            }
        }
    }

    private void PreviewSpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewSpeedComboBox.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double spd))
        {
            _previewSpeedMultiplier = spd;
        }
    }

    private void SetActivePetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPack == null || !_selectedPack.IsValid) return;

        string newPet = _selectedPack.Info.Id;
        bool switched = false;

        if (App.CurrentApp != null)
        {
            switched = App.CurrentApp.TrySwitchPet(newPet);
        }
        else
        {
            _settings.ActivePet = newPet;
            _service.Save();
            switched = true;
        }

        if (switched)
        {
            foreach (var pack in _packItems)
            {
                pack.IsActive = string.Equals(pack.Info.Id, newPet, StringComparison.OrdinalIgnoreCase);
            }
            PacksListBox.Items.Refresh();

            UpdateActivePetIndicator();
            SetActivePetButton.IsEnabled = false;
            SetActivePetButton.Content = "✓ Active on Desktop";
            FooterStatusText.Text = $"Switched active desktop companion to {_selectedPack.Info.DisplayName}.";
        }
    }

    private void PersonalityPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (PersonalityPresetComboBox.SelectedItem is not ComboBoxItem item) return;

        string preset = item.Tag?.ToString() ?? "Expressive";
        _settings.PersonalityPreset = preset;
        PresetDescriptionText.Text = Personality.GetPresetDescription(preset);

        if (!preset.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            Personality.ApplyPreset(preset, _settings);

            _suppressEvents = true;
            EnableFocusReactionsCheckBox.IsChecked = _settings.EnableFocusReactions;
            EnableBreakReactionsCheckBox.IsChecked = _settings.EnableBreakReactions;
            EnablePettingReactionsCheckBox.IsChecked = _settings.EnablePettingReactions;
            EnableDevReactionsCheckBox.IsChecked = _settings.EnableDevBuildReactions && _settings.EnableDevTestReactions;
            _suppressEvents = false;
        }

        _service.Save();
        _onChanged?.Invoke();
        FooterStatusText.Text = $"Personality preset set to '{preset}'.";
    }

    private void ReactionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        // Customizing individual reactions switches the preset to Custom
        _suppressEvents = true;
        _settings.PersonalityPreset = "Custom";
        SelectPresetItem("Custom");
        PresetDescriptionText.Text = Personality.GetPresetDescription("Custom");
        _suppressEvents = false;

        _settings.EnableFocusReactions = EnableFocusReactionsCheckBox.IsChecked == true;
        _settings.EnableBreakReactions = EnableBreakReactionsCheckBox.IsChecked == true;
        _settings.EnablePettingReactions = EnablePettingReactionsCheckBox.IsChecked == true;

        bool devEnabled = EnableDevReactionsCheckBox.IsChecked == true;
        _settings.EnableDevBuildReactions = devEnabled;
        _settings.EnableDevTestReactions = devEnabled;

        _service.Save();
        _onChanged?.Invoke();
        FooterStatusText.Text = "Custom reaction settings saved.";
    }

    private void AccessibilityCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        _settings.ReducedMotion = ReducedMotionCheckBox.IsChecked == true;
        _settings.EnableQuietMode = QuietModeCheckBox.IsChecked == true;

        _service.Save();
        _onChanged?.Invoke();
        FooterStatusText.Text = "Accessibility settings updated.";
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents || SizeValueText == null || _settings == null || _service == null) return;
        double scale = Math.Round(SizeSlider.Value, 2);
        SizeValueText.Text = $"{scale:0.00}x";
        _settings.SizeScale = scale;
        _settings.Validate();

        _service.Save();
        _onChanged?.Invoke();
        FooterStatusText.Text = $"Size scale: {scale:0.00}x";
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents || SpeedValueText == null || _settings == null || _service == null) return;
        double speed = Math.Round(SpeedSlider.Value, 1);
        SpeedValueText.Text = $"{speed:0.0}x";
        _settings.Speed = speed;
        _settings.Validate();

        _service.Save();
        _onChanged?.Invoke();
        FooterStatusText.Text = $"Animation speed: {speed:0.0}x";
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents || OpacityValueText == null || _settings == null || _service == null) return;
        double opacity = Math.Round(OpacitySlider.Value, 2);
        OpacityValueText.Text = $"{Math.Round(opacity * 100)}%";
        _settings.Opacity = opacity;
        _settings.Validate();

        _service.Save();
        _onChanged?.Invoke();
        FooterStatusText.Text = $"Window opacity: {Math.Round(opacity * 100)}%";
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        Personality.RestoreDefaults(_settings);
        _service.Save();

        _suppressEvents = true;
        SizeSlider.Value = _settings.SizeScale;
        SizeValueText.Text = $"{_settings.SizeScale:0.00}x";

        SpeedSlider.Value = _settings.Speed;
        SpeedValueText.Text = $"{_settings.Speed:0.0}x";

        OpacitySlider.Value = _settings.Opacity;
        OpacityValueText.Text = $"{Math.Round(_settings.Opacity * 100)}%";

        SelectPresetItem(_settings.PersonalityPreset);
        PresetDescriptionText.Text = Personality.GetPresetDescription(_settings.PersonalityPreset);

        EnableFocusReactionsCheckBox.IsChecked = _settings.EnableFocusReactions;
        EnableBreakReactionsCheckBox.IsChecked = _settings.EnableBreakReactions;
        EnablePettingReactionsCheckBox.IsChecked = _settings.EnablePettingReactions;
        EnableDevReactionsCheckBox.IsChecked = _settings.EnableDevBuildReactions && _settings.EnableDevTestReactions;

        ReducedMotionCheckBox.IsChecked = _settings.ReducedMotion;
        QuietModeCheckBox.IsChecked = _settings.EnableQuietMode;
        _suppressEvents = false;

        _onChanged?.Invoke();
        FooterStatusText.Text = "Defaults restored successfully.";
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public sealed class CompanionPackItem
{
    public CompanionPackInfo Info { get; }
    public ImageSource? Preview { get; }
    public string Name => Info.DisplayName;
    public string BadgeText => Info.IsCustom ? "LOCAL" : "BUILT-IN";
    public bool IsLocal => Info.IsCustom;
    public bool IsValid => Info.IsValid;
    public string DetailsLine => $"{Info.AnimationCount} animations • {Info.CellWidth}x{Info.CellHeight}px";
    public bool IsActive { get; set; }

    public CompanionPackItem(CompanionPackInfo info, ImageSource? preview, bool isActive)
    {
        Info = info;
        Preview = preview;
        IsActive = isActive;
    }
}
