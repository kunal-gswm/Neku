using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopPet.Engine;

namespace DesktopPet.Services;

/// <summary>
/// Result of a companion pack validation.
/// </summary>
public sealed record PackValidationResult(
    bool IsValid,
    string? ErrorMessage = null,
    PetManifest? Manifest = null,
    int? ImageWidth = null,
    int? ImageHeight = null,
    int? MaxFramesCapacity = null);

/// <summary>
/// Result of an import operation.
/// </summary>
public sealed record PackImportResult(
    bool Success,
    string? PackId = null,
    string? TargetDirectory = null,
    string? DisplayName = null,
    string? ErrorMessage = null,
    PetManifest? Manifest = null);

/// <summary>
/// Service responsible for validating, importing, staging, and deleting custom companion packs.
/// All user-imported packs live securely in %AppData%\Neku\Assets\pets\&lt;packId&gt;.
/// </summary>
public static class CompanionPackService
{
    public static event Action? PacksChanged;
    private static readonly object SyncLock = new();
    private static string? _customPacksDirectoryOverride;

    /// <summary>
    /// Base directory where user-imported custom companion packs reside.
    /// </summary>
    public static string CustomPacksDirectory
    {
        get
        {
            if (_customPacksDirectoryOverride != null)
            {
                if (!Directory.Exists(_customPacksDirectoryOverride))
                {
                    Directory.CreateDirectory(_customPacksDirectoryOverride);
                }
                return _customPacksDirectoryOverride;
            }

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku", "Assets", "pets");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }
    }

    /// <summary>Allows unit test suites to redirect custom packs to an isolated temporary sandbox.</summary>
    public static void SetCustomPacksDirectoryForTesting(string? path)
    {
        lock (SyncLock)
        {
            _customPacksDirectoryOverride = path;
        }
    }

    private static Action? _onDeploymentStartedForTesting;

    /// <summary>Allows unit test suites to simulate deployment failure for rollback verification.</summary>
    public static void SetOnDeploymentStartedForTesting(Action? action)
    {
        lock (SyncLock)
        {
            _onDeploymentStartedForTesting = action;
        }
    }

    /// <summary>Maximum allowable uncompressed archive size during ZIP import (50 MB).</summary>
    public const long MaxArchiveUncompressedBytes = 50 * 1024 * 1024;

    /// <summary>Maximum allowable single file size inside an imported pack (20 MB).</summary>
    public const long MaxSingleFileBytes = 20 * 1024 * 1024;

    /// <summary>Maximum allowable image width or height in pixels.</summary>
    public const int MaxImageDimension = 4096;

    /// <summary>Maximum allowable file entries inside an imported ZIP.</summary>
    public const int MaxArchiveEntries = 128;

    /// <summary>Strict allowlist of permitted file extensions in companion packs.</summary>
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".png", ".bmp", ".jpg", ".jpeg", ".txt", ".md"
    };

    /// <summary>Explicitly blocked executable extensions (defense-in-depth).</summary>
    public static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".scr", ".pif", ".com", ".sh", ".cpl", ".msi", ".jar", ".hta"
    };

    /// <summary>Windows reserved device names that cannot be used as folder names.</summary>
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
    };

    /// <summary>
    /// Sanitizes an input name into a safe, alphanumeric directory slug (e.g. "My Companion" -> "my-companion").
    /// Caps length and prevents Windows reserved device names.
    /// </summary>
    public static string SanitizePackId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "custom-pack-" + Guid.NewGuid().ToString("N")[..8];
        string slug = input.Trim().ToLowerInvariant().Replace('_', '-');
        slug = Regex.Replace(slug, @"[^a-z0-9-]+", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');

        if (slug.Length > 40)
        {
            slug = slug[..40].TrimEnd('-');
        }

        if (string.IsNullOrEmpty(slug) || slug == "." || slug == "..")
        {
            return "custom-pack-" + Guid.NewGuid().ToString("N")[..8];
        }

        if (ReservedWindowsNames.Contains(slug))
        {
            slug = slug + "-pack";
        }

        return slug;
    }

    /// <summary>
    /// Checks whether a given pack is a protected built-in mascot that cannot be removed or overwritten.
    /// </summary>
    public static bool IsProtectedPack(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId)) return true;
        string id = packId.Trim().ToLowerInvariant();
        return id switch
        {
            "neku" => true,
            "neku-personal-local" => true,
            "cat" => true,
            "cat-orange" => true,
            "billu" => true,
            "cat-silver" => true,
            "cat-cream" => true,
            "cat-chocolate" => true,
            "cat-blue" => true,
            "cat-janet" => true,
            _ => false
        };
    }

    /// <summary>
    /// Verifies that a proposed relative or candidate path is strictly contained within the specified root directory.
    /// Protects against directory traversal (../, ..\), rooted paths (/path, \path, C:\path, \\server\share),
    /// drive-relative paths (C:path), alternate data streams (path:stream), sibling-directory prefix collisions,
    /// and malformed path inputs.
    /// </summary>
    public static bool IsPathContained(string rootDirectory, string candidatePath, out string resolvedFullPath)
    {
        resolvedFullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        // 1. Reject drive prefixes, e.g. "C:foo", "D:\bar", "C:/baz"
        if (candidatePath.Length >= 2 && candidatePath[1] == ':')
        {
            return false;
        }

        // 2. Reject rooted paths (e.g. "/foo", "\foo", "\\server\share")
        if (Path.IsPathRooted(candidatePath) || candidatePath.StartsWith('/') || candidatePath.StartsWith('\\'))
        {
            return false;
        }

        // 3. Reject alternate data streams (e.g. "evil.png:stream")
        if (candidatePath.Contains(':'))
        {
            return false;
        }

        // 4. Reject explicit ".." traversal components in both slash styles
        string normalizedSlashes = candidatePath.Replace('\\', '/');
        var rawSegments = normalizedSlashes.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in rawSegments)
        {
            if (segment == "..")
            {
                return false;
            }
        }

        try
        {
            // 5. Normalize root directory with trailing separator
            string fullRoot = Path.GetFullPath(rootDirectory);
            string rootWithSep = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;

            // 6. Combine and normalize the candidate destination
            string combined = Path.Combine(fullRoot, candidatePath);
            string fullCandidate = Path.GetFullPath(combined);

            // 7. Calculate relative path from normalized root
            string relative = Path.GetRelativePath(fullRoot, fullCandidate);

            // Reject if relative path is rooted (e.g. across volume boundaries)
            if (Path.IsPathRooted(relative))
            {
                return false;
            }

            // Reject if relative path is ".." or starts with ".." + separator
            if (relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }

            // 8. Prevent sibling directory prefix collisions:
            // fullCandidate must either equal fullRoot or strictly start with rootWithSep (using OS-aware comparison)
            StringComparison pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!fullCandidate.Equals(fullRoot, pathComparison) &&
                !fullCandidate.StartsWith(rootWithSep, pathComparison))
            {
                return false;
            }

            resolvedFullPath = fullCandidate;
            return true;
        }
        catch
        {
            // Path.GetFullPath throws on invalid characters, wildcards, device names, etc.
            return false;
        }
    }

    /// <summary>
    /// Checks whether an absolute candidate path is strictly contained within the specified root directory.
    /// Prevents sibling-directory prefix collisions (e.g. C:\Temp\Root vs C:\Temp\Root-evil).
    /// </summary>
    public static bool IsDestinationContained(string rootDirectory, string absoluteDestinationPath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(absoluteDestinationPath))
        {
            return false;
        }

        try
        {
            string fullRoot = Path.GetFullPath(rootDirectory);
            string rootWithSep = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;

            string fullDest = Path.GetFullPath(absoluteDestinationPath);

            string relative = Path.GetRelativePath(fullRoot, fullDest);
            if (Path.IsPathRooted(relative))
            {
                return false;
            }

            if (relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }

            StringComparison pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return fullDest.Equals(fullRoot, pathComparison) || fullDest.StartsWith(rootWithSep, pathComparison);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether any directory or file component between the root and target path is a symbolic link or reparse point.
    /// </summary>
    public static bool HasReparsePointInPath(string fullRoot, string fullPath)
    {
        try
        {
            string current = fullPath;
            string root = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            while (!string.IsNullOrEmpty(current) && !current.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    var attr = File.GetAttributes(current);
                    if ((attr & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }

                string? parent = Path.GetDirectoryName(current);
                if (parent == null || parent.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = parent;
            }
        }
        catch
        {
            return true; // Fail closed if attributes cannot be read
        }
        return false;
    }

    /// <summary>
    /// Safely enumerates all regular files within a directory hierarchy, refusing to follow symbolic links or reparse points.
    /// </summary>
    public static List<string> EnumerateSafeFiles(string rootDirectory)
    {
        var safeFiles = new List<string>();
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return safeFiles;
        }

        var rootInfo = new DirectoryInfo(rootDirectory);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return safeFiles;
        }

        var queue = new Queue<DirectoryInfo>();
        queue.Enqueue(rootInfo);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            FileInfo[] files;
            try { files = current.GetFiles(); } catch { continue; }
            foreach (var file in files)
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue; // Skip symbolic link files
                }
                safeFiles.Add(file.FullName);
            }

            DirectoryInfo[] subdirs;
            try { subdirs = current.GetDirectories(); } catch { continue; }
            foreach (var sub in subdirs)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue; // Skip junction / symlink directories
                }
                queue.Enqueue(sub);
            }
        }

        return safeFiles;
    }

    /// <summary>
    /// Validates an unextracted or staged folder containing a companion pack.
    /// </summary>
    public static PackValidationResult ValidateDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return new PackValidationResult(false, "Directory does not exist.");
        }

        string manifestPath = Path.Combine(directoryPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new PackValidationResult(false, "Missing 'manifest.json' file in the companion pack directory.");
        }

        PetManifest manifest;
        try
        {
            string json = File.ReadAllText(manifestPath);
            manifest = PetManifest.Parse(json, manifestPath);
        }
        catch (Exception ex)
        {
            return new PackValidationResult(false, $"Malformed 'manifest.json': {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            return new PackValidationResult(false, "Manifest must specify a non-empty character 'name'.");
        }

        if (manifest.CellWidth <= 0 || manifest.CellHeight <= 0)
        {
            return new PackValidationResult(false, $"Invalid cell dimensions: {manifest.CellWidth}x{manifest.CellHeight}. Must be positive integers.");
        }

        if (manifest.Columns <= 0)
        {
            return new PackValidationResult(false, $"Invalid column count: {manifest.Columns}. Must be greater than 0.");
        }

        if (manifest.Scale <= 0 || manifest.Scale > 5.0)
        {
            return new PackValidationResult(false, $"Invalid display scale: {manifest.Scale}. Must be between 0.1 and 5.0.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Sheet))
        {
            return new PackValidationResult(false, "Manifest is missing the 'sheet' filename property.");
        }

        // Prevent traversal or malicious filenames in manifest.Sheet
        if (!IsPathContained(directoryPath, manifest.Sheet, out string sheetPath))
        {
            return new PackValidationResult(false, $"Spritesheet filename '{manifest.Sheet}' contains an invalid or escaping path.");
        }

        if (!File.Exists(sheetPath))
        {
            return new PackValidationResult(false, $"Spritesheet image '{manifest.Sheet}' was not found in the pack folder.");
        }

        string ext = Path.GetExtension(sheetPath).ToLowerInvariant();
        if (ext is not (".png" or ".bmp" or ".jpg" or ".jpeg"))
        {
            return new PackValidationResult(false, $"Unsupported image format '{ext}'. Only PNG, BMP, and JPG spritesheets are supported.");
        }

        byte[] sheetBytes;
        try
        {
            sheetBytes = File.ReadAllBytes(sheetPath);
            if (sheetBytes.Length == 0)
            {
                return new PackValidationResult(false, $"Spritesheet file '{manifest.Sheet}' is empty (0 bytes).");
            }
            if (sheetBytes.Length > MaxSingleFileBytes)
            {
                return new PackValidationResult(false,
                    $"Spritesheet file '{manifest.Sheet}' exceeds maximum file size limit ({MaxSingleFileBytes / (1024 * 1024)} MB).");
            }
        }
        catch (Exception ex)
        {
            return new PackValidationResult(false, $"Could not read spritesheet file: {ex.Message}");
        }

        var imageDims = AssetSource.TryGetImageDimensions(sheetBytes);
        if (imageDims == null || imageDims.Value.Width <= 0 || imageDims.Value.Height <= 0)
        {
            return new PackValidationResult(false,
                $"Could not determine valid dimensions for spritesheet '{manifest.Sheet}'. The image file may be corrupt or formatted incorrectly.");
        }

        int imgW = imageDims.Value.Width;
        int imgH = imageDims.Value.Height;

        if (imgW > MaxImageDimension || imgH > MaxImageDimension)
        {
            return new PackValidationResult(false,
                $"Spritesheet dimensions ({imgW}x{imgH}) exceed maximum allowed dimension ({MaxImageDimension}x{MaxImageDimension} px).");
        }

        int cols = imgW / manifest.CellWidth;
        int rows = imgH / manifest.CellHeight;
        if (cols <= 0 || rows <= 0)
        {
            return new PackValidationResult(false,
                $"Spritesheet dimensions ({imgW}x{imgH}) are smaller than cell size ({manifest.CellWidth}x{manifest.CellHeight}).");
        }

        if (manifest.Columns > cols)
        {
            return new PackValidationResult(false,
                $"Manifest columns ({manifest.Columns}) exceeds sheet width capacity ({cols} columns based on {imgW}px width and {manifest.CellWidth}px cell width).");
        }

        int maxFrames = manifest.Columns * rows;

        if (manifest.Animations == null || manifest.Animations.Count == 0)
        {
            return new PackValidationResult(false, "Manifest defines no animations.");
        }

        if (!manifest.Animations.TryGetValue("idle", out var idleAnim) || idleAnim.Frames == null || idleAnim.Frames.Length == 0)
        {
            return new PackValidationResult(false, "Pack must define at least one valid 'idle' animation with non-empty frames.");
        }

        foreach (var (animName, animDef) in manifest.Animations)
        {
            if (animDef.Frames == null || animDef.Frames.Length == 0)
            {
                return new PackValidationResult(false, $"Animation '{animName}' contains no frames.");
            }

            if (animDef.Fps <= 0)
            {
                return new PackValidationResult(false, $"Animation '{animName}' must have a positive fps (got {animDef.Fps}).");
            }

            foreach (int idx in animDef.Frames)
            {
                if (idx < 0)
                {
                    return new PackValidationResult(false, $"Animation '{animName}' contains negative frame index {idx}.");
                }

                int col = idx % manifest.Columns;
                int row = idx / manifest.Columns;
                if ((col + 1) * manifest.CellWidth > imgW || (row + 1) * manifest.CellHeight > imgH || idx >= maxFrames)
                {
                    return new PackValidationResult(false,
                        $"Animation '{animName}' references frame {idx}, but spritesheet ({imgW}x{imgH}) only has {maxFrames} cells.");
                }
            }
        }

        return new PackValidationResult(true, null, manifest, imgW, imgH, maxFrames);
    }

    /// <summary>
    /// Imports a companion pack from an uncompressed local directory.
    /// Copies all valid files into a staging sandbox, validates the staged pack,
    /// and atomically moves it into %AppData%\Neku\Assets\pets\&lt;packId&gt; with safe rollback.
    /// </summary>
    public static PackImportResult ImportFolder(string sourceDirectory, bool overwrite = false, string? customId = null)
    {
        lock (SyncLock)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            {
                return new PackImportResult(false, null, null, null, "Source folder does not exist.");
            }

            // Check if sourceDirectory itself is a reparse point
            try
            {
                var srcAttr = File.GetAttributes(sourceDirectory);
                if ((srcAttr & FileAttributes.ReparsePoint) != 0)
                {
                    return new PackImportResult(false, null, null, null, "Source folder cannot be a symbolic link or reparse point.");
                }
            }
            catch (Exception ex)
            {
                return new PackImportResult(false, null, null, null, $"Could not inspect source folder: {ex.Message}");
            }

            var validation = ValidateDirectory(sourceDirectory);
            if (!validation.IsValid || validation.Manifest == null)
            {
                return new PackImportResult(false, null, null, null, validation.ErrorMessage ?? "Validation failed.");
            }

            string packId = SanitizePackId(customId ?? validation.Manifest.Name);
            if (IsProtectedPack(packId))
            {
                packId = packId + "-custom";
            }

            string targetDir = Path.Combine(CustomPacksDirectory, packId);
            if (Directory.Exists(targetDir) && !overwrite)
            {
                return new PackImportResult(false, packId, targetDir, validation.Manifest.Name,
                    $"A companion pack with ID '{packId}' already exists. Enable overwrite to replace it.");
            }

            string stagingDir = Path.Combine(CustomPacksDirectory, $".staging_{Guid.NewGuid():N}");
            string backupDir = Path.Combine(CustomPacksDirectory, $".backup_{packId}_{Guid.NewGuid():N}");
            bool hadExisting = Directory.Exists(targetDir);
            bool rollbackFailed = false;

            try
            {
                Directory.CreateDirectory(stagingDir);

                // Safely copy allowed files from source to staging (skipping symlinks/reparse points)
                var safeFiles = EnumerateSafeFiles(sourceDirectory);
                foreach (string file in safeFiles)
                {
                    string rel = Path.GetRelativePath(sourceDirectory, file);
                    if (!IsPathContained(stagingDir, rel, out string destFile))
                    {
                        continue;
                    }

                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (BlockedExtensions.Contains(ext) || !AllowedExtensions.Contains(ext))
                    {
                        continue; // Skip unallowed or blocked files
                    }

                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length > MaxSingleFileBytes)
                    {
                        return new PackImportResult(false, packId, null, validation.Manifest.Name,
                            $"File '{rel}' exceeds maximum permitted size ({MaxSingleFileBytes / (1024 * 1024)} MB).");
                    }

                    string parentDir = Path.GetDirectoryName(destFile)!;
                    if (HasReparsePointInPath(stagingDir, parentDir))
                    {
                        return new PackImportResult(false, packId, null, validation.Manifest.Name,
                            $"Target directory for '{rel}' contains a symbolic link or reparse point.");
                    }

                    Directory.CreateDirectory(parentDir);
                    File.Copy(file, destFile, overwrite: true);
                }

                // Verify staging directory validation
                var stageValidation = ValidateDirectory(stagingDir);
                if (!stageValidation.IsValid)
                {
                    return new PackImportResult(false, packId, null, validation.Manifest.Name,
                        $"Staged pack validation failed: {stageValidation.ErrorMessage}");
                }

                // Atomic deployment: move existing target to backup, copy clean staged files, purge backup
                if (hadExisting)
                {
                    Directory.Move(targetDir, backupDir);
                }

                _onDeploymentStartedForTesting?.Invoke();

                Directory.CreateDirectory(targetDir);
                foreach (string file in EnumerateSafeFiles(stagingDir))
                {
                    string rel = Path.GetRelativePath(stagingDir, file);
                    string destFile = Path.Combine(targetDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                    File.Copy(file, destFile, overwrite: true);
                }

                // Clean up backup on success
                if (hadExisting && Directory.Exists(backupDir))
                {
                    try { Directory.Delete(backupDir, true); } catch { }
                }

                PacksChanged?.Invoke();
                return new PackImportResult(true, packId, targetDir, validation.Manifest.Name, null, validation.Manifest);
            }
            catch (Exception ex)
            {
                // Rollback on error: restore backup if target was deleted/moved
                string errorMessage = $"Import failed: {ex.Message}";
                if (hadExisting && Directory.Exists(backupDir))
                {
                    try
                    {
                        if (Directory.Exists(targetDir))
                        {
                            Directory.Delete(targetDir, true);
                        }
                        Directory.Move(backupDir, targetDir);
                    }
                    catch (Exception rollbackEx)
                    {
                        rollbackFailed = true;
                        errorMessage = $"Import failed: {ex.Message}. Rollback also failed: {rollbackEx.Message}. Your original pack was preserved in '{backupDir}'.";
                    }
                }
                return new PackImportResult(false, packId, null, validation.Manifest.Name, errorMessage);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingDir))
                    {
                        Directory.Delete(stagingDir, true);
                    }
                }
                catch { }

                try
                {
                    if (!rollbackFailed && Directory.Exists(backupDir))
                    {
                        Directory.Delete(backupDir, true);
                    }
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Imports a companion pack from a ZIP archive.
    /// Inspects and validates archive entries, rejects path traversal and unallowed files,
    /// and stages safely before atomic deployment.
    /// </summary>
    public static PackImportResult ImportZip(string zipFilePath, bool overwrite = false, string? customId = null)
    {
        lock (SyncLock)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
            {
                return new PackImportResult(false, null, null, null, "ZIP archive file not found.");
            }

            string stagingDir = Path.Combine(CustomPacksDirectory, $".zipstage_{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(stagingDir);

                using (var archive = ZipFile.OpenRead(zipFilePath))
                {
                    if (archive.Entries.Count == 0)
                    {
                        return new PackImportResult(false, null, null, null, "ZIP archive is empty.");
                    }

                    if (archive.Entries.Count > MaxArchiveEntries)
                    {
                        return new PackImportResult(false, null, null, null,
                            $"Archive contains too many files ({archive.Entries.Count} > {MaxArchiveEntries}).");
                    }

                    long totalUncompressedBytes = 0;

                    // Pass 1: Pre-validation of all entries in memory before extracting anything
                    foreach (var entry in archive.Entries)
                    {
                        // Strict path-aware containment check
                        if (!IsPathContained(stagingDir, entry.FullName, out string destPath))
                        {
                            return new PackImportResult(false, null, null, null,
                                $"Dangerous path traversal detected in ZIP entry: '{entry.FullName}'");
                        }

                        bool isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || string.IsNullOrEmpty(entry.Name);
                        if (!isDirectory)
                        {
                            if (entry.Length >= 0)
                            {
                                totalUncompressedBytes += entry.Length;
                                if (totalUncompressedBytes > MaxArchiveUncompressedBytes)
                                {
                                    return new PackImportResult(false, null, null, null,
                                        $"Archive exceeds maximum permitted uncompressed size limit ({MaxArchiveUncompressedBytes / (1024 * 1024)} MB).");
                                }

                                if (entry.Length > MaxSingleFileBytes)
                                {
                                    return new PackImportResult(false, null, null, null,
                                        $"Entry '{entry.FullName}' exceeds maximum allowed single-file size ({MaxSingleFileBytes / (1024 * 1024)} MB).");
                                }
                            }

                            string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                            if (BlockedExtensions.Contains(ext))
                            {
                                return new PackImportResult(false, null, null, null,
                                    $"ZIP contains blocked executable/script entry: '{entry.FullName}'");
                            }
                            if (!AllowedExtensions.Contains(ext))
                            {
                                return new PackImportResult(false, null, null, null,
                                    $"ZIP contains unsupported file entry: '{entry.FullName}'. Only images and pack metadata are permitted.");
                            }
                        }
                    }

                    // Pass 2: Extract verified entries to staging sandbox with bounded streaming extraction
                    long totalExtractedBytes = 0;
                    byte[] extractionBuffer = new byte[8192];

                    foreach (var entry in archive.Entries)
                    {
                        if (!IsPathContained(stagingDir, entry.FullName, out string destPath))
                        {
                            return new PackImportResult(false, null, null, null,
                                $"Dangerous path traversal detected in ZIP entry: '{entry.FullName}'");
                        }

                        bool isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || string.IsNullOrEmpty(entry.Name);
                        if (isDirectory)
                        {
                            if (HasReparsePointInPath(stagingDir, destPath))
                            {
                                return new PackImportResult(false, null, null, null,
                                    $"Directory entry '{entry.FullName}' resolves through a symbolic link or reparse point.");
                            }
                            Directory.CreateDirectory(destPath);
                        }
                        else
                        {
                            string parentDir = Path.GetDirectoryName(destPath)!;
                            if (HasReparsePointInPath(stagingDir, parentDir))
                            {
                                return new PackImportResult(false, null, null, null,
                                    $"Destination directory for '{entry.FullName}' contains a symbolic link or reparse point.");
                            }
                            Directory.CreateDirectory(parentDir);

                            if (File.Exists(destPath) && (File.GetAttributes(destPath) & FileAttributes.ReparsePoint) != 0)
                            {
                                return new PackImportResult(false, null, null, null,
                                    $"Destination file '{entry.FullName}' is a symbolic link or reparse point.");
                            }

                            using (var entryStream = entry.Open())
                            using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                long entryBytesWritten = 0;
                                int bytesRead;
                                while ((bytesRead = entryStream.Read(extractionBuffer, 0, extractionBuffer.Length)) > 0)
                                {
                                    entryBytesWritten += bytesRead;
                                    totalExtractedBytes += bytesRead;

                                    if (entryBytesWritten > MaxSingleFileBytes)
                                    {
                                        throw new InvalidDataException(
                                            $"Entry '{entry.FullName}' exceeds maximum allowed single-file size ({MaxSingleFileBytes / (1024 * 1024)} MB).");
                                    }

                                    if (totalExtractedBytes > MaxArchiveUncompressedBytes)
                                    {
                                        throw new InvalidDataException(
                                            $"Archive exceeds maximum permitted uncompressed size limit ({MaxArchiveUncompressedBytes / (1024 * 1024)} MB).");
                                    }

                                    destStream.Write(extractionBuffer, 0, bytesRead);
                                }
                            }
                        }
                    }
                }

                // Locate manifest.json (at root of stagingDir, or in single root subdirectory)
                string packContentDir = stagingDir;
                if (!File.Exists(Path.Combine(stagingDir, "manifest.json")))
                {
                    var subdirs = Directory.GetDirectories(stagingDir);
                    if (subdirs.Length == 1 && File.Exists(Path.Combine(subdirs[0], "manifest.json")))
                    {
                        packContentDir = subdirs[0];
                    }
                    else
                    {
                        var found = Directory.GetFiles(stagingDir, "manifest.json", SearchOption.AllDirectories);
                        if (found.Length == 1)
                        {
                            packContentDir = Path.GetDirectoryName(found[0])!;
                        }
                        else
                        {
                            return new PackImportResult(false, null, null, null, "Archive must contain a single 'manifest.json' file.");
                        }
                    }
                }

                return ImportFolder(packContentDir, overwrite, customId ?? Path.GetFileNameWithoutExtension(zipFilePath));
            }
            catch (Exception ex)
            {
                return new PackImportResult(false, null, null, null, $"Failed to extract ZIP archive: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingDir))
                    {
                        Directory.Delete(stagingDir, true);
                    }
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Removes a user-imported companion pack from disk.
    /// Strictly refuses to delete built-in or personal local packs.
    /// </summary>
    public static bool DeletePack(string packId)
    {
        lock (SyncLock)
        {
            if (string.IsNullOrWhiteSpace(packId)) return false;
            if (IsProtectedPack(packId)) return false;

            string targetDir = Path.Combine(CustomPacksDirectory, packId);
            if (!Directory.Exists(targetDir)) return false;

            try
            {
                Directory.Delete(targetDir, true);
                PacksChanged?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
