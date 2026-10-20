using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DesktopPet.Services;

/// <summary>System-tray icon with Settings / Pause / Hide / Reset / Exit menu.</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _focusItem;
    private readonly ToolStripMenuItem _focusCancelItem;
    private readonly ToolStripMenuItem _tasksItem;
    private readonly ToolStripMenuItem _paletteItem;
    private readonly ToolStripMenuItem _quickNoteItem;
    private readonly ToolStripMenuItem _devEventsItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _hideItem;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly ToolStripMenuItem _reducedMotionItem;
    private readonly ToolStripMenuItem _quietModeItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly Action _onUpdate;
    private readonly AppSettings _settings;
    private readonly ToolStripMenuItem _devSubmenu;
    private readonly ToolStripMenuItem? _radarItem;
    private readonly ToolStripMenuItem? _hudItem;
    private readonly ToolStripMenuItem? _focusHudItem;
    private readonly ToolStripMenuItem _quickCaptureItem;

    public bool IsDevSubmenuVisible => _devSubmenu.Available;
    public bool IsDevEventsVisible => _devEventsItem.Available;
    public bool IsRadarVisible => _radarItem?.Available ?? false;
    public bool IsHudVisible => _hudItem?.Available ?? false;
    public bool IsFocusHudVisible => _focusHudItem?.Available ?? false;
    private readonly Action _onDoubleClick;

    public TrayService(
        Action onFocusToggle, Func<string> getFocusLabel,
        Action onFocusCancel, Func<bool> isFocusActive,
        Action onCommandPalette,
        Action onProductivityHub,
        Action onTasks,
        Action onQuickNote,
        Action onDeveloperEvents,
        Action onCompanionStudio,
        Action onSettings,
        Action<bool> onPauseToggled, Action<bool> onHideToggled,
        Action<bool> onClickThroughToggled, Action<bool> onReducedMotionToggled,
        Action<bool> onQuietModeToggled, Action onResetPosition, Action onQuit,
        Action onUpdate, AppSettings settings,
        Action? onDeveloperRadar = null, Action? onToggleDeveloperHud = null,
        Action? onToggleFocusHud = null, Action? onQuickCapture = null, Action? onDoubleClick = null)
    {
        _onUpdate = onUpdate;
        _settings = settings;
        var menu = new ContextMenuStrip();

        _paletteItem = new ToolStripMenuItem("⚡ Command Palette… (Ctrl+Alt+P)");
        _paletteItem.Click += (_, _) => onCommandPalette();

        var hubItem = new ToolStripMenuItem("🐾 Productivity Hub…") { Font = new Font(menu.Font, FontStyle.Bold) };
        hubItem.Click += (_, _) => onProductivityHub();

        _focusItem = new ToolStripMenuItem(getFocusLabel());
        _focusItem.Click += (_, _) => onFocusToggle();

        _focusCancelItem = new ToolStripMenuItem("Cancel Focus Session");
        _focusCancelItem.Click += (_, _) => onFocusCancel();
        if (onToggleFocusHud != null)
        {
            _focusHudItem = new ToolStripMenuItem("Toggle Focus HUD");
            _focusHudItem.Click += (_, _) => onToggleFocusHud();
        }

        _tasksItem = new ToolStripMenuItem("Tasks…");
        _tasksItem.Click += (_, _) => onTasks();

        _quickCaptureItem = new ToolStripMenuItem("⚡ Quick Capture… (Ctrl+Alt+C)");
        _quickCaptureItem.Click += (_, _) => onQuickCapture?.Invoke();

        _quickNoteItem = new ToolStripMenuItem("Quick Note…");
        _quickNoteItem.Click += (_, _) => onQuickNote();

        _devEventsItem = new ToolStripMenuItem("Developer Events…");
        _devEventsItem.Click += (_, _) => onDeveloperEvents();

        if (onDeveloperRadar != null)
        {
            _radarItem = new ToolStripMenuItem("📡 Developer Radar…");
            _radarItem.Click += (_, _) => onDeveloperRadar();
        }

        if (onToggleDeveloperHud != null)
        {
            _hudItem = new ToolStripMenuItem("🪟 Toggle Developer HUD");
            _hudItem.Click += (_, _) => onToggleDeveloperHud();
        }

        _devSubmenu = new ToolStripMenuItem("🛠️ Developer Tools");
        _devSubmenu.DropDownItems.Add(_devEventsItem);
        if (_radarItem != null) _devSubmenu.DropDownItems.Add(_radarItem);
        if (_hudItem != null) _devSubmenu.DropDownItems.Add(_hudItem);

        var studioItem = new ToolStripMenuItem("🎨 Companion Studio…");
        studioItem.Click += (_, _) => onCompanionStudio();

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => onSettings();

        _pauseItem = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        _pauseItem.Click += (_, _) => onPauseToggled(_pauseItem.Checked);

        _hideItem = new ToolStripMenuItem("Hide") { CheckOnClick = true };
        _hideItem.Click += (_, _) => onHideToggled(_hideItem.Checked);

        _clickThroughItem = new ToolStripMenuItem("Click-Through Mode") { CheckOnClick = true, Checked = settings.ClickThrough };
        _clickThroughItem.Click += (_, _) => onClickThroughToggled(_clickThroughItem.Checked);

        var resetPosItem = new ToolStripMenuItem("Reset Position");
        resetPosItem.Click += (_, _) => onResetPosition();

        _quietModeItem = new ToolStripMenuItem("Quiet Mode") { CheckOnClick = true, Checked = settings.EnableQuietMode };
        _quietModeItem.Click += (_, _) => onQuietModeToggled(_quietModeItem.Checked);

        _reducedMotionItem = new ToolStripMenuItem("Reduced Motion") { CheckOnClick = true, Checked = settings.ReducedMotion };
        _reducedMotionItem.Click += (_, _) => onReducedMotionToggled(_reducedMotionItem.Checked);

        _updateItem = new ToolStripMenuItem("Update available — install now") { Visible = false };
        _updateItem.Click += (_, _) => onUpdate();

        var quitItem = new ToolStripMenuItem("Exit");
        quitItem.Click += (_, _) => onQuit();

        menu.Opening += (_, _) =>
        {
            _focusItem.Text = getFocusLabel();
            _focusCancelItem.Visible = isFocusActive();
            _pauseItem.Text = _pauseItem.Checked ? "Resume" : "Pause";
            RefreshDeveloperMenuVisibility();
        };

        RefreshDeveloperMenuVisibility();

        // 1. Productivity Hub, Focus Session & Tasks

        menu.Items.Add(hubItem);
        menu.Items.Add(_paletteItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(_focusItem);
        menu.Items.Add(_focusCancelItem);
        if (_focusHudItem != null) menu.Items.Add(_focusHudItem);
        menu.Items.Add(_tasksItem);
        menu.Items.Add(_quickNoteItem);
        menu.Items.Add(_quickCaptureItem);
        menu.Items.Add(new ToolStripSeparator());

        // 3. Companion Studio, Settings & Developer Tools
        menu.Items.Add(studioItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(_devSubmenu);
        menu.Items.Add(new ToolStripSeparator());

        // 3. Pause & Hide
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_hideItem);
        menu.Items.Add(_clickThroughItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(_quietModeItem);
        menu.Items.Add(_reducedMotionItem);
        menu.Items.Add(resetPosItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_updateItem);
        menu.Items.Add(quitItem);

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "Neku",
            ContextMenuStrip = menu
        };
        _onDoubleClick = onDoubleClick ?? onProductivityHub;
        _icon.DoubleClick += (_, _) => _onDoubleClick();
    }

    internal void SimulateDoubleClick() => _onDoubleClick();


    /// <summary>Open a URL in the user's default browser. Best-effort — never throws into the UI.</summary>
    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* if no browser is available, silently do nothing */ }
    }

    private static Icon LoadIcon()
    {
        try
        {
            using var s = AssetSource.TryOpen("Assets/app.ico");
            if (s != null) return new Icon(s);
        }
        catch { /* fall through */ }
        return SystemIcons.Application;
    }

    public void SetPaused(bool paused) => _pauseItem.Checked = paused;
    public void SetHidden(bool hidden) => _hideItem.Checked = hidden;
    public void SetClickThrough(bool enabled) => _clickThroughItem.Checked = enabled;
    public void SetReducedMotion(bool enabled) => _reducedMotionItem.Checked = enabled;
    public void SetQuietMode(bool enabled) => _quietModeItem.Checked = enabled;

    /// <summary>Show that a download is in flight, and stop a second click starting another.</summary>
    public void SetUpdateBusy(bool busy)
    {
        _updateItem.Enabled = !busy;
        _updateItem.Text = busy ? "Downloading update…" : "Update available — install now";
    }


    /// <summary>Reveal the "Update now" menu item and pop a balloon inviting the update.</summary>
    public void ShowUpdateAvailable()
    {
        _updateItem.Visible = true;
        _icon.BalloonTipTitle = "Neku update available";
        _icon.BalloonTipText = "A newer version is ready. Click here or the tray menu to install.";
        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.BalloonTipClicked -= OnBalloonClicked;
        _icon.BalloonTipClicked += OnBalloonClicked;
        _icon.ShowBalloonTip(8000);
    }

    private void OnBalloonClicked(object? sender, EventArgs e) => _onUpdate();

    public void UpdatePaletteShortcut(string? shortcutLabel)
    {
        if (string.IsNullOrWhiteSpace(shortcutLabel))
        {
            _paletteItem.Text = "⚡ Command Palette…";
        }
        else
        {
            _paletteItem.Text = $"⚡ Command Palette… ({shortcutLabel})";
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
    public void RefreshDeveloperMenuVisibility()
    {
        bool showEvents = _settings.EnableDeveloperEvents;
        bool showRadar = _radarItem != null && _settings.EnableDeveloperRadar;
        bool showHud = _hudItem != null && (_settings.EnableDeveloperHud || _settings.EnableDeveloperRadar);

        _devEventsItem.Visible = showEvents;
        if (_radarItem != null) _radarItem.Visible = showRadar;
        if (_hudItem != null) _hudItem.Visible = showHud;

        _devSubmenu.Visible = showEvents || showRadar || showHud;
    }
}
