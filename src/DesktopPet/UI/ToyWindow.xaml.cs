using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DesktopPet.Engine;
using DesktopPet.Native;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace DesktopPet.UI;

public partial class ToyWindow : Window
{
    private readonly ToyItem _toy;
    private readonly ToyService _toyService;
    private bool _isDragging;
    private Point _dragStartPoint;
    private Point _windowStartPoint;

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    public ToyItem Toy => _toy;

    public ToyWindow(ToyItem toy, ToyService toyService)
    {
        InitializeComponent();
        _toy = toy;
        _toyService = toyService;

        Left = toy.X;
        Top = toy.Y;
        Width = toy.Width;
        Height = toy.Height;

        EmojiText.Text = toy.Emoji;
        Title = $"Toy: {toy.DisplayName}";

        // Context Menu
        var menu = new ContextMenu();
        var removeItem = new MenuItem { Header = $"Remove {toy.DisplayName}" };
        removeItem.Click += (_, _) => CloseToy();
        var clearAllItem = new MenuItem { Header = "Clear All Toys" };
        clearAllItem.Click += (_, _) => _toyService.ClearAll();

        menu.Items.Add(removeItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(clearAllItem);
        ContextMenu = menu;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_NOACTIVATE);

            var source = HwndSource.FromHwnd(hwnd);
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

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        DismissBtn.Visibility = Visibility.Visible;
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            DismissBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = PointToScreen(e.GetPosition(this));
        _windowStartPoint = new Point(Left, Top);
        CaptureMouse();
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            Point currentScreen = PointToScreen(e.GetPosition(this));
            double deltaX = currentScreen.X - _dragStartPoint.X;
            double deltaY = currentScreen.Y - _dragStartPoint.Y;

            var desktop = DesktopGeometry.Capture(1.0);
            var bounds = desktop.Bounds;
            double newX = Math.Clamp(_windowStartPoint.X + deltaX, bounds.Left, bounds.Right - Width);
            double newY = Math.Clamp(_windowStartPoint.Y + deltaY, bounds.Top, bounds.Bottom - Height);

            Left = newX;
            Top = newY;

            _toyService.UpdateToyPosition(_toy.Id, Left, Top);
            e.Handled = true;
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            if (!IsMouseOver)
            {
                DismissBtn.Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        }
    }

    private void DismissBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseToy();
    }

    public void CloseToy()
    {
        _toyService.RemoveToy(_toy.Id);
        Close();
    }
}
