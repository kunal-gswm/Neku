using System.Windows;
using System.Windows.Input;
using DesktopPet.Engine;
using DesktopPet.Services;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Clipboard = System.Windows.Clipboard;

namespace DesktopPet.UI;

public partial class DropzoneActionWindow : Window
{
    private readonly DropzonePayload _payload;
    private readonly NotesService? _notesService;
    private readonly TaskService? _taskService;
    private readonly Action<string>? _onActionPerformed;

    public DropzoneActionWindow(
        DropzonePayload payload,
        double petX,
        double petY,
        double petWidth,
        double petHeight,
        NotesService? notesService,
        TaskService? taskService,
        Action<string>? onActionPerformed = null,
        string defaultAction = "Prompt")
    {
        InitializeComponent();
        _payload = payload;
        _notesService = notesService;
        _taskService = taskService;
        _onActionPerformed = onActionPerformed;

        // Populate payload details
        PreviewTextBlock.Text = payload.PreviewText;

        switch (payload.Type)
        {
            case DropPayloadType.Files:
                TypeEmojiText.Text = "📁";
                TypeBadgeText.Text = $"{payload.FilePaths.Count} Files";
                break;
            case DropPayloadType.Link:
                TypeEmojiText.Text = "🔗";
                TypeBadgeText.Text = "Link";
                break;
            case DropPayloadType.Text:
            default:
                TypeEmojiText.Text = "📝";
                TypeBadgeText.Text = "Text";
                break;
        }

        // Position popover near pet (clamped to monitor work area)
        PositionNearPet(petX, petY, petWidth, petHeight);

        Loaded += (_, _) =>
        {
            Activate();
            if (string.Equals(defaultAction, "Task", StringComparison.OrdinalIgnoreCase))
            {
                CreateTaskBtn.Focus();
            }
            else
            {
                SaveNoteBtn.Focus();
            }
        };

        // Dismiss without action if clicking outside
        Deactivated += (_, _) => Close();
    }

    private void PositionNearPet(double petX, double petY, double petWidth, double petHeight)
    {
        var desktop = DesktopGeometry.Capture(1.0);
        var work = desktop.WorkAreaAt(petX);

        double targetX = petX + (petWidth - Width) / 2;
        double targetY = petY - Height - 12;

        if (targetY < work.Top + 10)
        {
            targetY = petY + petHeight + 12;
        }

        Left = Math.Clamp(targetX, work.Left + 10, work.Right - Width - 10);
        Top = Math.Clamp(targetY, work.Top + 10, work.Bottom - Height - 10);
    }

    private void SaveNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        _notesService?.Create(_payload.Title, _payload.Content);
        _onActionPerformed?.Invoke($"Saved note: {_payload.Title}");
        Close();
    }

    private void CreateTaskBtn_Click(object sender, RoutedEventArgs e)
    {
        _taskService?.Create(_payload.Title, description: _payload.Content);
        _onActionPerformed?.Invoke($"Created task: {_payload.Title}");
        Close();
    }

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_payload.Content);
            _onActionPerformed?.Invoke("Copied to clipboard! 📋");
        }
        catch { }
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
