using System.Text.RegularExpressions;
using DesktopPet.Engine;
using DesktopPet.Services;
using Neku.Protocol;

namespace DesktopPet;

public partial class App
{
    private NekuIpcServer? _commandIpcServer;

    private async Task<NekuIpcResponse> HandleIpcCommandAsync(NekuIpcRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Command))
        {
            return NekuIpcResponse.Fail("Missing command.", 400);
        }

        switch (request.Command.ToLowerInvariant())
        {
            case NekuIpcProtocol.Commands.Ping:
                return NekuIpcResponse.Success("pong", new
                {
                    version = "1.0.0",
                    activePet = _settings.ActivePet,
                    status = "running"
                });

            case NekuIpcProtocol.Commands.Status:
                return await Dispatcher.InvokeAsync(() =>
                {
                    return NekuIpcResponse.Success("Neku is running.", new
                    {
                        running = true,
                        version = "1.0.0",
                        activeCompanion = _settings.ActivePet,
                        companionDisplayName = AssetSource.GetDefaultDisplayName(_settings.ActivePet),
                        isPetHidden = _userHidden || _hiddenForQuiet,
                        isPetPaused = _engine?.Paused ?? false,
                        petState = _engine?.State.ToString() ?? "Idle",
                        focus = new
                        {
                            state = _focusTimer.State.ToString(),
                            remaining = _focusTimer.FormattedRemaining,
                            remainingSeconds = (int)_focusTimer.RemainingTime.TotalSeconds,
                            task = _focusTimer.ActiveTaskTitle
                        },
                        tasks = new
                        {
                            total = _taskService.Tasks.Count,
                            pending = _taskService.Tasks.Count(t => !t.IsCompleted),
                            completed = _taskService.Tasks.Count(t => t.IsCompleted)
                        },
                        notes = new
                        {
                            total = _notesService.Notes.Count
                        }
                    });
                });

            case NekuIpcProtocol.Commands.Quit:
                // Allow the IPC server to complete sending the acknowledgement response before the application tears down
                _ = Task.Run(async () =>
                {
                    await Task.Delay(150).ConfigureAwait(false);
                    await Dispatcher.InvokeAsync(QuitApp);
                });
                return NekuIpcResponse.Success("Neku desktop companion is shutting down.");

            case NekuIpcProtocol.Commands.PetShow:
                await Dispatcher.InvokeAsync(() => OnHideToggled(false));
                return NekuIpcResponse.Success("Pet is now visible.");

            case NekuIpcProtocol.Commands.PetHide:
                await Dispatcher.InvokeAsync(() => OnHideToggled(true));
                return NekuIpcResponse.Success("Pet is now hidden.");

            case NekuIpcProtocol.Commands.PetPause:
                await Dispatcher.InvokeAsync(() => OnPauseToggled(true));
                return NekuIpcResponse.Success("Pet is now paused.");

            case NekuIpcProtocol.Commands.PetResume:
                await Dispatcher.InvokeAsync(() => OnPauseToggled(false));
                return NekuIpcResponse.Success("Pet is now resumed.");

            case NekuIpcProtocol.Commands.PetStatus:
                return await Dispatcher.InvokeAsync(() =>
                {
                    return NekuIpcResponse.Success("Pet status retrieved.", new
                    {
                        companion = _settings.ActivePet,
                        companionDisplayName = AssetSource.GetDefaultDisplayName(_settings.ActivePet),
                        hidden = _userHidden || _hiddenForQuiet,
                        paused = _engine?.Paused ?? false,
                        state = _engine?.State.ToString() ?? "Idle",
                        x = _petWindow != null ? (int)_petWindow.Left : 0,
                        y = _petWindow != null ? (int)_petWindow.Top : 0
                    });
                });

            case NekuIpcProtocol.Commands.FocusStart:
                int minutes = _settings.FocusDurationMinutes;
                string? minsArg = request.GetArg("minutes");
                if (!string.IsNullOrWhiteSpace(minsArg))
                {
                    if (!int.TryParse(minsArg, out minutes) || minutes < 1 || minutes > 480)
                    {
                        return NekuIpcResponse.Fail("Invalid duration. Minutes must be an integer between 1 and 480.", 400);
                    }
                }
                string? focusTask = request.GetArg("task");
                string? focusProj = request.GetArg("project");
                await Dispatcher.InvokeAsync(() =>
                {
                    _focusTimer.StartFocus(minutes, null, focusTask, focusProj);
                });
                return NekuIpcResponse.Success($"Focus session started for {minutes} minutes.", new
                {
                    minutes,
                    task = focusTask,
                    project = focusProj
                });

            case NekuIpcProtocol.Commands.FocusStatus:
                return await Dispatcher.InvokeAsync(() =>
                {
                    return NekuIpcResponse.Success("Focus status retrieved.", new
                    {
                        state = _focusTimer.State.ToString(),
                        isActive = _focusTimer.IsActive,
                        isPaused = _focusTimer.IsPaused,
                        remaining = _focusTimer.FormattedRemaining,
                        remainingSeconds = (int)_focusTimer.RemainingTime.TotalSeconds,
                        sessionType = _focusTimer.CurrentSessionType.ToString(),
                        task = _focusTimer.ActiveTaskTitle,
                        project = _focusTimer.ActiveTaskProject
                    });
                });

            case NekuIpcProtocol.Commands.FocusStop:
                await Dispatcher.InvokeAsync(() => _focusTimer.Cancel());
                return NekuIpcResponse.Success("Focus session stopped.");

            case NekuIpcProtocol.Commands.FocusPause:
                await Dispatcher.InvokeAsync(() => _focusTimer.Pause());
                return NekuIpcResponse.Success("Focus session paused.");

            case NekuIpcProtocol.Commands.FocusResume:
                await Dispatcher.InvokeAsync(() => _focusTimer.Resume());
                return NekuIpcResponse.Success("Focus session resumed.");

            case NekuIpcProtocol.Commands.TaskAdd:
                string? taskTitle = request.GetArg("title");
                if (string.IsNullOrWhiteSpace(taskTitle))
                {
                    return NekuIpcResponse.Fail("Task title is required.", 400);
                }
                string? priorityStr = request.GetArg("priority");
                TaskPriority priority = TaskPriority.Normal;
                if (!string.IsNullOrWhiteSpace(priorityStr))
                {
                    if (Enum.TryParse<TaskPriority>(priorityStr, true, out var parsedPri))
                    {
                        priority = parsedPri;
                    }
                    else
                    {
                        return NekuIpcResponse.Fail($"Invalid priority '{priorityStr}'. Must be Low, Normal, High, or Urgent.", 400);
                    }
                }
                string taskProj = request.GetArg("project") ?? "";
                string taskDesc = request.GetArg("description") ?? "";
                var createdTask = _taskService.Create(taskTitle, description: taskDesc, priority: priority, project: taskProj);
                return NekuIpcResponse.Success($"Task '{createdTask.Title}' created.", new
                {
                    id = createdTask.Id,
                    title = createdTask.Title,
                    priority = createdTask.Priority.ToString(),
                    isCompleted = createdTask.IsCompleted,
                    project = createdTask.Project
                });

            case NekuIpcProtocol.Commands.TaskList:
                bool showAll = string.Equals(request.GetArg("all"), "true", StringComparison.OrdinalIgnoreCase);
                var tasks = _taskService.Tasks
                    .Where(t => showAll || !t.IsCompleted)
                    .Select(t => new
                    {
                        id = t.Id,
                        title = t.Title,
                        priority = t.Priority.ToString(),
                        isCompleted = t.IsCompleted,
                        project = t.Project,
                        dueDate = t.DueDate?.ToString("yyyy-MM-dd")
                    })
                    .ToList();
                return NekuIpcResponse.Success($"Found {tasks.Count} tasks.", tasks);

            case NekuIpcProtocol.Commands.TaskComplete:
                string? completeId = request.GetArg("id");
                if (string.IsNullOrWhiteSpace(completeId))
                {
                    return NekuIpcResponse.Fail("Task ID is required.", 400);
                }
                var targetTask = _taskService.Tasks.FirstOrDefault(t =>
                    t.Id.Equals(completeId, StringComparison.OrdinalIgnoreCase) ||
                    t.Id.StartsWith(completeId, StringComparison.OrdinalIgnoreCase));
                if (targetTask == null)
                {
                    return NekuIpcResponse.Fail($"Task '{completeId}' not found.", 404);
                }
                _taskService.Complete(targetTask.Id);
                return NekuIpcResponse.Success($"Task '{targetTask.Title}' marked as complete.", new
                {
                    id = targetTask.Id,
                    title = targetTask.Title
                });

            case NekuIpcProtocol.Commands.TaskDelete:
                string? deleteId = request.GetArg("id");
                if (string.IsNullOrWhiteSpace(deleteId))
                {
                    return NekuIpcResponse.Fail("Task ID is required.", 400);
                }
                var delTask = _taskService.Tasks.FirstOrDefault(t =>
                    t.Id.Equals(deleteId, StringComparison.OrdinalIgnoreCase) ||
                    t.Id.StartsWith(deleteId, StringComparison.OrdinalIgnoreCase));
                if (delTask == null)
                {
                    return NekuIpcResponse.Fail($"Task '{deleteId}' not found.", 404);
                }
                _taskService.Delete(delTask.Id);
                return NekuIpcResponse.Success($"Task '{delTask.Title}' deleted.", new
                {
                    id = delTask.Id,
                    title = delTask.Title
                });

            case NekuIpcProtocol.Commands.NoteAdd:
                string noteTitle = request.GetArg("title") ?? "Untitled";
                string noteContent = request.GetArg("body") ?? request.GetArg("content") ?? "";
                var note = _notesService.Create(noteTitle, noteContent);
                return NekuIpcResponse.Success($"Note '{note.Title}' created.", new
                {
                    id = note.Id,
                    title = note.Title,
                    createdAt = note.CreatedAt
                });

            case NekuIpcProtocol.Commands.NoteList:
                var notes = _notesService.Notes.Select(n => new
                {
                    id = n.Id,
                    title = n.Title,
                    updatedAt = n.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    preview = n.Content.Length > 60 ? n.Content[..60] + "..." : n.Content
                }).ToList();
                return NekuIpcResponse.Success($"Found {notes.Count} notes.", notes);

            case NekuIpcProtocol.Commands.NoteSearch:
                string query = request.GetArg("query") ?? "";
                var matches = _notesService.Notes
                    .Where(n => n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                n.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Select(n => new
                    {
                        id = n.Id,
                        title = n.Title,
                        updatedAt = n.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        preview = n.Content.Length > 60 ? n.Content[..60] + "..." : n.Content
                    })
                    .ToList();
                return NekuIpcResponse.Success($"Found {matches.Count} notes matching '{query}'.", matches);

            case NekuIpcProtocol.Commands.NoteGet:
                string? noteId = request.GetArg("id");
                if (string.IsNullOrWhiteSpace(noteId))
                {
                    return NekuIpcResponse.Fail("Note ID is required.", 400);
                }
                var nItem = _notesService.Notes.FirstOrDefault(n =>
                    n.Id.Equals(noteId, StringComparison.OrdinalIgnoreCase) ||
                    n.Id.StartsWith(noteId, StringComparison.OrdinalIgnoreCase));
                if (nItem == null)
                {
                    return NekuIpcResponse.Fail($"Note '{noteId}' not found.", 404);
                }
                return NekuIpcResponse.Success("Note retrieved.", new
                {
                    id = nItem.Id,
                    title = nItem.Title,
                    content = nItem.Content,
                    createdAt = nItem.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    updatedAt = nItem.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                });

            case NekuIpcProtocol.Commands.NoteDelete:
                string? delNoteId = request.GetArg("id");
                if (string.IsNullOrWhiteSpace(delNoteId))
                {
                    return NekuIpcResponse.Fail("Note ID is required.", 400);
                }
                var delNote = _notesService.Notes.FirstOrDefault(n =>
                    n.Id.Equals(delNoteId, StringComparison.OrdinalIgnoreCase) ||
                    n.Id.StartsWith(delNoteId, StringComparison.OrdinalIgnoreCase));
                if (delNote == null)
                {
                    return NekuIpcResponse.Fail($"Note '{delNoteId}' not found.", 404);
                }
                _notesService.Delete(delNote.Id);
                return NekuIpcResponse.Success($"Note '{delNote.Title}' deleted.", new
                {
                    id = delNote.Id,
                    title = delNote.Title
                });

            case NekuIpcProtocol.Commands.CompanionList:
                var packs = AssetSource.DiscoverPacks();
                string activePet = _settings.ActivePet;
                var compList = packs.Select(p => new
                {
                    id = p.Id,
                    displayName = p.DisplayName,
                    isActive = p.Id.Equals(activePet, StringComparison.OrdinalIgnoreCase),
                    isCustom = p.IsCustom,
                    isValid = p.IsValid,
                    error = p.Error
                }).ToList();
                return NekuIpcResponse.Success($"Found {compList.Count} companions.", compList);

            case NekuIpcProtocol.Commands.CompanionSet:
                string? petName = request.GetArg("name");
                if (string.IsNullOrWhiteSpace(petName))
                {
                    return NekuIpcResponse.Fail("Companion name is required.", 400);
                }
                bool switched = await Dispatcher.InvokeAsync(() => TrySwitchPet(petName));
                if (!switched)
                {
                    var available = AssetSource.DiscoverPacks()
                        .Where(p => p.IsValid)
                        .Select(p => p.Id)
                        .ToList();
                    return NekuIpcResponse.Fail($"Unknown or invalid companion '{petName}'. Available: {string.Join(", ", available)}", 404);
                }
                return NekuIpcResponse.Success($"Active companion set to '{_settings.ActivePet}'.", new
                {
                    companion = _settings.ActivePet,
                    displayName = AssetSource.GetDefaultDisplayName(_settings.ActivePet)
                });

            case NekuIpcProtocol.Commands.Event:
                string? eventType = request.GetArg("type");
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    return NekuIpcResponse.Fail("Event type is required (e.g. 'build_success', 'build_failure', 'test_pass', 'test_failure').", 400);
                }
                string? eventMsg = request.GetArg("message");
                await Dispatcher.InvokeAsync(() =>
                {
                    _developerEvents?.HandleEvent(eventType.ToLowerInvariant(), eventMsg);
                });
                return NekuIpcResponse.Success($"Developer event '{eventType}' processed.", new
                {
                    type = eventType,
                    message = eventMsg
                });

            default:
                return NekuIpcResponse.Fail($"Unknown command '{request.Command}'.", 400);
        }
    }
}
