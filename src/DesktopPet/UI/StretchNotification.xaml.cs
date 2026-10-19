using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.Native;

namespace DesktopPet.UI;

public partial class StretchNotification : Window
{
    private readonly DispatcherTimer _autoDismiss;

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
            _autoDismiss.Stop();
            Close();
            e.Handled = true;
        }
    }

    public StretchNotification()
    {
        InitializeComponent();

        // Position at top-center of primary monitor work area
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + 24;

        _autoDismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _autoDismiss.Tick += (_, _) => { _autoDismiss.Stop(); Close(); };
        _autoDismiss.Start();

        // Slide-in animation via opacity
        Opacity = 0;
        Loaded += (_, _) => FadeIn();
    }

    private void FadeIn()
    {
        int tick = 0;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        t.Tick += (_, _) =>
        {
            Opacity = Math.Min(1, tick / 12.0);
            if (++tick >= 12) t.Stop();
        };
        t.Start();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Close();
}
