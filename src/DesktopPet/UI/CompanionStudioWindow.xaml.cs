using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.Engine;
using DesktopPet.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using MessageBox = System.Windows.MessageBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DesktopPet.UI;

public partial class CompanionStudioWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _service;
    private readonly Action? _onChanged;

    private readonly List<CompanionPackItem> _allPackItems = new();
    private readonly List<CompanionPackItem> _filteredPackItems = new();
    private CompanionPackItem? _selectedPack;

    private SpriteAnimator? _previewAnimator;
    private readonly DispatcherTimer _previewTimer;
    private DateTime _lastTickTime;
    private double _previewSpeedMultiplier = 1.0;
    private bool _isPlaying = true;
    private bool _suppressEvents;
    private Button? _selectedAnimButton;

    private string _searchQuery = "";
    private string _selectedCategory = "All";

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
        CompanionPackService.PacksChanged += OnPacksChangedExternally;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        DesktopPet.Native.Win32.EnableImmersiveDarkMode(handle);
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
        CompanionPackService.PacksChanged -= OnPacksChangedExternally;
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

    private void OnPacksChangedExternally()
    {
        Dispatcher.InvokeAsync(PopulateLibrary);
    }

    private void PopulateLibrary()
    {
        _allPackItems.Clear();
        var packs = AssetSource.DiscoverPacks();

        foreach (var pack in packs)
        {
            ImageSource? preview = AssetSource.GetPackPreview(pack.Id);
            bool isActive = string.Equals(pack.Id, _settings.ActivePet, StringComparison.OrdinalIgnoreCase);

            var item = new CompanionPackItem(pack, preview, isActive);
            _allPackItems.Add(item);
        }

        UpdateActivePetIndicator();
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        _filteredPackItems.Clear();
        string q = _searchQuery.Trim().ToLowerInvariant();

        foreach (var item in _allPackItems)
        {
            // Category check
            if (_selectedCategory != "All")
            {
                if (_selectedCategory == "Mascot" && !string.Equals(item.Info.Category, "Mascot", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_selectedCategory == "Imported" && !string.Equals(item.Info.Category, "Imported", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_selectedCategory == "Personal" && !string.Equals(item.Info.Category, "Personal", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_selectedCategory == "Legacy" && !string.Equals(item.Info.Category, "Legacy Cats", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Search query check
            if (!string.IsNullOrEmpty(q))
            {
                bool match = item.Name.ToLowerInvariant().Contains(q) ||
                             item.Info.Id.ToLowerInvariant().Contains(q) ||
                             (item.Info.Category?.ToLowerInvariant().Contains(q) ?? false) ||
                             (item.Info.Author?.ToLowerInvariant().Contains(q) ?? false) ||
                             (item.Info.Description?.ToLowerInvariant().Contains(q) ?? false);
                if (!match) continue;
            }

            _filteredPackItems.Add(item);
        }

        PacksListBox.ItemsSource = null;
        PacksListBox.ItemsSource = _filteredPackItems;

        // Try re-selecting currently selected pack or first available
        var toSelect = _filteredPackItems.FirstOrDefault(p => _selectedPack != null && p.Info.Id == _selectedPack.Info.Id)
                       ?? _filteredPackItems.FirstOrDefault(p => p.IsActive)
                       ?? _filteredPackItems.FirstOrDefault();

        if (toSelect != null)
        {
            PacksListBox.SelectedItem = toSelect;
        }
        else
        {
            _selectedPack = null;
            UpdateSelectedCardUI();
        }
    }

    private void UpdateActivePetIndicator()
    {
        string activeName = AssetSource.GetDefaultDisplayName(_settings.ActivePet);
        var activePack = _allPackItems.FirstOrDefault(p => string.Equals(p.Info.Id, _settings.ActivePet, StringComparison.OrdinalIgnoreCase));
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
        UpdateSelectedCardUI();
        LoadPreviewAnimator(item.Info.Id);
    }

    private void UpdateSelectedCardUI()
    {
        if (_selectedPack == null)
        {
            SelectedPackTitleText.Text = "No companion selected";
            SelectedPackCategoryBadge.Text = "NONE";
            SelectedPackMetaText.Visibility = Visibility.Collapsed;
            SelectedPackDescriptionText.Text = "Select a character from the library or import a new one.";
            SelectedPackDimensionsText.Text = "--";
            SelectedPackScaleText.Text = "--";
            SelectedPackStatusText.Text = "--";
            SetActivePetButton.IsEnabled = false;
            SetActivePetButton.Content = "Set as Desktop Companion";
            DeletePackButton.Visibility = Visibility.Collapsed;
            OpenFolderButton.Visibility = Visibility.Collapsed;
            PackErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        var item = _selectedPack;
        SelectedPackTitleText.Text = item.Info.DisplayName;
        SelectedPackCategoryBadge.Text = item.BadgeText;
        SelectedPackCategoryBadge.Background = item.BadgeBrush;

        // Meta line (Author, License, Version)
        var metaParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Info.Author)) metaParts.Add($"By {item.Info.Author}");
        if (!string.IsNullOrWhiteSpace(item.Info.Version)) metaParts.Add($"v{item.Info.Version}");
        if (!string.IsNullOrWhiteSpace(item.Info.License)) metaParts.Add($"License: {item.Info.License}");

        if (metaParts.Count > 0)
        {
            SelectedPackMetaText.Text = string.Join("  •  ", metaParts);
            SelectedPackMetaText.Visibility = Visibility.Visible;
        }
        else
        {
            SelectedPackMetaText.Visibility = Visibility.Collapsed;
        }

        SelectedPackDescriptionText.Text = item.Info.Description ?? "No description available.";
        SelectedPackDimensionsText.Text = $"{item.Info.CellWidth}x{item.Info.CellHeight} px";
        SelectedPackScaleText.Text = $"Scale: {item.Info.Scale:0.#}x";
        SelectedPackStatusText.Text = item.Info.Category;

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

        // Action buttons
        DeletePackButton.Visibility = item.IsRemovable ? Visibility.Visible : Visibility.Collapsed;
        OpenFolderButton.Visibility = !string.IsNullOrEmpty(item.DiskDirectory) && Directory.Exists(item.DiskDirectory)
            ? Visibility.Visible
            : Visibility.Collapsed;
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

    private static readonly Brush InactiveBtnBg = new SolidColorBrush(Color.FromArgb(255, 40, 35, 55));
    private static readonly Brush InactiveBtnBorder = new SolidColorBrush(Color.FromArgb(255, 72, 60, 102));
    private static readonly Brush ActiveBtnBg = new SolidColorBrush(Color.FromArgb(255, 139, 92, 246));
    private static readonly Brush ActiveBtnBorder = new SolidColorBrush(Color.FromArgb(255, 159, 117, 255));

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

        bool looping = _previewAnimator.Manifest.Animations.TryGetValue(animName, out var def) && def.Loop;
        PlayingAnimationNameText.Text = $"Animation: {animName}";
        PlayingAnimationInfoText.Text = looping ? "Looping" : "One-shot (holds final frame)";
    }

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        if (_previewAnimator == null) return;

        DateTime now = DateTime.UtcNow;
        double dt = (now - _lastTickTime).TotalSeconds * _previewSpeedMultiplier;
        _lastTickTime = now;

        if (_isPlaying && dt > 0)
        {
            var frame = _previewAnimator.Tick(dt);
            StudioPreviewImage.Source = frame;

            if (_previewAnimator.Finished)
            {
                PlayingAnimationInfoText.Text = "Finished (holding last frame)";
            }
        }
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        _isPlaying = !_isPlaying;
        PlayPauseButton.Content = _isPlaying ? "⏸ Pause" : "▶ Play";
        _lastTickTime = DateTime.UtcNow;
    }

    private void ResetIdleButton_Click(object sender, RoutedEventArgs e)
    {
        PlayAnimation("idle");
        foreach (var child in AnimationButtonsWrapPanel.Children)
        {
            if (child is Button b && string.Equals(b.Tag?.ToString(), "idle", StringComparison.OrdinalIgnoreCase))
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
            foreach (var pack in _allPackItems)
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

    private void ImportZipButton_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Companion Pack (ZIP Archive)",
            Filter = "Companion Pack Archive (*.zip)|*.zip|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (ofd.ShowDialog(this) == true)
        {
            var result = CompanionPackService.ImportZip(ofd.FileName, overwrite: true);
            HandleImportResult(result);
        }
    }

    private void ImportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Companion Pack Directory (containing manifest.json and spritesheet)"
        };

        if (dlg.ShowDialog(this) == true)
        {
            var result = CompanionPackService.ImportFolder(dlg.FolderName, overwrite: true);
            HandleImportResult(result);
        }
    }

    private void HandleImportResult(PackImportResult result)
    {
        if (result.Success && result.PackId != null)
        {
            int anims = result.Manifest?.Animations?.Count ?? 0;
            string msg = $"Successfully imported '{result.DisplayName ?? result.PackId}' ({anims} animations)!\n\nWould you like to activate this companion on your desktop now?";
            var answer = MessageBox.Show(this, msg, "Companion Imported", MessageBoxButton.YesNo, MessageBoxImage.Information);

            PopulateLibrary();

            var imported = _allPackItems.FirstOrDefault(p => p.Info.Id == result.PackId);
            if (imported != null)
            {
                PacksListBox.SelectedItem = imported;
            }

            if (answer == MessageBoxResult.Yes)
            {
                App.CurrentApp?.TrySwitchPet(result.PackId);
                UpdateActivePetIndicator();
                UpdateSelectedCardUI();
            }
        }
        else
        {
            MessageBox.Show(this, $"Failed to import companion pack:\n\n{result.ErrorMessage}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeletePackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPack == null || !_selectedPack.IsRemovable) return;

        string packName = _selectedPack.Name;
        string packId = _selectedPack.Info.Id;

        var confirm = MessageBox.Show(this,
            $"Are you sure you want to remove '{packName}'?\n\nThis will delete its companion folder from your local AppData.",
            "Remove Companion Pack",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        bool wasActive = string.Equals(_settings.ActivePet, packId, StringComparison.OrdinalIgnoreCase);
        bool deleted = CompanionPackService.DeletePack(packId);

        if (deleted)
        {
            if (wasActive)
            {
                App.CurrentApp?.TrySwitchPet("neku");
            }

            PopulateLibrary();
            FooterStatusText.Text = $"Removed companion '{packName}'.";
        }
        else
        {
            MessageBox.Show(this, "Could not delete companion pack. Please verify file permissions.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedPack?.DiskDirectory) && Directory.Exists(_selectedPack.DiskDirectory))
        {
            try
            {
                var psi = new ProcessStartInfo("explorer.exe")
                {
                    UseShellExecute = false
                };
                psi.ArgumentList.Add(_selectedPack.DiskDirectory);
                Process.Start(psi);
            }
            catch { }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchBox.Text;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Collapsed : Visibility.Visible;
        ApplyFilters();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void CategoryFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryFilterComboBox.SelectedItem is ComboBoxItem item)
        {
            _selectedCategory = item.Tag?.ToString() ?? "All";
            ApplyFilters();
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
    }

    private void ReactionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        _settings.EnableFocusReactions = EnableFocusReactionsCheckBox.IsChecked == true;
        _settings.EnableBreakReactions = EnableBreakReactionsCheckBox.IsChecked == true;
        _settings.EnablePettingReactions = EnablePettingReactionsCheckBox.IsChecked == true;

        bool dev = EnableDevReactionsCheckBox.IsChecked == true;
        _settings.EnableDevBuildReactions = dev;
        _settings.EnableDevTestReactions = dev;

        // Custom preset when individual checkboxes toggle
        _settings.PersonalityPreset = "Custom";
        _suppressEvents = true;
        SelectPresetItem("Custom");
        PresetDescriptionText.Text = Personality.GetPresetDescription("Custom");
        _suppressEvents = false;

        _service.Save();
        _onChanged?.Invoke();
    }

    private void AccessibilityCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        _settings.ReducedMotion = ReducedMotionCheckBox.IsChecked == true;
        _settings.EnableQuietMode = QuietModeCheckBox.IsChecked == true;

        _service.Save();
        _onChanged?.Invoke();
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        double val = Math.Round(e.NewValue, 2);
        _settings.SizeScale = val;
        SizeValueText.Text = $"{val:0.00}x";

        _service.Save();
        _onChanged?.Invoke();
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        double val = Math.Round(e.NewValue, 1);
        _settings.Speed = val;
        SpeedValueText.Text = $"{val:0.0}x";

        _service.Save();
        _onChanged?.Invoke();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        double val = Math.Round(e.NewValue, 2);
        _settings.Opacity = val;
        OpacityValueText.Text = $"{Math.Round(val * 100)}%";

        _service.Save();
        _onChanged?.Invoke();
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        _suppressEvents = true;

        _settings.SizeScale = 1.0;
        _settings.Speed = 1.0;
        _settings.Opacity = 1.0;
        _settings.PersonalityPreset = "Expressive";
        _settings.ReducedMotion = false;
        _settings.EnableQuietMode = false;
        Personality.ApplyPreset("Expressive", _settings);

        LoadSettingsValues();
        _suppressEvents = false;

        _service.Save();
        _onChanged?.Invoke();
        FooterStatusText.Text = "Defaults restored successfully.";
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.I && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                ImportFolderButton_Click(this, new RoutedEventArgs());
            else
                ImportZipButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F5 || (e.Key == Key.R && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
        {
            PopulateLibrary();
            FooterStatusText.Text = "Refreshed companion library.";
            e.Handled = true;
        }
    }
}

/// <summary>
/// UI view model for displaying a companion pack in Companion Studio.
/// </summary>
public sealed class CompanionPackItem
{
    public CompanionPackInfo Info { get; }
    public ImageSource? Preview { get; }
    public string Name => Info.DisplayName;
    public string BadgeText => Info.Category.ToUpperInvariant();
    public bool IsLocal => Info.IsCustom;
    public bool IsValid => Info.IsValid;
    public bool IsRemovable => Info.IsRemovable;
    public string? DiskDirectory => Info.DiskDirectory;

    public Brush BadgeBrush => Info.Category.ToLowerInvariant() switch
    {
        "mascot" => new SolidColorBrush(Color.FromArgb(255, 139, 92, 246)),     // Purple
        "personal" => new SolidColorBrush(Color.FromArgb(255, 16, 185, 129)),   // Emerald Green
        "imported" => new SolidColorBrush(Color.FromArgb(255, 59, 130, 246)),   // Blue
        "anime" => new SolidColorBrush(Color.FromArgb(255, 236, 72, 153)),      // Pink
        "cartoon" => new SolidColorBrush(Color.FromArgb(255, 245, 158, 11)),    // Amber
        "legacy cats" => new SolidColorBrush(Color.FromArgb(255, 72, 60, 102)), // Muted
        _ => new SolidColorBrush(Color.FromArgb(255, 55, 48, 74))
    };

    public string DetailsLine
    {
        get
        {
            string line = $"{Info.AnimationCount} anims • {Info.CellWidth}x{Info.CellHeight}px";
            if (!string.IsNullOrWhiteSpace(Info.Author))
            {
                line = $"by {Info.Author} • " + line;
            }
            return line;
        }
    }

    public bool IsActive { get; set; }

    public CompanionPackItem(CompanionPackInfo info, ImageSource? preview, bool isActive)
    {
        Info = info;
        Preview = preview;
        IsActive = isActive;
    }
}
