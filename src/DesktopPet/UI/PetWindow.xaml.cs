using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using VerticalAlignment = System.Windows.VerticalAlignment;
using System.Windows.Controls;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.Engine;
using DesktopPet.Native;

namespace DesktopPet.UI;

public partial class PetWindow : Window, IPetView
{
    private PetEngine? _engine;
    private readonly Stopwatch _clock = new();
    private double _lastSeconds;
    private readonly Random _rng = new();

    public PetWindow()
    {
        InitializeComponent();
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>
    /// The engine's DIP scale: the PRIMARY monitor's, not this window's. With PerMonitorV2 the
    /// window's own DPI flips as it crosses onto a differently-scaled monitor, and dividing by
    /// that re-maps every coordinate mid-walk (the cat leapt from one screen's edge to the
    /// middle of the next). One fixed scale keeps the engine in a single coordinate space.
    /// </summary>
    public double DpiScale => Win32.PrimaryMonitorScale();

    // Last position the engine asked for, in engine units. WPF's Left/Top can't be trusted for
    // this: they're expressed in whichever DPI the window is currently on.
    private double _left = double.NaN, _top = double.NaN;
    private int _physX = int.MinValue, _physY = int.MinValue;

    public double PetLeft => _left;
    public double PetTop  => _top;

    /// <summary>
    /// Keep the cat the same physical size on every monitor. WPF enlarges content by each
    /// monitor's scale, so on a 125% screen the window would outgrow the size the engine plans
    /// around — feet sinking into the taskbar and the far edge hanging off-screen. Scaling the
    /// content by engineScale / windowScale cancels that out.
    /// </summary>
    private void SyncContentScale()
    {
        if (Content is not FrameworkElement root) return;
        double k = DpiScale / VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (root.LayoutTransform is ScaleTransform st && Math.Abs(st.ScaleX - k) < 1e-6) return;
        root.LayoutTransform = Math.Abs(k - 1) < 1e-6 ? Transform.Identity : new ScaleTransform(k, k);
    }

    protected override void OnDpiChanged(System.Windows.DpiScale oldDpi, System.Windows.DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        SyncContentScale();
        // Windows just moved us to its own "suggested" rect for the new DPI; put the cat back
        // exactly where the engine has it.
        _physX = _physY = int.MinValue;
        if (!double.IsNaN(_left)) PlaceAt(_left, _top);
    }

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = Handle;
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

    private bool _renderLoopRunning;


    public void Attach(PetEngine engine, double w, double h)
    {
        _engine = engine;
        SetPetSize(w, h);

        _clock.Start();
        _lastSeconds = _clock.Elapsed.TotalSeconds;
        SetRenderLoopEnabled(true);
    }

    /// <summary>
    /// Start or stop the per-frame loop.
    ///
    /// Capping the tick rate inside the handler saves very little, because subscribing to
    /// CompositionTarget.Rendering makes WPF composite every frame whether or not we do any
    /// work in it. Detaching the handler is what actually lets the app go idle, so pausing
    /// really does stop the cost rather than just skipping the simulation.
    /// </summary>
    public void SetRenderLoopEnabled(bool enabled)
    {
        if (enabled == _renderLoopRunning) return;
        _renderLoopRunning = enabled;

        if (enabled)
        {
            // Don't let the paused interval arrive as one huge dt.
            _lastSeconds = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    public void SetPetSize(double w, double h)
    {
        PetImage.Width  = w;
        PetImage.Height = h;
        FlipTransform.CenterX = w / 2;
        HeatFlip.CenterX = w / 2;
    }

    /// <summary>
    /// Simulation tick budget, for very high refresh-rate displays where the engine would
    /// otherwise run several times more often than anything can show. 0 follows the compositor.
    ///
    /// This is NOT a power optimisation, despite appearances: skipping work inside the handler
    /// saves almost nothing, because the cost is being subscribed to Rendering at all. Use
    /// <see cref="SetRenderLoopEnabled"/> for that.
    /// </summary>
    public int TargetFps { get; set; }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_engine == null) return;
        double now = _clock.Elapsed.TotalSeconds;

        int fps = TargetFps;
        if (fps > 0)
        {
            // Skip this compositor frame if the budget has not elapsed. dt then accumulates,
            // so the simulation still advances by real time rather than running slow.
            double minStep = 1.0 / fps;
            if (now - _lastSeconds < minStep) return;
        }

        double dt = Math.Min(now - _lastSeconds, 0.1);
        _lastSeconds = now;
        if (dt <= 0) return;
        _engine.Update(dt);
    }

    public void SetPetVisible(bool visible)
    {
        // Collapsing the image rather than hiding the window keeps the HWND alive, which the
        // global hotkey is registered against.
        PetImage.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RedOverlay.Visibility = PetImage.Visibility;
        if (!visible)
        {
            Effects?.ClearSpeechBubble();
            Effects?.ClearToiletPaper();
        }
    }

    // ── IPetView ──────────────────────────────────────────────────────────────

    public void Render(ImageSource frame, double left, double top, int facing)
    {
        PetImage.Source = frame;
        double sx = facing >= 0 ? 1 : -1;
        FlipTransform.ScaleX = sx;
        // Keep the heat overlay aligned with the current frame and facing.
        HeatMaskBrush.ImageSource = frame;
        HeatFlip.ScaleX = sx;
        PlaceAt(left, top);
    }

    private void PlaceAt(double left, double top)
    {
        _left = left;
        _top  = top;

        IntPtr hwnd = Handle;
        if (hwnd == IntPtr.Zero)
        {
            Left = left;
            Top  = top;
            return;
        }

        SyncContentScale();
        double s = DpiScale;
        int x = (int)Math.Round(left * s), y = (int)Math.Round(top * s);
        if (x == _physX && y == _physY) return;
        _physX = x;
        _physY = y;
        Win32.MoveWindowPhysical(hwnd, x, y);
    }

    public void SetHeatLevel(double level)
    {
        // Red "hot" flush, clipped to the cat silhouette by the OpacityMask. Capped well
        // below full so the cat's features stay readable through the tint.
        RedOverlay.Opacity = Math.Clamp(level, 0, 1) * 0.55;
    }

    /// <summary>Persistent overlay that renders heart/sparkle particles. Set by App.</summary>
    public EffectsOverlay? Effects { get; set; }

    public void SpawnHearts()
    {
        // Hearts float up from just above the cat's head. Drawn on the shared overlay —
        // NOT as new windows (creating windows in the render loop throws + is costly).
        Effects?.Burst(_left + PetImage.Width / 2, _top);
    }

    public void DrawToiletPaper(double catLeft, double catTop, double catW, double catH, double length)
        => Effects?.DrawToiletPaper(catLeft, catTop, catW, catH, length);

    public void ClearToiletPaper()
        => Effects?.ClearToiletPaper();

    public void DropPebble(double screenX, double screenY)
        => Effects?.DropPebble(screenX, screenY);

    public void DrawSpeechBubble(double catLeft, double catTop, double catW, double catH, string text)
        => Effects?.DrawSpeechBubble(catLeft, catTop, catW, catH, text);

    public void ClearSpeechBubble()
        => Effects?.ClearSpeechBubble();

    /// <summary>Raised when the user taps the cat and the AI companion is enabled — App opens the chat box.</summary>
    public event Action? ChatRequested;

    /// <summary>Raised whenever user clicks or interacts with the pet (used to dismiss onboarding).</summary>
    public event Action? PetInteracted;

    /// <summary>When true, a tap on the cat opens the AI chat box (in addition to the happy boop).</summary>
    public bool AiTapOpensChat { get; set; }


    // ── Mouse events ──────────────────────────────────────────────────────────

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _engine?.BeginPet();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsMouseCaptured)
            _engine?.EndPet();
    }

    private System.Windows.Point _downPos;
    private bool _pressing;
    private bool _dragging;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        PetInteracted?.Invoke();
        // Don't start a drag yet — wait to see if this is a tap (boop) or a drag.
        _downPos  = PointToScreen(e.GetPosition(this));
        _pressing = true;
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        PetInteracted?.Invoke();
    }


    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressing && !_dragging)
        {
            var p = PointToScreen(e.GetPosition(this));
            if (Math.Abs(p.X - _downPos.X) + Math.Abs(p.Y - _downPos.Y) > 8)
            {
                _dragging = true;
                _engine?.BeginDrag();   // movement => pick it up
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        if (_dragging)
            _engine?.EndDrag();
        else
        {
            _engine?.Boop();            // tap with no movement => boop
            if (AiTapOpensChat) ChatRequested?.Invoke();
        }
        _pressing = false;
        _dragging = false;
        if (IsMouseOver)
            _engine?.BeginPet();
        e.Handled = true;
    }

    /// <summary>Toggle click-through mode using Win32 extended window styles.</summary>
    public void SetClickThrough(bool enabled)
    {
        IntPtr hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        if (enabled)
            Win32.MakeClickThrough(hwnd);
        else
            Win32.RemoveClickThrough(hwnd);
    }

    /// <summary>Configures the right-click context menu on the pet window.</summary>
    public void SetupContextMenu(
        Action onFocusToggle, Func<string> getFocusLabel,
        Action onFocusCancel, Func<bool> isFocusActive,
        Action onToggleFocusHud,
        Action onQuickNote,
        Action onDeveloperEvents,
        Action onCompanionStudio,
        Action onSettings,
        Action onTogglePause, Func<bool> isPaused,
        Action onHide,
        Action onToggleClickThrough, Func<bool> isClickThrough,
        Action onToggleReducedMotion, Func<bool> isReducedMotion,
        Action onToggleQuietMode, Func<bool> isQuietMode,
        Action onResetPosition,
        Action onExit)
    {
        var menu = new ContextMenu();

        // 1. Focus Session
        var focusItem = new MenuItem { Header = getFocusLabel() };
        focusItem.Click += (_, _) => onFocusToggle();

        var focusCancelItem = new MenuItem { Header = "Cancel Focus Session" };
        focusCancelItem.Click += (_, _) => onFocusCancel();

        var focusHudItem = new MenuItem { Header = "Toggle Focus HUD" };
        focusHudItem.Click += (_, _) => onToggleFocusHud();

        // 2. Quick Note
        var quickNoteItem = new MenuItem { Header = "Quick Note…" };
        quickNoteItem.Click += (_, _) => onQuickNote();

        // 3. Developer Events
        var devEventsItem = new MenuItem { Header = "Developer Events…" };
        devEventsItem.Click += (_, _) => onDeveloperEvents();

        // 4. Companion Studio & Settings
        var studioItem = new MenuItem { Header = "🎨 Companion Studio…" };
        studioItem.Click += (_, _) => onCompanionStudio();

        var settingsItem = new MenuItem { Header = "Character and Behavior Settings…" };
        settingsItem.Click += (_, _) => onSettings();

        // 5. Pause / Resume
        var pauseItem = new MenuItem { Header = "Pause", IsCheckable = true };
        pauseItem.Click += (_, _) => onTogglePause();

        // 6. Hide
        var hideItem = new MenuItem { Header = "Hide" };
        hideItem.Click += (_, _) => onHide();

        // Optional convenience items
        var clickThroughItem = new MenuItem { Header = "Click-Through Mode", IsCheckable = true };
        clickThroughItem.Click += (_, _) => onToggleClickThrough();

        var reducedMotionItem = new MenuItem { Header = "Reduced Motion", IsCheckable = true };
        reducedMotionItem.Click += (_, _) => onToggleReducedMotion();

        var quietModeItem = new MenuItem { Header = "Quiet Mode", IsCheckable = true };
        quietModeItem.Click += (_, _) => onToggleQuietMode();

        var resetPosItem = new MenuItem { Header = "Reset Position" };
        resetPosItem.Click += (_, _) => onResetPosition();

        // 7. Exit
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => onExit();

        menu.Opened += (_, _) =>
        {
            focusItem.Header = getFocusLabel();
            bool active = isFocusActive();
            focusCancelItem.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            pauseItem.IsChecked = isPaused();
            pauseItem.Header = isPaused() ? "Resume" : "Pause";
            clickThroughItem.IsChecked = isClickThrough();
            reducedMotionItem.IsChecked = isReducedMotion();
            quietModeItem.IsChecked = isQuietMode();
        };

        menu.Items.Add(focusItem);
        menu.Items.Add(focusCancelItem);
        menu.Items.Add(focusHudItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(quickNoteItem);
        menu.Items.Add(devEventsItem);
        menu.Items.Add(studioItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(pauseItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(clickThroughItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(reducedMotionItem);
        menu.Items.Add(quietModeItem);
        menu.Items.Add(resetPosItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        ContextMenu = menu;
    }

}
