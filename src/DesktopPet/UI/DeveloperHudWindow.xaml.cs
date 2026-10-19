using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPet.Native;
using DesktopPet.Services;
using DesktopPet.Services.DeskSense;
using DesktopPet.Services.Radar;

namespace DesktopPet.UI;

public partial class DeveloperHudWindow : Window
{
    private readonly AppSettings _settings;
    private readonly DeveloperRadarService _radar;
    private readonly Action? _onOpenRadar;
    private readonly DesktopPet.Services.Health.SystemHealthService? _health;

    public DeveloperHudWindow(AppSettings settings, DeveloperRadarService radar, Action? onOpenRadar = null, DesktopPet.Services.Health.SystemHealthService? health = null)
    {
        InitializeComponent();
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _radar = radar ?? throw new ArgumentNullException(nameof(radar));
        _onOpenRadar = onOpenRadar;
        _health = health;

        _radar.RadarStateChanged += OnRadarStateChanged;
        if (_health != null)
        {
            _health.HealthUpdated += OnHealthUpdated;
        }

        Loaded += (_, _) =>
        {
            PositionWindow();
            UpdateDisplay();
        };

        SetupContextMenu();
        UpdateDisplay();

        Closed += (_, _) =>
        {
            _radar.RadarStateChanged -= OnRadarStateChanged;
            if (_health != null)
            {
                _health.HealthUpdated -= OnHealthUpdated;
            }
        };
    }

    private void OnHealthUpdated(DesktopPet.Services.Health.SystemHealthSnapshot snapshot)
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateDisplay();
        }
        else
        {
            Dispatcher.InvokeAsync(UpdateDisplay);
        }
    }

    public void RefreshDisplay()
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateDisplay();
        }
        else
        {
            Dispatcher.InvokeAsync(UpdateDisplay);
        }
    }

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_NOACTIVATE);

            var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;

        if (!double.IsNaN(_settings.DeveloperHudX) && !double.IsNaN(_settings.DeveloperHudY))
        {
            Left = Math.Clamp(_settings.DeveloperHudX, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
            Top = Math.Clamp(_settings.DeveloperHudY, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        }
        else
        {
            Left = workArea.Right - Width - 24;
            Top = workArea.Bottom - Height - 128;
        }
    }

    private void SetupContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openRadarItem = new System.Windows.Controls.MenuItem { Header = "📡  Open Developer Radar..." };
        openRadarItem.Click += (_, _) => _onOpenRadar?.Invoke();

        var openExplorerItem = new System.Windows.Controls.MenuItem { Header = "📂  Open Repository in Explorer" };
        openExplorerItem.Click += (_, _) => _radar.OpenInExplorer();

        var copyPathItem = new System.Windows.Controls.MenuItem { Header = "📋  Copy Repository Path" };
        copyPathItem.Click += (_, _) => _radar.CopyRepositoryPath();

        var refreshItem = new System.Windows.Controls.MenuItem { Header = "🔄  Refresh Status" };
        refreshItem.Click += async (_, _) => await _radar.RepoMonitor.RefreshAllAsync();

        var hideItem = new System.Windows.Controls.MenuItem { Header = "✕  Hide Developer HUD" };
        hideItem.Click += (_, _) => CloseButton_Click(this, new RoutedEventArgs());

        menu.Items.Add(openRadarItem);
        menu.Items.Add(openExplorerItem);
        menu.Items.Add(copyPathItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(hideItem);

        ContextMenu = menu;
    }

    private void OnRadarStateChanged()
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateDisplay();
        }
        else
        {
            Dispatcher.InvokeAsync(UpdateDisplay);
        }
    }

    public void UpdateDisplay()
    {
        var ctx = _radar.CurrentContext;

        // 1. Context Icon & Category
        (string icon, string label) = ctx.Category switch
        {
            DeskSenseAppCategory.Coding => ("💻", !string.IsNullOrWhiteSpace(ctx.ProcessName) ? ctx.ProcessName : "Coding"),
            DeskSenseAppCategory.Terminal => ("⌨️", !string.IsNullOrWhiteSpace(ctx.ProcessName) ? ctx.ProcessName : "Terminal"),
            DeskSenseAppCategory.Browser => ("🌐", "Browser"),
            DeskSenseAppCategory.Meeting => ("👥", "Meeting"),
            DeskSenseAppCategory.Idle => ("💤", "Idle"),
            _ => ("🐾", "Desktop")
        };

        ContextIconText.Text = icon;
        ContextCategoryText.Text = label;

        // 2. Repository & Branch
        var repo = _radar.GetSelectedRepository();
        if (repo != null)
        {
            string dirtyMark = repo.HasUncommittedChanges ? "*" : "";
            RepoBranchText.Text = $"{repo.Name} ({repo.Branch}{dirtyMark})";
            RepoBranchText.ToolTip = $"Path: {repo.Path}\nStatus: {repo.UncommittedSummary}";
        }
        else
        {
            RepoBranchText.Text = "No repo monitored";
            RepoBranchText.ToolTip = "Add a repository in Developer Radar";
        }

        // 3. Build Status
        var build = _radar.GetLatestBuildRecord();
        if (build != null)
        {
            BuildBadge.Visibility = Visibility.Visible;
            if (build.Status == BuildTestResultStatus.Success)
            {
                BuildBadgeText.Text = "✔ Pass";
                BuildBadgeText.Foreground = (SolidColorBrush)FindResource("AccentGreenBrush");
            }
            else if (build.Status == BuildTestResultStatus.Failure)
            {
                BuildBadgeText.Text = "✖ Fail";
                BuildBadgeText.Foreground = (SolidColorBrush)FindResource("AccentRedBrush");
            }
            else
            {
                BuildBadgeText.Text = "● Build";
                BuildBadgeText.Foreground = (SolidColorBrush)FindResource("AccentOrangeBrush");
            }
            BuildBadge.ToolTip = $"{build.Source}: {build.Message} ({build.FormattedTime})";
        }
        else
        {
            BuildBadge.Visibility = Visibility.Collapsed;
        }

        // 4. Agent Sessions
        int activeAgents = _radar.GetActiveAgentSessionCount();
        if (activeAgents > 0)
        {
            AgentBadge.Visibility = Visibility.Visible;
            AgentBadgeText.Text = $"🤖 {activeAgents}";
            AgentBadge.ToolTip = $"{activeAgents} active AI coding agent session(s)";
        }
        else
        {
            AgentBadge.Visibility = Visibility.Collapsed;
        }

        // 5. Focus Timer
        string focusSummary = _radar.GetFocusTimerSummary();
        if (focusSummary != "Idle")
        {
            FocusTimerText.Visibility = Visibility.Visible;
            FocusTimerText.Text = focusSummary;
        }
        else
        {
            FocusTimerText.Visibility = Visibility.Collapsed;
        }

        // 6. System Health Pill
        if (_settings.EnableHealthHud && _health != null)
        {
            var snap = _health.CurrentSnapshot;
            HealthBadge.Visibility = Visibility.Visible;
            HealthBadgeText.Text = $"💻 {snap.CpuPercent}% | 🧠 {snap.MemoryPercent}%";
            HealthBadge.ToolTip = $"System Health:\nCPU Load: {snap.CpuPercent}%\nRAM Usage: {snap.MemoryPercent}% ({snap.UsedMemoryGb} / {snap.TotalMemoryGb} GB)";
        }
        else
        {
            HealthBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            _settings.DeveloperHudX = Left;
            _settings.DeveloperHudY = Top;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.EnableDeveloperHud = false;
        Hide();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
        {
            CloseButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            App.CurrentApp?.ToggleCommandPalette();
            e.Handled = true;
            return;
        }
    }
}
