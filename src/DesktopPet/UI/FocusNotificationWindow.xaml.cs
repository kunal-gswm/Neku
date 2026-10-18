using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.Native;

namespace DesktopPet.UI;

public partial class FocusNotificationWindow : Window
{
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly Action? _onAction;

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

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
        {
            _autoCloseTimer.Stop();
            Close();
            e.Handled = true;
        }
    }

    public FocusNotificationWindow(string title, string subtitle, string icon, string actionLabel, Action? onAction = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubText.Text = subtitle;
        IconText.Text = icon;
        ActionButton.Content = actionLabel;
        _onAction = onAction;

        PositionAtBottomRight();

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Close();
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

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();
        _onAction?.Invoke();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();
    }
}
