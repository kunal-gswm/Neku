using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopPet.Services;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace DesktopPet.UI;

public partial class QuickNotesWindow : Window
{
    private readonly NotesService _notesService;
    private NoteItem? _currentNote;
    private bool _isDirty;
    private bool _loadingNote;

    public sealed class NoteDisplayItem
    {
        public NoteItem Note { get; }
        public string Title => string.IsNullOrWhiteSpace(Note.Title) ? "Untitled" : Note.Title;
        public string FormattedDate => Note.UpdatedAt.ToLocalTime().ToString("MMM d, h:mm tt");

        public NoteDisplayItem(NoteItem note)
        {
            Note = note;
        }
    }

    public QuickNotesWindow(NotesService notesService)
    {
        InitializeComponent();
        _notesService = notesService;

        RefreshNotesList();

        if (_notesService.Notes.Count > 0)
        {
            SelectNote(_notesService.Notes[0]);
        }
        else
        {
            CreateNewNote();
        }

        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(ContentBox.Text)) TitleBox.Focus();
            else ContentBox.Focus();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        DesktopPet.Native.Win32.EnableImmersiveDarkMode(handle);
    }

    private void RefreshNotesList(string? filter = null)
    {
        string term = filter?.Trim().ToLowerInvariant() ?? "";
        var items = _notesService.Notes
            .Where(n => string.IsNullOrEmpty(term) ||
                        n.Title.ToLowerInvariant().Contains(term) ||
                        n.Content.ToLowerInvariant().Contains(term))
            .Select(n => new NoteDisplayItem(n))
            .ToList();

        NotesListBox.ItemsSource = items;
        if (EmptyNotesBorder != null)
        {
            EmptyNotesBorder.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_currentNote != null)
        {
            var match = items.FirstOrDefault(i => i.Note.Id == _currentNote.Id);
            if (match != null)
            {
                NotesListBox.SelectedItem = match;
            }
        }
    }

    private void SelectNote(NoteItem note)
    {
        if (_isDirty)
        {
            SaveCurrentNote();
        }

        _loadingNote = true;
        _currentNote = note;
        TitleBox.Text = note.Title;
        ContentBox.Text = note.Content;
        _isDirty = false;
        _loadingNote = false;

        StatusText.Text = $"Last saved: {note.UpdatedAt.ToLocalTime():h:mm:ss tt}";
    }

    private void SaveCurrentNote()
    {
        if (_currentNote == null) return;

        string title = TitleBox.Text.Trim();
        string content = ContentBox.Text;

        _notesService.Update(_currentNote.Id, title, content);
        _isDirty = false;
        StatusText.Text = $"Saved at {DateTime.Now:h:mm:ss tt}";

        // Refresh list to update title/date
        string filter = SearchBox.Text;
        RefreshNotesList(filter);
    }

    /// <summary>Creates a new note and focuses the title field for immediate editing.</summary>
    public void CreateNewNote()
    {
        if (_isDirty)
        {
            SaveCurrentNote();
        }

        var newNote = _notesService.Create("New Note", "");
        RefreshNotesList(SearchBox.Text);
        SelectNote(newNote);
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void DeleteCurrentNote()
    {
        if (_currentNote == null) return;

        var res = MessageBox.Show(
            $"Delete note \"{_currentNote.Title}\"?",
            "Delete Note",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            string id = _currentNote.Id;
            _currentNote = null;
            _isDirty = false;
            _notesService.Delete(id);

            RefreshNotesList(SearchBox.Text);

            if (_notesService.Notes.Count > 0)
            {
                SelectNote(_notesService.Notes[0]);
            }
            else
            {
                CreateNewNote();
            }
        }
    }

    private void NotesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingNote) return;

        if (NotesListBox.SelectedItem is NoteDisplayItem selected)
        {
            if (_currentNote?.Id != selected.Note.Id)
            {
                if (_isDirty)
                {
                    SaveCurrentNote();
                }
                SelectNote(selected.Note);
            }
        }
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingNote) return;
        _isDirty = true;
        StatusText.Text = "Unsaved changes*";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshNotesList(SearchBox.Text);
    }

    private void NewNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        CreateNewNote();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentNote();
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        DeleteCurrentNote();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        HandleKeyDown(e.Key, Keyboard.Modifiers, e);
    }

    internal void HandleKeyDown(Key key, ModifierKeys modifiers, System.Windows.Input.KeyEventArgs? e = null)
    {
        if (key == Key.P && (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            App.CurrentApp?.ToggleCommandPalette();
            if (e != null) e.Handled = true;
            return;
        }

        if (key == Key.S && (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SaveCurrentNote();
            if (e != null) e.Handled = true;
        }
        else if (key == Key.N && (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CreateNewNote();
            if (e != null) e.Handled = true;
        }
        else if (key == Key.F && (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            if (e != null) e.Handled = true;
        }
        else if (key == Key.W && (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Close();
            if (e != null) e.Handled = true;
        }
        else if (key == Key.Delete && modifiers == ModifierKeys.None)
        {
            if (NotesListBox.IsKeyboardFocusWithin && !TitleBox.IsKeyboardFocusWithin && !ContentBox.IsKeyboardFocusWithin && !SearchBox.IsKeyboardFocusWithin)
            {
                DeleteCurrentNote();
                if (e != null) e.Handled = true;
            }
        }
        else if ((key == Key.F5 && modifiers == ModifierKeys.None) || (key == Key.R && modifiers == ModifierKeys.Control))
        {
            RefreshNotesList(SearchBox.Text);
            if (e != null) e.Handled = true;
            return;
        }
        else if (key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = "";
                if (e != null) e.Handled = true;
                return;
            }

            if (TitleBox.IsKeyboardFocusWithin || ContentBox.IsKeyboardFocusWithin || (FocusManager.GetFocusedElement(this) is TextBox))
            {
                Keyboard.ClearFocus();
                if (e != null) e.Handled = true;
                return;
            }

            Close();
            if (e != null) e.Handled = true;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isDirty)
        {
            SaveCurrentNote();
        }
        base.OnClosing(e);
    }
}
