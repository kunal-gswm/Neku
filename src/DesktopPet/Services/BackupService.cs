using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.Services;

public sealed class BackupFileEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}

public sealed class BackupManifest
{
    [JsonPropertyName("manifestVersion")]
    public int ManifestVersion { get; set; } = 1;

    [JsonPropertyName("backupId")]
    public string BackupId { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0.5";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "Manual user backup";

    [JsonPropertyName("isSafetyBackup")]
    public bool IsSafetyBackup { get; set; } = false;

    [JsonPropertyName("files")]
    public List<BackupFileEntry> Files { get; set; } = new();
}

public sealed class BackupMetadata
{
    public string BackupId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public DateTime CreatedAt { get; set; }
    public string AppVersion { get; set; } = "1.0.5";
    public string Description { get; set; } = "";
    public bool IsSafetyBackup { get; set; }
    public long FileSizeBytes { get; set; }
    public int IncludedFilesCount { get; set; }
    public bool IsValid { get; set; }
    public string? ValidationError { get; set; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Description)
        ? (IsSafetyBackup ? "Pre-Restore Safety Snapshot" : "Local Backup")
        : Description;

    public string DisplayTime => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string DisplaySize => $"{FileSizeBytes / 1024.0:F1} KB";
    public string StatusBadge => IsValid ? "✅ Valid" : "⚠️ Corrupted / Invalid";
}

public sealed class BackupResult
{
    public bool Success { get; set; }
    public string? BackupFilePath { get; set; }
    public BackupManifest? Manifest { get; set; }
    public string? ErrorMessage { get; set; }

    public static BackupResult Ok(string filePath, BackupManifest manifest) =>
        new() { Success = true, BackupFilePath = filePath, Manifest = manifest };

    public static BackupResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}

public sealed class RestoreResult
{
    public bool Success { get; set; }
    public string? SafetyBackupPath { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RolledBack { get; set; }

    public static RestoreResult Ok(string? safetyBackupPath) =>
        new() { Success = true, SafetyBackupPath = safetyBackupPath };

    public static RestoreResult Fail(string error, bool rolledBack = false, string? safetyBackupPath = null) =>
        new() { Success = false, ErrorMessage = error, RolledBack = rolledBack, SafetyBackupPath = safetyBackupPath };
}

public sealed class BackupValidationResult
{
    public bool IsValid { get; set; }
    public BackupManifest? Manifest { get; set; }
    public string? ErrorMessage { get; set; }

    public static BackupValidationResult Valid(BackupManifest manifest) =>
        new() { IsValid = true, Manifest = manifest };

    public static BackupValidationResult Invalid(string error) =>
        new() { IsValid = false, ErrorMessage = error };
}

/// <summary>
/// Provides secure local backup and transactional restore for user productivity data:
/// tasks, recurring schedules, daily routines, notes, settings, and focus history.
/// Fully offline, local-first, zero telemetry, zero cloud synchronization.
/// </summary>
public sealed class BackupService
{
    private readonly string _appDataDir;
    private readonly string _backupsDir;
    private readonly TaskService? _taskService;
    private readonly NotesService? _notesService;
    private readonly SettingsService? _settingsService;
    private readonly FocusHistoryService? _focusHistoryService;

    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public int MaxBackupsRetained { get; set; } = 10;
    public string BackupsDirectory => _backupsDir;
    public event Action? BackupsChanged;

    public static readonly string[] DataFileNames =
    [
        "tasks.json",
        "notes.json",
        "settings.json",
        "focus_history.json"
    ];

    public BackupService(
        string? customAppDataDir = null,
        string? customBackupsDir = null,
        TaskService? taskService = null,
        NotesService? notesService = null,
        SettingsService? settingsService = null,
        FocusHistoryService? focusHistoryService = null)
    {
        _appDataDir = !string.IsNullOrWhiteSpace(customAppDataDir)
            ? customAppDataDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku");

        _backupsDir = !string.IsNullOrWhiteSpace(customBackupsDir)
            ? customBackupsDir
            : Path.Combine(_appDataDir, "backups");

        _taskService = taskService;
        _notesService = notesService;
        _settingsService = settingsService;
        _focusHistoryService = focusHistoryService;

        try
        {
            Directory.CreateDirectory(_backupsDir);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BackupService] Failed to create backups directory: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a local zip backup archive of current user data files with manifest and SHA256 checksums.
    /// </summary>
    public BackupResult CreateBackup(string description = "Manual backup", bool isSafety = false)
    {
        lock (_lock)
        {
            string? stagingDir = null;
            string? tempZipPath = null;

            try
            {
                // 1. Flush any pending memory state to disk
                FlushServices();

                Directory.CreateDirectory(_backupsDir);

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string shortId = Guid.NewGuid().ToString("N")[..8];
                string prefix = isSafety ? "safety_backup" : "backup";
                string backupId = $"{prefix}_{timestamp}_{shortId}";
                string finalZipPath = Path.Combine(_backupsDir, $"{backupId}.zip");
                tempZipPath = Path.Combine(_backupsDir, $"{backupId}.tmp.{Guid.NewGuid():N}.zip");

                // 2. Stage files
                stagingDir = Path.Combine(Path.GetTempPath(), "NekuBackupStage_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingDir);

                var manifest = new BackupManifest
                {
                    ManifestVersion = 1,
                    BackupId = backupId,
                    CreatedAt = DateTime.UtcNow,
                    AppVersion = "1.0.5",
                    Description = description,
                    IsSafetyBackup = isSafety,
                    Files = new List<BackupFileEntry>()
                };

                foreach (var fileName in DataFileNames)
                {
                    string sourcePath = Path.Combine(_appDataDir, fileName);
                    if (File.Exists(sourcePath))
                    {
                        string targetPath = Path.Combine(stagingDir, fileName);
                        File.Copy(sourcePath, targetPath, overwrite: true);

                        var fi = new FileInfo(targetPath);
                        string hash = ComputeSha256(targetPath);
                        manifest.Files.Add(new BackupFileEntry
                        {
                            Name = fileName,
                            Size = fi.Length,
                            Sha256 = hash
                        });
                    }
                }

                if (manifest.Files.Count == 0)
                {
                    return BackupResult.Fail("No data files found to back up in " + _appDataDir);
                }

                // 3. Write manifest.json
                string manifestJson = JsonSerializer.Serialize(manifest, JsonOpts);
                File.WriteAllText(Path.Combine(stagingDir, "manifest.json"), manifestJson);

                // 4. Create zip archive
                ZipFile.CreateFromDirectory(stagingDir, tempZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                // 5. Atomic move
                File.Move(tempZipPath, finalZipPath, overwrite: true);
                tempZipPath = null;

                // 6. Prune old backups to respect retention policy
                if (!isSafety)
                {
                    PruneOldBackups();
                }

                BackupsChanged?.Invoke();
                return BackupResult.Ok(finalZipPath, manifest);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[BackupService] Backup creation failed: {ex.Message}");
                return BackupResult.Fail("Backup creation failed: " + ex.Message);
            }
            finally
            {
                if (stagingDir != null && Directory.Exists(stagingDir))
                {
                    try { Directory.Delete(stagingDir, recursive: true); } catch { }
                }
                if (tempZipPath != null && File.Exists(tempZipPath))
                {
                    try { File.Delete(tempZipPath); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Lists all available backups in the backup directory, ordered newest first.
    /// Validates archive integrity and checksums for each backup.
    /// </summary>
    public IReadOnlyList<BackupMetadata> GetAvailableBackups()
    {
        lock (_lock)
        {
            var results = new List<BackupMetadata>();
            if (!Directory.Exists(_backupsDir))
            {
                return results;
            }

            var zipFiles = Directory.GetFiles(_backupsDir, "*.zip")
                                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                    .ToList();

            foreach (var zipPath in zipFiles)
            {
                // Skip stray temporary zip files
                if (zipPath.Contains(".tmp.")) continue;

                var fi = new FileInfo(zipPath);
                var meta = new BackupMetadata
                {
                    FilePath = zipPath,
                    FileSizeBytes = fi.Length,
                    CreatedAt = fi.LastWriteTimeUtc
                };

                var validation = ValidateBackup(zipPath);
                if (validation.IsValid && validation.Manifest != null)
                {
                    meta.BackupId = validation.Manifest.BackupId;
                    meta.CreatedAt = validation.Manifest.CreatedAt;
                    meta.AppVersion = validation.Manifest.AppVersion;
                    meta.Description = validation.Manifest.Description;
                    meta.IsSafetyBackup = validation.Manifest.IsSafetyBackup;
                    meta.IncludedFilesCount = validation.Manifest.Files.Count;
                    meta.IsValid = true;
                }
                else
                {
                    meta.IsValid = false;
                    meta.ValidationError = validation.ErrorMessage ?? "Corrupt or invalid backup archive";
                    meta.BackupId = Path.GetFileNameWithoutExtension(zipPath);
                }

                results.Add(meta);
            }

            return results;
        }
    }

    /// <summary>
    /// Validates a backup archive's manifest, file presence, checksums, and safety constraints without altering data.
    /// </summary>
    public BackupValidationResult ValidateBackup(string backupFilePath)
    {
        if (!File.Exists(backupFilePath))
        {
            return BackupValidationResult.Invalid("Backup file does not exist.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(backupFilePath);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                return BackupValidationResult.Invalid("Archive does not contain a manifest.json file.");
            }

            BackupManifest? manifest;
            using (var stream = manifestEntry.Open())
            {
                manifest = JsonSerializer.Deserialize<BackupManifest>(stream, JsonOpts);
            }

            if (manifest == null)
            {
                return BackupValidationResult.Invalid("manifest.json could not be deserialized.");
            }

            if (manifest.ManifestVersion < 1)
            {
                return BackupValidationResult.Invalid($"Unsupported manifest version: {manifest.ManifestVersion}.");
            }

            if (manifest.Files == null || manifest.Files.Count == 0)
            {
                return BackupValidationResult.Invalid("manifest.json lists no included data files.");
            }

            // Path traversal & safe entry names check
            foreach (var entry in archive.Entries)
            {
                string norm = entry.FullName.Replace('\\', '/');
                if (norm.Contains("..") || norm.StartsWith('/') || Path.IsPathRooted(entry.FullName))
                {
                    return BackupValidationResult.Invalid($"Dangerous path traversal detected in archive entry: {entry.FullName}");
                }

                // Check that only expected data files or manifest are in archive
                if (norm != "manifest.json" && !DataFileNames.Contains(norm))
                {
                    return BackupValidationResult.Invalid($"Unexpected entry found in backup archive: {entry.FullName}");
                }
            }

            // Verify each file listed in manifest
            foreach (var fileEntry in manifest.Files)
            {
                var entry = archive.GetEntry(fileEntry.Name);
                if (entry == null)
                {
                    return BackupValidationResult.Invalid($"Archive is missing file listed in manifest: {fileEntry.Name}");
                }

                if (entry.Length != fileEntry.Size)
                {
                    return BackupValidationResult.Invalid($"File size mismatch for {fileEntry.Name}: expected {fileEntry.Size}, actual {entry.Length}.");
                }

                // Compute SHA256 of entry stream
                using var entryStream = entry.Open();
                string actualHash = ComputeSha256(entryStream);
                if (!string.Equals(actualHash, fileEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return BackupValidationResult.Invalid($"SHA256 checksum mismatch for {fileEntry.Name}. Expected {fileEntry.Sha256}, got {actualHash}.");
                }
            }

            return BackupValidationResult.Valid(manifest);
        }
        catch (Exception ex)
        {
            return BackupValidationResult.Invalid("Archive validation error: " + ex.Message);
        }
    }

    /// <summary>
    /// Executes a controlled transactional restore of a backup archive into %AppData%\Neku.
    /// Protects current data with an automatic pre-restore safety snapshot before changing files.
    /// Rolls back automatically if an error occurs.
    /// </summary>
    public RestoreResult RestoreBackup(string backupFilePath)
    {
        lock (_lock)
        {
            string? stagingDir = null;
            string? safetyBackupPath = null;

            try
            {
                // 1. Validate the archive thoroughly
                var validation = ValidateBackup(backupFilePath);
                if (!validation.IsValid || validation.Manifest == null)
                {
                    return RestoreResult.Fail("Cannot restore invalid backup: " + validation.ErrorMessage);
                }

                var manifest = validation.Manifest;

                // 2. Extract into isolated temporary staging directory
                stagingDir = Path.Combine(Path.GetTempPath(), "NekuRestoreStage_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingDir);

                using (var archive = ZipFile.OpenRead(backupFilePath))
                {
                    foreach (var fileEntry in manifest.Files)
                    {
                        var entry = archive.GetEntry(fileEntry.Name);
                        if (entry == null) continue;

                        string targetStagedPath = Path.Combine(stagingDir, fileEntry.Name);
                        entry.ExtractToFile(targetStagedPath, overwrite: true);
                    }
                }

                // 3. Test deserialization of all staged files into memory models to guarantee schema compatibility
                string testError = TestDeserialization(stagingDir, manifest.Files);
                if (!string.IsNullOrEmpty(testError))
                {
                    return RestoreResult.Fail("Backup data validation failed schema test: " + testError);
                }

                // 4. Create an automatic pre-restore safety snapshot of the CURRENT data
                var safetyResult = CreateBackup(description: "Automatic safety snapshot before restoring " + Path.GetFileName(backupFilePath), isSafety: true);
                if (!safetyResult.Success)
                {
                    return RestoreResult.Fail("Failed to create safety snapshot of current data before restore: " + safetyResult.ErrorMessage);
                }
                safetyBackupPath = safetyResult.BackupFilePath;

                // 5. Replace current files with staged files atomically
                Directory.CreateDirectory(_appDataDir);
                foreach (var fileEntry in manifest.Files)
                {
                    string stagedFile = Path.Combine(stagingDir, fileEntry.Name);
                    string liveTargetFile = Path.Combine(_appDataDir, fileEntry.Name);
                    string tempTargetFile = liveTargetFile + ".restore.tmp." + Guid.NewGuid().ToString("N");

                    File.Copy(stagedFile, tempTargetFile, overwrite: true);
                    File.Move(tempTargetFile, liveTargetFile, overwrite: true);
                }

                // 6. Reload live services in memory and notify UI
                ReloadServices();

                BackupsChanged?.Invoke();
                return RestoreResult.Ok(safetyBackupPath);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[BackupService] Restore failed: {ex.Message}. Initiating rollback.");
                bool rolledBack = false;

                // Attempt rollback from safety backup if available
                if (!string.IsNullOrEmpty(safetyBackupPath) && File.Exists(safetyBackupPath))
                {
                    try
                    {
                        RollbackFromSafetyBackup(safetyBackupPath);
                        ReloadServices();
                        rolledBack = true;
                    }
                    catch (Exception rollEx)
                    {
                        DebugLog.Write($"[BackupService] Rollback also failed: {rollEx.Message}");
                    }
                }

                return RestoreResult.Fail($"Restore failed: {ex.Message}", rolledBack: rolledBack, safetyBackupPath: safetyBackupPath);
            }
            finally
            {
                if (stagingDir != null && Directory.Exists(stagingDir))
                {
                    try { Directory.Delete(stagingDir, recursive: true); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Deletes a backup archive and triggers notification.
    /// </summary>
    public bool DeleteBackup(string backupFilePath)
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(backupFilePath))
                {
                    File.Delete(backupFilePath);
                    BackupsChanged?.Invoke();
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[BackupService] Failed to delete backup: {ex.Message}");
            }
            return false;
        }
    }

    private void PruneOldBackups()
    {
        try
        {
            if (!Directory.Exists(_backupsDir)) return;

            // Only prune normal user backups, keep safety snapshots untouched or separate
            var normalBackups = Directory.GetFiles(_backupsDir, "backup_*.zip")
                                         .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                         .ToList();

            if (normalBackups.Count > MaxBackupsRetained)
            {
                var toDelete = normalBackups.Skip(MaxBackupsRetained);
                foreach (var file in toDelete)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BackupService] Error pruning old backups: {ex.Message}");
        }
    }

    private void FlushServices()
    {
        try { _taskService?.Save(); } catch { }
        try { _notesService?.Save(); } catch { }
        try { _settingsService?.Save(); } catch { }
        try { _focusHistoryService?.Save(); } catch { }
    }

    private void ReloadServices()
    {
        try { _taskService?.Load(); } catch { }
        try { _notesService?.Load(); } catch { }
        try { _settingsService?.Load(); } catch { }
        try { _focusHistoryService?.Load(); } catch { }
    }

    private void RollbackFromSafetyBackup(string safetyZipPath)
    {
        using var archive = ZipFile.OpenRead(safetyZipPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName == "manifest.json") continue;
            if (!DataFileNames.Contains(entry.FullName)) continue;

            string targetPath = Path.Combine(_appDataDir, entry.FullName);
            string tempPath = targetPath + ".rollback.tmp." + Guid.NewGuid().ToString("N");
            entry.ExtractToFile(tempPath, overwrite: true);
            File.Move(tempPath, targetPath, overwrite: true);
        }
    }

    private static string TestDeserialization(string stagingDir, IEnumerable<BackupFileEntry> files)
    {
        foreach (var file in files)
        {
            string path = Path.Combine(stagingDir, file.Name);
            if (!File.Exists(path)) continue;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                switch (file.Name)
                {
                    case "tasks.json":
                        // Test TaskDocument or raw array
                        try
                        {
                            var doc = JsonSerializer.Deserialize<TaskDocument>(json, JsonOpts);
                            if (doc?.Tasks == null)
                            {
                                var rawList = JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOpts);
                                if (rawList == null) return "tasks.json could not be parsed as TaskDocument or List<TaskItem>";
                            }
                        }
                        catch
                        {
                            var rawList = JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOpts);
                            if (rawList == null) return "tasks.json could not be parsed";
                        }
                        break;

                    case "notes.json":
                        var notes = JsonSerializer.Deserialize<List<NoteItem>>(json, JsonOpts);
                        if (notes == null) return "notes.json deserialized to null";
                        break;

                    case "settings.json":
                        var settings = JsonSerializer.Deserialize<AppSettings>(json, SettingsService.Options);
                        if (settings == null) return "settings.json deserialized to null";
                        break;

                    case "focus_history.json":
                        try
                        {
                            var historyDoc = JsonSerializer.Deserialize<FocusHistoryDocument>(json, JsonOpts);
                            if (historyDoc?.Sessions == null)
                            {
                                var rawSessions = JsonSerializer.Deserialize<List<FocusSessionRecord>>(json, JsonOpts);
                                if (rawSessions == null) return "focus_history.json deserialized to null";
                            }
                        }
                        catch
                        {
                            var rawSessions = JsonSerializer.Deserialize<List<FocusSessionRecord>>(json, JsonOpts);
                            if (rawSessions == null) return "focus_history.json deserialized to null";
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                return $"Error deserializing {file.Name}: {ex.Message}";
            }
        }

        return "";
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return ComputeSha256(stream);
    }

    private static string ComputeSha256(Stream stream)
    {
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
