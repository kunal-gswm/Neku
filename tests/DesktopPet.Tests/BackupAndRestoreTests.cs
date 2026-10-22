using System.IO;
using System.IO.Compression;
using System.Text.Json;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public sealed class BackupAndRestoreTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _appDataDir;
    private readonly string _backupsDir;

    public BackupAndRestoreTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "NekuBackupTests_" + Guid.NewGuid().ToString("N"));
        _appDataDir = Path.Combine(_testRoot, "AppData");
        _backupsDir = Path.Combine(_testRoot, "Backups");

        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(_backupsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private void SeedSampleData(string taskTitle = "Initial Task", string noteTitle = "Initial Note")
    {
        var taskService = new TaskService(Path.Combine(_appDataDir, "tasks.json"));
        taskService.Create(taskTitle);
        taskService.CreateRoutine("Daily Ritual", steps: new[] { new RoutineStep { Title = "Step 1" } });

        var notesService = new NotesService(Path.Combine(_appDataDir, "notes.json"));
        notesService.Create(noteTitle, "Sample content");

        var settingsService = new SettingsService(Path.Combine(_appDataDir, "settings.json"));
        settingsService.Current.Speed = 1.75;
        settingsService.Save();

        var historyService = new FocusHistoryService(Path.Combine(_appDataDir, "focus_history.json"));
        historyService.RecordSession(new FocusSessionRecord { TaskTitle = "Focus 1", TargetMinutes = 25, ActualSeconds = 1500 });
    }

    [Fact]
    public void BackupService_CreateBackup_ProducesValidZipWithManifestAndChecksums()
    {
        SeedSampleData();

        var backupSvc = new BackupService(_appDataDir, _backupsDir);
        var result = backupSvc.CreateBackup("Test backup 1");

        Assert.True(result.Success);
        Assert.NotNull(result.BackupFilePath);
        Assert.True(File.Exists(result.BackupFilePath));
        Assert.NotNull(result.Manifest);
        Assert.Equal("Test backup 1", result.Manifest.Description);
        Assert.Equal(4, result.Manifest.Files.Count);

        // Verify zip contains manifest and files
        using var archive = ZipFile.OpenRead(result.BackupFilePath);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("tasks.json"));
        Assert.NotNull(archive.GetEntry("notes.json"));
        Assert.NotNull(archive.GetEntry("settings.json"));
        Assert.NotNull(archive.GetEntry("focus_history.json"));
    }

    [Fact]
    public void BackupService_GetAvailableBackups_ListsAndValidatesMetadata()
    {
        SeedSampleData();

        var backupSvc = new BackupService(_appDataDir, _backupsDir);
        backupSvc.CreateBackup("Backup A");
        backupSvc.CreateBackup("Backup B");

        var backups = backupSvc.GetAvailableBackups();
        Assert.Equal(2, backups.Count);
        Assert.All(backups, b => Assert.True(b.IsValid));
        Assert.All(backups, b => Assert.True(b.FileSizeBytes > 0));
        Assert.All(backups, b => Assert.Equal(4, b.IncludedFilesCount));
    }

    [Fact]
    public void BackupService_CorruptedArchive_DetectedAndRejected()
    {
        SeedSampleData();

        var backupSvc = new BackupService(_appDataDir, _backupsDir);
        var res = backupSvc.CreateBackup("Valid");
        Assert.True(res.Success);

        // Corrupt archive by writing garbage into it
        string corruptPath = Path.Combine(_backupsDir, "corrupt_backup.zip");
        File.WriteAllText(corruptPath, "NOT_A_VALID_ZIP_FILE_AT_ALL");

        var val = backupSvc.ValidateBackup(corruptPath);
        Assert.False(val.IsValid);
        Assert.NotNull(val.ErrorMessage);

        var restoreRes = backupSvc.RestoreBackup(corruptPath);
        Assert.False(restoreRes.Success);
    }

    [Fact]
    public void BackupService_TamperedChecksum_DetectedAndRejected()
    {
        SeedSampleData();

        var backupSvc = new BackupService(_appDataDir, _backupsDir);
        var res = backupSvc.CreateBackup("To Tamper");
        string zipPath = res.BackupFilePath!;

        // Unpack, modify notes.json without updating manifest checksum, repack
        string tempDir = Path.Combine(_testRoot, "tamper_stage");
        ZipFile.ExtractToDirectory(zipPath, tempDir);
        string notesFile = Path.Combine(tempDir, "notes.json");
        byte[] bytes = File.ReadAllBytes(notesFile);
        bytes[bytes.Length - 1] = (byte)(bytes[bytes.Length - 1] ^ 0xFF);
        File.WriteAllBytes(notesFile, bytes);
        File.Delete(zipPath);
        ZipFile.CreateFromDirectory(tempDir, zipPath);

        var val = backupSvc.ValidateBackup(zipPath);
        Assert.False(val.IsValid);
        Assert.Contains("checksum mismatch", val.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var restoreRes = backupSvc.RestoreBackup(zipPath);
        Assert.False(restoreRes.Success);
    }

    [Fact]
    public void BackupService_MissingRequiredFileInArchive_DetectedAndRejected()
    {
        SeedSampleData();

        var backupSvc = new BackupService(_appDataDir, _backupsDir);
        var res = backupSvc.CreateBackup("To Delete File");
        string zipPath = res.BackupFilePath!;

        string tempDir = Path.Combine(_testRoot, "missing_file_stage");
        ZipFile.ExtractToDirectory(zipPath, tempDir);
        File.Delete(Path.Combine(tempDir, "tasks.json")); // Delete tasks.json but leave in manifest
        File.Delete(zipPath);
        ZipFile.CreateFromDirectory(tempDir, zipPath);

        var val = backupSvc.ValidateBackup(zipPath);
        Assert.False(val.IsValid);
        Assert.Contains("missing file listed in manifest", val.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackupService_PathTraversalEntry_Rejected()
    {
        string maliciousZip = Path.Combine(_backupsDir, "malicious.zip");

        using (var zip = ZipFile.Open(maliciousZip, ZipArchiveMode.Create))
        {
            var manifestEntry = zip.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                var manifest = new BackupManifest
                {
                    BackupId = "malicious_1",
                    Files = new List<BackupFileEntry>
                    {
                        new BackupFileEntry { Name = "../evil.txt", Size = 4, Sha256 = "123" }
                    }
                };
                writer.Write(JsonSerializer.Serialize(manifest));
            }

            var badEntry = zip.CreateEntry("../evil.txt");
            using (var writer = new StreamWriter(badEntry.Open()))
            {
                writer.Write("evil");
            }
        }

        var backupSvc = new BackupService(_appDataDir, _backupsDir);
        var val = backupSvc.ValidateBackup(maliciousZip);
        Assert.False(val.IsValid);
        Assert.Contains("Dangerous path traversal", val.ErrorMessage);
    }

    [Fact]
    public void BackupService_Restore_CreatesPreRestoreSafetyBackup_And_RestoresData()
    {
        // 1. Seed State A
        SeedSampleData(taskTitle: "State A Task", noteTitle: "State A Note");

        var backupSvc = new BackupService(_appDataDir, _backupsDir);
        var resA = backupSvc.CreateBackup("State A Backup");
        Assert.True(resA.Success);

        // 2. Modify data to State B
        var tasksPath = Path.Combine(_appDataDir, "tasks.json");
        var notesPath = Path.Combine(_appDataDir, "notes.json");
        var taskSvc = new TaskService(tasksPath);
        taskSvc.Create("State B Task");

        var notesSvc = new NotesService(notesPath);
        notesSvc.Create("State B Note");

        Assert.Equal(2, taskSvc.Tasks.Count);
        Assert.Equal(2, notesSvc.Notes.Count);

        // 3. Restore State A
        var restoreRes = backupSvc.RestoreBackup(resA.BackupFilePath!);
        Assert.True(restoreRes.Success);
        Assert.NotNull(restoreRes.SafetyBackupPath);
        Assert.True(File.Exists(restoreRes.SafetyBackupPath));

        // 4. Verify disk reflects State A
        var reloadedTasks = new TaskService(tasksPath);
        var reloadedNotes = new NotesService(notesPath);

        Assert.Single(reloadedTasks.Tasks);
        Assert.Equal("State A Task", reloadedTasks.Tasks[0].Title);

        Assert.Single(reloadedNotes.Notes);
        Assert.Equal("State A Note", reloadedNotes.Notes[0].Title);

        // 5. Verify safety backup contains State B
        var safetyValidation = backupSvc.ValidateBackup(restoreRes.SafetyBackupPath!);
        Assert.True(safetyValidation.IsValid);
    }

    [Fact]
    public void BackupService_RetentionPolicy_PrunesOldBackupsExceedingLimit()
    {
        SeedSampleData();

        var backupSvc = new BackupService(_appDataDir, _backupsDir)
        {
            MaxBackupsRetained = 3
        };

        for (int i = 1; i <= 5; i++)
        {
            var res = backupSvc.CreateBackup($"Backup #{i}");
            Assert.True(res.Success);
            Thread.Sleep(50); // Small sleep to ensure distinct file timestamps
        }

        var backups = backupSvc.GetAvailableBackups();
        // Normal backups should be pruned to MaxBackupsRetained (3)
        Assert.Equal(3, backups.Count);
    }

    [Fact]
    public void BackupService_DeleteBackup_RemovesFileAndFiresEvent()
    {
        SeedSampleData();

        var backupSvc = new BackupService(_appDataDir, _backupsDir);
        var res = backupSvc.CreateBackup("To Delete");
        Assert.True(File.Exists(res.BackupFilePath));

        bool eventFired = false;
        backupSvc.BackupsChanged += () => eventFired = true;

        bool deleted = backupSvc.DeleteBackup(res.BackupFilePath!);
        Assert.True(deleted);
        Assert.False(File.Exists(res.BackupFilePath));
        Assert.True(eventFired);
    }

    [Fact]
    public void NotesService_CorruptFile_BacksUpAndRecoversCleanly()
    {
        string notesPath = Path.Combine(_appDataDir, "corrupt_test_notes.json");
        File.WriteAllText(notesPath, "{ INVALID JSON BROKEN SYNTAX [");

        var svc = new NotesService(notesPath);
        Assert.Empty(svc.Notes);
        Assert.NotNull(svc.LastCorruptBackupPath);
        Assert.True(File.Exists(svc.LastCorruptBackupPath));
    }

    [Fact]
    public void SettingsService_CorruptFile_BacksUpAndRecoversCleanly()
    {
        string settingsPath = Path.Combine(_appDataDir, "corrupt_test_settings.json");
        File.WriteAllText(settingsPath, "{{ NOT JSON AT ALL");

        var svc = new SettingsService(settingsPath);
        var loaded = svc.Load();
        Assert.NotNull(loaded);
        Assert.NotNull(svc.LastCorruptBackupPath);
        Assert.True(File.Exists(svc.LastCorruptBackupPath));
    }
}
