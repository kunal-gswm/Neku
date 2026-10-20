using System.Windows;
using System.Windows.Threading;
using DesktopPet.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace DesktopPet.UI;

public enum QuickCaptureMode
{
    Task,
    Note
}

public partial class QuickCaptureWindow : Window
{
    private readonly TaskService _taskService;
    private readonly NotesService _notesService;
    private QuickCaptureMode _mode = QuickCaptureMode.Task;
    private bool _isSaving = false;

    private readonly SolidColorBrush _activeBrush = new(Color.FromRgb(139, 92, 246));
    private readonly SolidColorBrush _inactiveBrush = new(Color.FromRgb(56, 47, 74));
    private readonly SolidColorBrush _successBrush = new(Color.FromRgb(34, 197, 94));
    private readonly SolidColorBrush _errorBrush = new(Color.FromRgb(239, 68, 68));

    public QuickCaptureMode CurrentMode => _mode;

    public QuickCaptureWindow(TaskService taskService, NotesService notesService, QuickCaptureMode initialMode = QuickCaptureMode.Task)
    {
        InitializeComponent();
        _taskService = taskService;
        _notesService = notesService;
        _mode = initialMode;
        UpdateModeUi();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InputBox.Focus();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            e.Handled = true;
            ExecuteSave();
        }
        else if (e.Key == Key.Tab && string.IsNullOrWhiteSpace(InputBox.Text))
        {
            e.Handled = true;
            SetMode(_mode == QuickCaptureMode.Task ? QuickCaptureMode.Note : QuickCaptureMode.Task);
        }
    }

    private void TaskModeBtn_Click(object sender, RoutedEventArgs e)
    {
        SetMode(QuickCaptureMode.Task);
        InputBox.Focus();
    }

    private void NoteModeBtn_Click(object sender, RoutedEventArgs e)
    {
        SetMode(QuickCaptureMode.Note);
        InputBox.Focus();
    }

    public void SetMode(QuickCaptureMode mode)
    {
        _mode = mode;
        UpdateModeUi();
    }

    private void UpdateModeUi()
    {
        if (_mode == QuickCaptureMode.Task)
        {
            TaskModeBtn.Background = _activeBrush;
            TaskModeBtn.Foreground = Brushes.White;
            NoteModeBtn.Background = _inactiveBrush;
            NoteModeBtn.Foreground = new SolidColorBrush(Color.FromRgb(243, 232, 255));
            FeedbackText.Text = "Creating Task due Today • Enter to save • Esc to cancel";
        }
        else
        {
            NoteModeBtn.Background = _activeBrush;
            NoteModeBtn.Foreground = Brushes.White;
            TaskModeBtn.Background = _inactiveBrush;
            TaskModeBtn.Foreground = new SolidColorBrush(Color.FromRgb(243, 232, 255));
            FeedbackText.Text = "Creating Quick Note • Enter to save • Shift+Enter for newline";
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteSave();
    }

    public bool ExecuteSave()
    {
        if (_isSaving) return false;

        string text = InputBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            FeedbackText.Text = "⚠️ Please enter text before saving.";
            FeedbackText.Foreground = _errorBrush;
            InputBox.Focus();
            return false;
        }

        _isSaving = true;
        try
        {
            if (_mode == QuickCaptureMode.Task)
            {
                _taskService.Create(title: text, dueDate: DateTime.Today);
                FeedbackText.Text = "✓ Task saved!";
            }
            else
            {
                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string title = lines.Length > 0 ? lines[0] : "Untitled Note";
                if (title.Length > 60) title = title[..60] + "…";
                _notesService.Create(title: title, content: text);
                FeedbackText.Text = "✓ Note saved!";
            }

            FeedbackText.Foreground = _successBrush;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Close();
            };
            timer.Start();
            return true;
        }
        catch (Exception ex)
        {
            FeedbackText.Text = $"⚠️ Save error: {ex.Message}";
            FeedbackText.Foreground = _errorBrush;
            _isSaving = false;
            return false;
        }
    }
}

