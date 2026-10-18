using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.Services;

namespace DesktopPet.UI;

public partial class FocusHudWindow : Window
{
    private readonly FocusTimerService _timer;
    private readonly Action? _onToggle;
    private readonly Action? _onCancel;
    private readonly DispatcherTimer _displayTimer;

    public FocusHudWindow(FocusTimerService timer, Action? onToggle = null, Action? onCancel = null)
    {
        InitializeComponent();
        _timer = timer;
        _onToggle = onToggle;
        _onCancel = onCancel;

        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _displayTimer.Tick += (_, _) => UpdateDisplay();

        _timer.StateChanged += OnStateChanged;

        PositionDefault();
        SetupContextMenu();
        UpdateDisplay();
        if (IsVisible) _displayTimer.Start();

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                UpdateDisplay();
                _displayTimer.Start();
            }
            else
            {
                _displayTimer.Stop();
            }
        };

        Closed += (_, _) =>
        {
            _displayTimer.Stop();
            _timer.StateChanged -= OnStateChanged;
        };
    }

    private void PositionDefault()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 80;
    }

    private void SetupContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var toggleItem = new System.Windows.Controls.MenuItem();
        var cancelItem = new System.Windows.Controls.MenuItem { Header = "Cancel Focus Session" };
        var hideItem = new System.Windows.Controls.MenuItem { Header = "Hide HUD" };

        toggleItem.Click += (_, _) => HandleToggle();
        cancelItem.Click += (_, _) => HandleCancel();
        hideItem.Click += (_, _) => CloseButton_Click(this, new RoutedEventArgs());

        menu.Opened += (_, _) =>
        {
            toggleItem.Header = !_timer.IsActive
                ? "Start Focus Session"
                : (_timer.IsPaused ? "Resume Focus Session" : "Pause Focus Session");
            cancelItem.Visibility = _timer.IsActive ? Visibility.Visible : Visibility.Collapsed;
        };

        menu.Items.Add(toggleItem);
        menu.Items.Add(cancelItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(hideItem);

        ContextMenu = menu;
    }

    private void OnStateChanged(FocusTimerState state)
    {
        Dispatcher.InvokeAsync(UpdateDisplay);
    }

    public void UpdateDisplay()
    {
        if (!_timer.IsActive)
        {
            int defaultMins = _timer.FocusDurationMinutes > 0 ? _timer.FocusDurationMinutes : 25;
            TimeText.Text = $"{defaultMins:D2}:00";
            ModeIcon.Text = "🍅";
            PauseButton.Content = "▶";
            PauseButton.ToolTip = "Start Focus Session";
            ToolTip = !string.IsNullOrWhiteSpace(_timer.ActiveTaskTitle)
                ? $"Ready to focus on: {_timer.ActiveTaskTitle} (Click ▶ to start)"
                : "Focus Session (Click ▶ to start)";
            return;
        }

        ModeIcon.Text = _timer.IsBreak ? "☕" : "🍅";
        TimeText.Text = _timer.FormattedRemaining;
        PauseButton.Content = _timer.IsPaused ? "▶" : "⏸";
        PauseButton.ToolTip = _timer.IsPaused ? "Resume Session" : "Pause Session";
        ToolTip = !string.IsNullOrWhiteSpace(_timer.ActiveTaskTitle)
            ? $"Focusing on: {_timer.ActiveTaskTitle}"
            : (_timer.IsBreak ? "Break Time" : "Focus Session");
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void HandleToggle()
    {
        if (_onToggle != null)
        {
            _onToggle();
        }
        else
        {
            if (_timer.IsPaused)
                _timer.Resume();
            else if (_timer.IsActive)
                _timer.Pause();
            else
                _timer.StartFocus();
        }
        UpdateDisplay();
    }

    private void HandleCancel()
    {
        if (_onCancel != null)
            _onCancel();
        else
            _timer.Cancel();
        UpdateDisplay();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        HandleToggle();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _displayTimer.Stop();
        Hide();
    }
}
