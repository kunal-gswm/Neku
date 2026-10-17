using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.Services;

namespace DesktopPet.UI;

public partial class FocusHudWindow : Window
{
    private readonly FocusTimerService _timer;
    private readonly DispatcherTimer _displayTimer;

    public FocusHudWindow(FocusTimerService timer)
    {
        InitializeComponent();
        _timer = timer;

        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _displayTimer.Tick += (_, _) => UpdateDisplay();

        _timer.StateChanged += OnStateChanged;

        PositionDefault();
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

    private void OnStateChanged(FocusTimerState state)
    {
        Dispatcher.InvokeAsync(UpdateDisplay);
    }

    public void UpdateDisplay()
    {
        if (!_timer.IsActive)
        {
            TimeText.Text = "00:00";
            ModeIcon.Text = "🍅";
            PauseButton.Content = "▶";
            return;
        }

        ModeIcon.Text = _timer.IsBreak ? "☕" : "🍅";
        TimeText.Text = _timer.FormattedRemaining;
        PauseButton.Content = _timer.IsPaused ? "▶" : "⏸";
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.IsPaused)
            _timer.Resume();
        else
            _timer.Pause();
        UpdateDisplay();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _displayTimer.Stop();
        Hide();
    }
}
