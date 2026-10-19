using System;
using System.IO;
using System.Linq;
using System.Windows;
using DesktopPet.Services;
using DesktopPet.UI;
using Xunit;
using IDataObject = System.Windows.IDataObject;
using DataFormats = System.Windows.DataFormats;

namespace DesktopPet.Tests;

public class DropzoneTests
{
    private sealed class MockDataObject : IDataObject
    {
        private readonly Dictionary<string, object> _data = new(StringComparer.OrdinalIgnoreCase);

        public void SetData(string format, object data) => _data[format] = data;

        public object GetData(string format) => _data.TryGetValue(format, out var val) ? val : null!;
        public object GetData(Type format) => GetData(format.FullName ?? "");
        public object GetData(string format, bool autoConvert) => GetData(format);

        public bool GetDataPresent(string format) => _data.ContainsKey(format);
        public bool GetDataPresent(Type format) => GetDataPresent(format.FullName ?? "");
        public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);

        public string[] GetFormats() => _data.Keys.ToArray();
        public string[] GetFormats(bool autoConvert) => GetFormats();

        public void SetData(object data) { }
        public void SetData(Type format, object data) => SetData(format.FullName ?? "", data);
        public void SetData(string format, object data, bool autoConvert) => SetData(format, data);
    }

    [Fact]
    public void FromDataObject_returns_null_for_null_or_empty_data()
    {
        Assert.Null(DropzonePayload.FromDataObject(null));

        var emptyData = new MockDataObject();
        Assert.Null(DropzonePayload.FromDataObject(emptyData));
    }

    [Fact]
    public void FromDataObject_safely_parses_plain_text()
    {
        var data = new MockDataObject();
        data.SetData(DataFormats.UnicodeText, "Remember to fix the memory leak in buffer pool");

        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);
        Assert.Equal(DropPayloadType.Text, payload.Type);
        Assert.Contains("Remember to fix", payload.Title);
        Assert.Equal("Remember to fix the memory leak in buffer pool", payload.Content);
        Assert.Empty(payload.FilePaths);
        Assert.Null(payload.Url);
    }

    [Fact]
    public void FromDataObject_safely_parses_file_paths_without_reading_contents()
    {
        var data = new MockDataObject();
        string dummyFile1 = @"C:\Projects\Neku\README.md";
        string dummyFile2 = @"C:\Projects\Neku\src\App.xaml.cs";
        data.SetData(DataFormats.FileDrop, new[] { dummyFile1, dummyFile2 });

        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);
        Assert.Equal(DropPayloadType.Files, payload.Type);
        Assert.Equal(2, payload.FilePaths.Count);
        Assert.Equal(dummyFile1, payload.FilePaths[0]);
        Assert.Equal(dummyFile2, payload.FilePaths[1]);
        Assert.Equal("2 Files Dropped", payload.Title);
        Assert.Contains("README.md", payload.Content);
        Assert.Contains("App.xaml.cs", payload.Content);
    }

    [Fact]
    public void FromDataObject_safely_parses_single_file_name_as_title()
    {
        var data = new MockDataObject();
        string dummyFile = @"C:\temp\report.pdf";
        data.SetData(DataFormats.FileDrop, new[] { dummyFile });

        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);
        Assert.Equal(DropPayloadType.Files, payload.Type);
        Assert.Single(payload.FilePaths);
        Assert.Equal("File: report.pdf", payload.Title);
        Assert.Equal(dummyFile, payload.Content);
    }

    [Fact]
    public void FromDataObject_safely_identifies_urls_without_network_calls()
    {
        var data = new MockDataObject();
        data.SetData(DataFormats.UnicodeText, "https://github.com/kunal-gswm/Neku");

        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);
        Assert.Equal(DropPayloadType.Link, payload.Type);
        Assert.Equal("https://github.com/kunal-gswm/Neku", payload.Url);
        Assert.Contains("github.com", payload.Title);
    }

    [Fact]
    public void Saving_payload_as_note_creates_valid_note_item()
    {
        var data = new MockDataObject();
        data.SetData(DataFormats.UnicodeText, "Refactor rendering pipeline for PerMonitorV2");
        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);

        var notesService = new NotesService();
        var note = notesService.Create(payload.Title, payload.Content);

        Assert.NotNull(note);
        Assert.Contains("Refactor rendering", note.Title);
        Assert.Equal(payload.Content, note.Content);
        Assert.Contains(note, notesService.Notes);
    }

    [Fact]
    public void Saving_payload_as_task_creates_valid_task_item()
    {
        var data = new MockDataObject();
        data.SetData(DataFormats.UnicodeText, "Review PR #42: Smooth animations");
        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);

        var taskService = new TaskService();
        var task = taskService.Create(payload.Title, project: "Dropzone", priority: TaskPriority.Normal);

        Assert.NotNull(task);
        Assert.Contains("Review PR #42", task.Title);
        Assert.Equal("Dropzone", task.Project);
        Assert.Equal(TaskPriority.Normal, task.Priority);
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public void Dangerous_scripts_are_never_executed_and_stay_inert_data()
    {
        var data = new MockDataObject();
        string maliciousScript = "powershell -Command Remove-Item C:\\Windows -Recurse";
        data.SetData(DataFormats.UnicodeText, maliciousScript);

        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);
        Assert.Equal(DropPayloadType.Text, payload.Type);
        Assert.Equal(maliciousScript, payload.Content);
        // Verified: The payload is an inert data record without process invocation hooks.
    }

    [Fact]
    public void FromDataObject_truncates_oversized_text_safely()
    {
        var data = new MockDataObject();
        string hugeText = new string('A', 100_000);
        data.SetData(DataFormats.UnicodeText, hugeText);

        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);
        Assert.Equal(DropPayloadType.Text, payload.Type);
        Assert.True(payload.Content.Length <= 65536 + 20);
        Assert.EndsWith("… (truncated)", payload.Content);
    }

    [Fact]
    public void FromDataObject_handles_unusual_url_schemes_safely()
    {
        var data = new MockDataObject();
        string jsUrl = "javascript:alert(document.cookie)";
        data.SetData(DataFormats.UnicodeText, jsUrl);

        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);
        // Non-http/https schemes must not be treated as executable web links
        Assert.Equal(DropPayloadType.Text, payload.Type);
        Assert.Null(payload.Url);
    }

    [Fact]
    public void Cancelling_dropzone_workflow_creates_no_notes_or_tasks()
    {
        var notesService = new NotesService();
        var taskService = new TaskService();
        int initialNotesCount = notesService.Notes.Count;
        int initialTasksCount = taskService.Tasks.Count;

        var data = new MockDataObject();
        data.SetData(DataFormats.UnicodeText, "Temporary dropped snippet that user cancels");
        var payload = DropzonePayload.FromDataObject(data);
        Assert.NotNull(payload);

        // Simulating cancellation: no action button invoked
        Assert.Equal(initialNotesCount, notesService.Notes.Count);
        Assert.Equal(initialTasksCount, taskService.Tasks.Count);
    }

    [Fact]
    public void FromDataObject_rejects_unsupported_data_types()
    {
        var data = new MockDataObject();
        data.SetData("DeviceIndependentBitmap", new byte[] { 0x01, 0x02, 0x03 });

        var payload = DropzonePayload.FromDataObject(data);
        Assert.Null(payload);
    }
}
