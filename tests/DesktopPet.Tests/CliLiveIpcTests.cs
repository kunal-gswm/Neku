using System.IO;
using System.Text.Json;
using DesktopPet.Services;
using Neku.Protocol;
using Xunit;

namespace DesktopPet.Tests;

public sealed class CliLiveIpcTests
{
    [Fact]
    public async Task LiveIpc_StatusAndPetCommands_ExecuteAndRespondCorrectly()
    {
        string pipeName = $"Neku.TestPipe.{Guid.NewGuid():N}";
        bool petVisible = false;
        bool petPaused = false;

        using var server = new NekuIpcServer(req =>
        {
            switch (req.Command)
            {
                case NekuIpcProtocol.Commands.Status:
                    return Task.FromResult(NekuIpcResponse.Success("Neku is running.", new
                    {
                        running = true,
                        version = "1.0.4",
                        companionDisplayName = "Neku (Goblin companion)",
                        isPetHidden = !petVisible,
                        isPetPaused = petPaused,
                        petState = "Idle",
                        tasks = new { total = 3, pending = 2, completed = 1 },
                        notes = new { total = 5 }
                    }));

                case NekuIpcProtocol.Commands.PetShow:
                    petVisible = true;
                    return Task.FromResult(NekuIpcResponse.Success("Pet is now visible."));

                case NekuIpcProtocol.Commands.PetHide:
                    petVisible = false;
                    return Task.FromResult(NekuIpcResponse.Success("Pet is now hidden."));

                case NekuIpcProtocol.Commands.PetPause:
                    petPaused = true;
                    return Task.FromResult(NekuIpcResponse.Success("Pet paused."));

                case NekuIpcProtocol.Commands.PetResume:
                    petPaused = false;
                    return Task.FromResult(NekuIpcResponse.Success("Pet resumed."));

                default:
                    return Task.FromResult(NekuIpcResponse.Fail("unknown"));
            }
        }, pipeName);

        server.Start();
        await server.WaitForReadyAsync();

        // 1. Status command via IpcClient
        var statusResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.Status, customPipeName: pipeName);
        Assert.True(statusResp.Ok);
        Assert.Equal("Neku is running.", statusResp.Message);

        // 2. Pet show
        var showResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.PetShow, customPipeName: pipeName);
        Assert.True(showResp.Ok);
        Assert.True(petVisible);

        // 3. Pet pause
        var pauseResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.PetPause, customPipeName: pipeName);
        Assert.True(pauseResp.Ok);
        Assert.True(petPaused);

        // 4. Pet resume
        var resumeResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.PetResume, customPipeName: pipeName);
        Assert.True(resumeResp.Ok);
        Assert.False(petPaused);

        // 5. Pet hide
        var hideResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.PetHide, customPipeName: pipeName);
        Assert.True(hideResp.Ok);
        Assert.False(petVisible);
    }

    [Fact]
    public async Task LiveIpc_TaskAndNoteOperations_ExecuteThroughRealServices()
    {
        string pipeName = $"Neku.TestPipe.{Guid.NewGuid():N}";
        string tempDir = Path.Combine(Path.GetTempPath(), "Neku_CliTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var taskService = new TaskService(Path.Combine(tempDir, "tasks.json"));
            var notesService = new NotesService(Path.Combine(tempDir, "notes.json"));

            using var server = new NekuIpcServer(req =>
            {
                switch (req.Command)
                {
                    case NekuIpcProtocol.Commands.TaskAdd:
                        string title = req.GetArg("title") ?? "Untitled";
                        var t = taskService.Create(title, priority: TaskPriority.High);
                        return Task.FromResult(NekuIpcResponse.Success($"Task '{t.Title}' created.", new { id = t.Id, title = t.Title }));

                    case NekuIpcProtocol.Commands.TaskList:
                        var tasks = taskService.Tasks.Select(x => new { id = x.Id, title = x.Title, isCompleted = x.IsCompleted }).ToList();
                        return Task.FromResult(NekuIpcResponse.Success($"Found {tasks.Count} tasks.", tasks));

                    case NekuIpcProtocol.Commands.TaskComplete:
                        string id = req.GetArg("id") ?? "";
                        var target = taskService.Tasks.FirstOrDefault(x => x.Id == id || x.Id.StartsWith(id));
                        if (target == null) return Task.FromResult(NekuIpcResponse.Fail("Task not found.", 404));
                        taskService.Complete(target.Id);
                        return Task.FromResult(NekuIpcResponse.Success($"Task '{target.Title}' completed."));

                    case NekuIpcProtocol.Commands.NoteAdd:
                        string nTitle = req.GetArg("title") ?? "Untitled";
                        string nBody = req.GetArg("body") ?? "";
                        var n = notesService.Create(nTitle, nBody);
                        return Task.FromResult(NekuIpcResponse.Success($"Note '{n.Title}' created.", new { id = n.Id, title = n.Title }));

                    case NekuIpcProtocol.Commands.NoteList:
                        var notes = notesService.Notes.Select(x => new { id = x.Id, title = x.Title, preview = x.Content }).ToList();
                        return Task.FromResult(NekuIpcResponse.Success($"Found {notes.Count} notes.", notes));

                    default:
                        return Task.FromResult(NekuIpcResponse.Fail("unknown"));
                }
            }, pipeName);

            server.Start();
            await server.WaitForReadyAsync();

            // Task Add
            var addResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.TaskAdd, new() { ["title"] = "Ship CLI" }, customPipeName: pipeName);
            Assert.True(addResp.Ok);
            Assert.Contains("Ship CLI", addResp.Message);

            // Task List
            var listResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.TaskList, customPipeName: pipeName);
            Assert.True(listResp.Ok);
            Assert.NotNull(listResp.Data);

            // Note Add
            var noteResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.NoteAdd, new() { ["title"] = "Test Note", ["body"] = "Secret content" }, customPipeName: pipeName);
            Assert.True(noteResp.Ok);
            Assert.Contains("Test Note", noteResp.Message);

            // Note List
            var noteListResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.NoteList, customPipeName: pipeName);
            Assert.True(noteListResp.Ok);
            Assert.NotNull(noteListResp.Data);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
