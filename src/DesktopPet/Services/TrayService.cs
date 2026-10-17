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
    private readonly ToolStripMenuItem _quickNoteItem;
    private readonly ToolStripMenuItem _devEventsItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _hideItem;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly ToolStripMenuItem _reducedMotionItem;
    private readonly ToolStripMenuItem _quietModeItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _aiItem;
    private readonly ToolStripMenuItem _talkItem;
    private readonly Action _onUpdate;

    public TrayService(
        Action onFocusToggle, Func<string> getFocusLabel,
        Action onFocusCancel, Func<bool> isFocusActive,
        Action onQuickNote,
        Action onDeveloperEvents,
        Action onSettings,
        Action<bool> onPauseToggled, Action<bool> onHideToggled,
        Action<bool> onClickThroughToggled, Action<bool> onReducedMotionToggled,
        Action<bool> onQuietModeToggled, Action onResetPosition, Action onQuit,
        Action onUpdate, Action<bool> onAiToggled, Action onTalk, AppSettings settings)
    {
        _onUpdate = onUpdate;
        var menu = new ContextMenuStrip();

        _focusItem = new ToolStripMenuItem(getFocusLabel());
        _focusItem.Click += (_, _) => onFocusToggle();

        _focusCancelItem = new ToolStripMenuItem("Cancel Focus Session");
        _focusCancelItem.Click += (_, _) => onFocusCancel();

        _quickNoteItem = new ToolStripMenuItem("Quick Note…");
        _quickNoteItem.Click += (_, _) => onQuickNote();

        _devEventsItem = new ToolStripMenuItem("Developer Events…");
        _devEventsItem.Click += (_, _) => onDeveloperEvents();

        var settingsItem = new ToolStripMenuItem("Character and Behavior Settings…");
        settingsItem.Click += (_, _) => onSettings();

        _pauseItem = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        _pauseItem.Click += (_, _) => onPauseToggled(_pauseItem.Checked);

        _hideItem = new ToolStripMenuItem("Hide") { CheckOnClick = true };
        _hideItem.Click += (_, _) => onHideToggled(_hideItem.Checked);

        _clickThroughItem = new ToolStripMenuItem("Click-through mode") { CheckOnClick = true, Checked = settings.ClickThrough };
        _clickThroughItem.Click += (_, _) => onClickThroughToggled(_clickThroughItem.Checked);

        var resetPosItem = new ToolStripMenuItem("Reset position");
        resetPosItem.Click += (_, _) => onResetPosition();

        _quietModeItem = new ToolStripMenuItem("Quiet mode") { CheckOnClick = true, Checked = settings.EnableQuietMode };
        _quietModeItem.Click += (_, _) => onQuietModeToggled(_quietModeItem.Checked);

        _reducedMotionItem = new ToolStripMenuItem("Reduced motion") { CheckOnClick = true, Checked = settings.ReducedMotion };
        _reducedMotionItem.Click += (_, _) => onReducedMotionToggled(_reducedMotionItem.Checked);

        _aiItem = new ToolStripMenuItem("AI companion") { CheckOnClick = true, Checked = settings.EnableAiCompanion };
        _talkItem = new ToolStripMenuItem("Talk to Neku…") { Enabled = settings.EnableAiCompanion };
        _aiItem.Click += (_, _) => { onAiToggled(_aiItem.Checked); _talkItem.Enabled = _aiItem.Checked; };
        _talkItem.Click += (_, _) => onTalk();

        _updateItem = new ToolStripMenuItem("Update available — install now") { Visible = false };
        _updateItem.Click += (_, _) => onUpdate();

        var quitItem = new ToolStripMenuItem("Exit");
        quitItem.Click += (_, _) => onQuit();

        menu.Opening += (_, _) =>
        {
            _focusItem.Text = getFocusLabel();
            _focusCancelItem.Visible = isFocusActive();
            _pauseItem.Text = _pauseItem.Checked ? "Resume" : "Pause";
        };

        // 1. Focus Session
        menu.Items.Add(_focusItem);
        menu.Items.Add(_focusCancelItem);
        menu.Items.Add(new ToolStripSeparator());

        // 2. Quick Note & Developer Events
        menu.Items.Add(_quickNoteItem);
        menu.Items.Add(_devEventsItem);
        menu.Items.Add(settingsItem);
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
        menu.Items.Add(_aiItem);
        menu.Items.Add(_talkItem);
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
        _icon.DoubleClick += (_, _) => onSettings();
    }


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

    /// <summary>Reflect the AI-companion on/off state in the tray (kept in sync with the Settings window).</summary>
    public void SetAiEnabled(bool enabled)
    {
        _aiItem.Checked   = enabled;
        _talkItem.Enabled = enabled;
    }

    /// <summary>Reveal the "Update now" menu item and pop a balloon inviting the update.</summary>
    public void ShowUpdateAvailable()
    {
        _updateItem.Visible = true;
        _icon.BalloonTipTitle = "Neku update available";
        _icon.BalloonTipText  = "A newer version is ready. Click here or the tray menu to install.";
        _icon.BalloonTipIcon  = ToolTipIcon.Info;
        _icon.BalloonTipClicked -= OnBalloonClicked;
        _icon.BalloonTipClicked += OnBalloonClicked;
        _icon.ShowBalloonTip(8000);
    }

    private void OnBalloonClicked(object? sender, EventArgs e) => _onUpdate();

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
