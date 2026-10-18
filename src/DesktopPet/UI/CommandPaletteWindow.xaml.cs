using System.Windows;
using System.Windows.Input;
using DesktopPet.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DesktopPet.UI;

/// <summary>
/// Centered keyboard-driven overlay command palette for fast navigation and action execution.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private readonly CommandPaletteService _service;
    private bool _isExecuting;

    public CommandPaletteWindow(CommandPaletteService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        InitializeComponent();
        RefreshResults();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            DesktopPet.Native.Win32.SetForegroundWindow(hwnd);
        }

        SearchInputBox.Focus();
        Keyboard.Focus(SearchInputBox);
        RefreshResults();
    }

    private void SearchInputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrEmpty(SearchInputBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        RefreshResults();
    }

    private void RefreshResults()
    {
        var results = _service.Search(SearchInputBox.Text);
        ResultsListBox.ItemsSource = results;

        bool hasResults = results.Count > 0;
        EmptyStatePanel.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;
        ResultsListBox.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;

        CommandCountText.Text = $"{results.Count} {(results.Count == 1 ? "command" : "commands")}";

        if (hasResults)
        {
            ResultsListBox.SelectedIndex = 0;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        // Escape or Ctrl+W closes the palette immediately from anywhere
        if (key == Key.Escape || (key == Key.W && modifiers == ModifierKeys.Control))
        {
            Close();
            e.Handled = true;
            return;
        }

        // Palette toggle key pressed while palette is open -> dismiss
        if (key == Key.P && (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) ||
                             modifiers == (ModifierKeys.Control | ModifierKeys.Shift) ||
                             modifiers == ModifierKeys.Control))
        {
            Close();
            e.Handled = true;
            return;
        }

        // If focus is currently in SearchInputBox, HandleSearchKeyDown will be called via SearchInputBox_PreviewKeyDown
        // BUT if focus is outside SearchInputBox (e.g. ListBox or border), handle it here
        if (!SearchInputBox.IsKeyboardFocused)
        {
            if (key == Key.Enter)
            {
                ExecuteSelected();
                e.Handled = true;
                return;
            }

            if ((modifiers == ModifierKeys.Control && key == Key.N) || (modifiers == ModifierKeys.None && key == Key.Down))
            {
                MoveSelection(1);
                e.Handled = true;
                return;
            }

            if ((modifiers == ModifierKeys.Control && key == Key.P) || (modifiers == ModifierKeys.None && key == Key.Up))
            {
                MoveSelection(-1);
                e.Handled = true;
                return;
            }

            if (key == Key.PageDown && modifiers == ModifierKeys.None)
            {
                MoveSelection(5);
                e.Handled = true;
                return;
            }

            if (key == Key.PageUp && modifiers == ModifierKeys.None)
            {
                MoveSelection(-5);
                e.Handled = true;
                return;
            }

            if (key == Key.Tab)
            {
                int delta = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1;
                MoveSelection(delta);
                e.Handled = true;
                return;
            }

            // Restore focus to SearchInputBox when user begins typing printable characters or backspace
            if (modifiers is ModifierKeys.None or ModifierKeys.Shift)
            {
                if (key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9) or (>= Key.NumPad0 and <= Key.NumPad9)
                    or Key.Space or Key.Back or Key.Delete or Key.OemMinus or Key.OemPlus or Key.OemPeriod or Key.OemQuestion)
                {
                    SearchInputBox.Focus();
                    Keyboard.Focus(SearchInputBox);
                    SearchInputBox.CaretIndex = SearchInputBox.Text.Length;
                }
            }
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = FindVisualChild<System.Windows.Controls.ScrollViewer>(ResultsListBox);
        if (sv != null)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - (e.Delta / 3.0));
            e.Handled = true;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Dismiss if the user clicks the outer transparent margin
        if (ReferenceEquals(e.OriginalSource, this))
        {
            Close();
        }
    }

    private void SearchInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        HandleSearchKeyDown(e.Key, Keyboard.Modifiers, e);
    }

    internal void HandleSearchKeyDown(Key key, ModifierKeys modifiers, KeyEventArgs? e = null)
    {
        // Ctrl+N / Ctrl+P navigation
        if (modifiers == ModifierKeys.Control)
        {
            if (key == Key.N)
            {
                MoveSelection(1);
                if (e != null) e.Handled = true;
                return;
            }
            if (key == Key.P)
            {
                MoveSelection(-1);
                if (e != null) e.Handled = true;
                return;
            }
            if (key == Key.W)
            {
                Close();
                if (e != null) e.Handled = true;
                return;
            }
        }

        switch (key)
        {
            case Key.Down:
                MoveSelection(1);
                if (e != null) e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                if (e != null) e.Handled = true;
                break;

            case Key.PageDown:
                MoveSelection(5);
                if (e != null) e.Handled = true;
                break;

            case Key.PageUp:
                MoveSelection(-5);
                if (e != null) e.Handled = true;
                break;

            case Key.Tab:
                // Tab / Shift+Tab cycles through commands while keeping focus in the search box
                int delta = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1;
                MoveSelection(delta);
                if (e != null) e.Handled = true;
                break;

            case Key.Enter:
                ExecuteSelected();
                if (e != null) e.Handled = true;
                break;

            case Key.Escape:
                Close();
                if (e != null) e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        int count = ResultsListBox.Items.Count;
        if (count == 0) return;

        int current = ResultsListBox.SelectedIndex;
        int next;

        if (Math.Abs(delta) == 1)
        {
            // Wraparound for single step
            next = (current + delta + count) % count;
        }
        else
        {
            // Clamp for page jumps
            next = Math.Clamp(current + delta, 0, count - 1);
        }

        ResultsListBox.SelectedIndex = next;
        ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
    }

    private void ResultsListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        if (dep == null) return;

        // CRITICAL FIX: If the user clicked on a scrollbar (thumb, track, repeat button), DO NOT execute!
        if (FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(dep) != null)
        {
            return;
        }

        // Only execute if an actual ListBoxItem was clicked
        var item = FindVisualParent<System.Windows.Controls.ListBoxItem>(dep);
        if (item != null && item.DataContext is PaletteCommand cmd)
        {
            ResultsListBox.SelectedItem = cmd;
            ExecuteSelected();
        }
    }

    private void ExecuteSelected()
    {
        if (_isExecuting) return;
        if (ResultsListBox.SelectedItem is not PaletteCommand cmd) return;

        _isExecuting = true;
        Close();

        // Dispatch execution immediately following window dismissal
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                _service.Execute(cmd);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[CommandPalette] Error executing command '{cmd.Id}': {ex.Message}");
            }
        });
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_isExecuting)
        {
            Close();
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var sub = FindVisualChild<T>(child);
            if (sub != null) return sub;
        }
        return null;
    }
}
