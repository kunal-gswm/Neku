using System.Windows;
using System.Windows.Threading;
using DesktopPet.Native;
using DesktopPet.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace DesktopPet.UI;

public partial class ReminderNotificationWindow : Window
{
    private readonly TaskItem? _task;
    private readonly IReadOnlyList<TaskItem>? _consolidatedTasks;
    private readonly TaskReminderService? _reminderService;
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly Action? _customAction;

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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
        {
            _autoCloseTimer.Stop();
            DismissButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    /// <summary>
    /// Standard single task reminder notification.
    /// </summary>
    public ReminderNotificationWindow(TaskItem task, TaskReminderService reminderService, bool isMissed = false, int defaultSnoozeMinutes = 15)
    {
        InitializeComponent();
        _task = task;
        _reminderService = reminderService;

        TitleText.Text = task.Title;
        string timeStr = task.ReminderAt.HasValue ? task.ReminderAt.Value.ToLocalTime().ToString("t") : "";
        SubText.Text = isMissed
            ? $"⚠️ Missed reminder (scheduled for {timeStr})"
            : $"⏰ Scheduled reminder ({timeStr})";

        SnoozeButton.Content = $"💤 Snooze ({defaultSnoozeMinutes}m)";

        PositionAtBottomRight();

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Close();
            if (_task != null && _reminderService != null)
            {
                _reminderService.SnoozeReminder(_task.Id, defaultSnoozeMinutes);
            }
        };
        _autoCloseTimer.Start();
        Closed += (_, _) => _autoCloseTimer.Stop();
    }

    /// <summary>
    /// Consolidated reminder notification for multiple tasks.
    /// </summary>
    public ReminderNotificationWindow(
        IReadOnlyList<TaskItem> tasks,
        TaskReminderService reminderService,
        Action onOpenTasks)
    {
        InitializeComponent();
        _consolidatedTasks = tasks;
        _reminderService = reminderService;
        _customAction = onOpenTasks;

        int count = tasks.Count;
        TitleText.Text = $"{count} Missed Reminders";
        SubText.Text = count <= 2
            ? string.Join(", ", tasks.Select(t => t.Title))
            : $"{string.Join(", ", tasks.Take(2).Select(t => t.Title))}, and {count - 2} more";
        IconText.Text = "📋";
        CompleteButton.Content = "Review Tasks 📋";
        CompleteButton.ToolTip = "Open the Tasks window to review and manage overdue tasks";
        System.Windows.Automation.AutomationProperties.SetName(CompleteButton, "Review Tasks");
        CompleteButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 92, 246));
        SnoozeButton.Content = "💤 Snooze All (15m)";
        SnoozeButton.ToolTip = "Postpone all represented reminders by 15 minutes";
        System.Windows.Automation.AutomationProperties.SetName(SnoozeButton, "Snooze All Reminders");
        SnoozeButton.Visibility = Visibility.Visible;
        DismissButton.ToolTip = "Dismiss notification without modifying tasks";
        System.Windows.Automation.AutomationProperties.SetName(DismissButton, "Dismiss notification");

        PositionAtBottomRight();

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Close();
            if (_consolidatedTasks != null && _reminderService != null)
            {
                foreach (var t in _consolidatedTasks)
                {
                    _reminderService.SnoozeReminder(t.Id, 15);
                }
            }
        };
        _autoCloseTimer.Start();
        Closed += (_, _) => _autoCloseTimer.Stop();
    }

    /// <summary>
    /// Legacy consolidated notification constructor for generic actions.
    /// </summary>
    public ReminderNotificationWindow(string title, string subtitle, string actionLabel, Action onAction)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubText.Text = subtitle;
        IconText.Text = "📋";
        CompleteButton.Content = actionLabel;
        CompleteButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 92, 246));
        SnoozeButton.Visibility = Visibility.Collapsed;
        _customAction = onAction;

        PositionAtBottomRight();

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Close();
            if (_consolidatedTasks != null && _reminderService != null)
            {
                foreach (var t in _consolidatedTasks)
                {
                    _reminderService.SnoozeReminder(t.Id, 15);
                }
            }
        };
        _autoCloseTimer.Start();
        Closed += (_, _) => _autoCloseTimer.Stop();
    }

    private void PositionAtBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();

        if (_customAction != null)
        {
            _customAction.Invoke();
        }
        else if (_task != null && _reminderService != null)
        {
            _reminderService.CompleteTask(_task.Id);
        }
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();

        if (_consolidatedTasks != null && _reminderService != null)
        {
            foreach (var t in _consolidatedTasks)
            {
                _reminderService.SnoozeReminder(t.Id, 15);
            }
        }
        else if (_task != null && _reminderService != null)
        {
            _reminderService.SnoozeReminder(_task.Id, 15);
        }
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();

        if (_consolidatedTasks != null && _reminderService != null)
        {
            foreach (var t in _consolidatedTasks)
            {
                _reminderService.DismissReminder(t.Id, persistTask: false);
            }
        }
        else if (_task != null && _reminderService != null)
        {
            _reminderService.DismissReminder(_task.Id);
        }
    }
}
