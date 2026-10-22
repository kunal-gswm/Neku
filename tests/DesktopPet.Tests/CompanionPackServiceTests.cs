using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.Engine;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class CompanionPackServiceTests : IDisposable
{
    private readonly string _testRoot;

    public CompanionPackServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "NekuCompanionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        CompanionPackService.SetCustomPacksDirectoryForTesting(Path.Combine(_testRoot, "custom_packs"));
    }

    public void Dispose()
    {
        CompanionPackService.SetCustomPacksDirectoryForTesting(null);
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch { }
    }

    private static byte[] CreateTestPngBytes(int width = 128, int height = 128)
    {
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static byte[] CreateTestBmpBytes(int width = 128, int height = 128)
    {
        int pixelDataSize = width * height * 4;
        int fileSize = 54 + pixelDataSize;
        byte[] bmp = new byte[fileSize];

        // BITMAPFILEHEADER
        bmp[0] = 0x42; bmp[1] = 0x4D; // "BM"
        BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10); // pixel data offset

        // BITMAPINFOHEADER
        BitConverter.GetBytes(40).CopyTo(bmp, 14); // header size
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26); // planes
        BitConverter.GetBytes((short)32).CopyTo(bmp, 28); // bpp
        BitConverter.GetBytes(0).CopyTo(bmp, 30); // compression BI_RGB
        BitConverter.GetBytes(pixelDataSize).CopyTo(bmp, 34);

        return bmp;
    }

    private string CreateValidPackDirectory(string packName = "anime_hero", int cellW = 64, int cellH = 64, int cols = 2)
    {
        string dir = Path.Combine(_testRoot, packName);
        Directory.CreateDirectory(dir);

        var manifest = new
        {
            name = packName,
            author = "Test Artist",
            version = "1.0.0",
            license = "CC-BY-4.0",
            category = "Anime",
            sheet = "sheet.png",
            cellWidth = cellW,
            cellHeight = cellH,
            columns = cols,
            scale = 1.0,
            animations = new Dictionary<string, object>
            {
                ["idle"] = new { frames = new[] { 0, 1 }, fps = 2.0, loop = true },
                ["walk"] = new { frames = new[] { 2, 3 }, fps = 4.0, loop = true }
            }
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllBytes(Path.Combine(dir, "sheet.png"), CreateTestPngBytes(cellW * cols, cellH * 2));

        return dir;
    }

    private string CreatePackZip(string zipName, Action<ZipArchive> populate)
    {
        string zipPath = Path.Combine(_testRoot, zipName);
        using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            populate(archive);
        }
        return zipPath;
    }

    [Fact]
    public void PetManifest_ParsesExtendedMetadataFields()
    {
        string json = """
        {
          "name": "dora_cat",
          "author": "FanCreator",
          "version": "1.2.0",
          "license": "Fan Art (Personal Use)",
          "sourceUrl": "https://example.com/art",
          "category": "Cartoon",
          "tags": ["blue", "robot", "cat"],
          "sheet": "sheet.png",
          "cellWidth": 64,
          "cellHeight": 64,
          "columns": 4,
          "scale": 1.5,
          "animations": {
            "idle": { "frames": [0, 1], "fps": 2.0, "loop": true }
          }
        }
        """;

        var manifest = PetManifest.Parse(json, "in-memory");
        Assert.Equal("dora_cat", manifest.Name);
        Assert.Equal("FanCreator", manifest.Author);
        Assert.Equal("1.2.0", manifest.Version);
        Assert.Equal("Fan Art (Personal Use)", manifest.License);
        Assert.Equal("https://example.com/art", manifest.SourceUrl);
        Assert.Equal("Cartoon", manifest.Category);
        Assert.NotNull(manifest.Tags);
        Assert.Equal(3, manifest.Tags.Length);
        Assert.Equal("robot", manifest.Tags[1]);
        Assert.Equal(1.5, manifest.Scale);
    }

    [Fact]
    public void ValidateDirectory_ValidPack_ReturnsSuccess()
    {
        string packDir = CreateValidPackDirectory("valid_robot", 64, 64, 2);
        var result = CompanionPackService.ValidateDirectory(packDir);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Manifest);
        Assert.Equal("valid_robot", result.Manifest.Name);
        Assert.Equal(128, result.ImageWidth);
        Assert.Equal(128, result.ImageHeight);
    }

    [Fact]
    public void ValidateDirectory_MissingManifest_Fails()
    {
        string emptyDir = Path.Combine(_testRoot, "empty_dir");
        Directory.CreateDirectory(emptyDir);

        var result = CompanionPackService.ValidateDirectory(emptyDir);
        Assert.False(result.IsValid);
        Assert.Contains("Missing 'manifest.json'", result.ErrorMessage);
    }

    [Fact]
    public void ValidateDirectory_MissingIdleAnimation_Fails()
    {
        string dir = Path.Combine(_testRoot, "no_idle");
        Directory.CreateDirectory(dir);

        var manifest = new
        {
            name = "no_idle",
            sheet = "sheet.png",
            cellWidth = 64,
            cellHeight = 64,
            columns = 2,
            animations = new Dictionary<string, object>
            {
                ["walk"] = new { frames = new[] { 0, 1 }, fps = 2.0, loop = true }
            }
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllBytes(Path.Combine(dir, "sheet.png"), CreateTestPngBytes(128, 128));

        var result = CompanionPackService.ValidateDirectory(dir);
        Assert.False(result.IsValid);
        Assert.Contains("idle", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDirectory_FrameIndexOutOfBounds_Fails()
    {
        string dir = Path.Combine(_testRoot, "out_of_bounds");
        Directory.CreateDirectory(dir);

        // 64x64 sheet with 64x64 cells = 1 cell total (index 0)
        var manifest = new
        {
            name = "bad_frames",
            sheet = "sheet.png",
            cellWidth = 64,
            cellHeight = 64,
            columns = 1,
            animations = new Dictionary<string, object>
            {
                ["idle"] = new { frames = new[] { 0, 99 }, fps = 2.0, loop = true }
            }
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllBytes(Path.Combine(dir, "sheet.png"), CreateTestPngBytes(64, 64));

        var result = CompanionPackService.ValidateDirectory(dir);
        Assert.False(result.IsValid);
        Assert.Contains("only has 1 cells", result.ErrorMessage);
    }

    [Fact]
    public void ValidateDirectory_UnsupportedImageFormat_Fails()
    {
        string dir = Path.Combine(_testRoot, "bad_format");
        Directory.CreateDirectory(dir);

        var manifest = new
        {
            name = "bad_format",
            sheet = "sheet.svg",
            cellWidth = 64,
            cellHeight = 64,
            columns = 2,
            animations = new Dictionary<string, object>
            {
                ["idle"] = new { frames = new[] { 0 }, fps = 2.0, loop = true }
            }
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllText(Path.Combine(dir, "sheet.svg"), "<svg></svg>");

        var result = CompanionPackService.ValidateDirectory(dir);
        Assert.False(result.IsValid);
        Assert.Contains("Unsupported image format", result.ErrorMessage);
    }

    [Fact]
    public void ImportZip_ValidArchive_InstallsPackSuccessfully()
    {
        string zipPath = CreatePackZip("hero.zip", archive =>
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(new
                {
                    name = "Mighty Hero",
                    author = "AnimeStudio",
                    sheet = "hero.png",
                    cellWidth = 64,
                    cellHeight = 64,
                    columns = 2,
                    animations = new Dictionary<string, object>
                    {
                        ["idle"] = new { frames = new[] { 0, 1 }, fps = 2.0, loop = true }
                    }
                }));
            }

            var sheetEntry = archive.CreateEntry("hero.png");
            using (var stream = sheetEntry.Open())
            {
                byte[] png = CreateTestPngBytes(128, 64);
                stream.Write(png, 0, png.Length);
            }
        });

        var importRes = CompanionPackService.ImportZip(zipPath, overwrite: true, customId: "mighty-hero-test");
        Assert.True(importRes.Success, importRes.ErrorMessage);
        Assert.Equal("mighty-hero-test", importRes.PackId);
        Assert.NotNull(importRes.TargetDirectory);
        Assert.True(Directory.Exists(importRes.TargetDirectory));

        // Clean up imported test pack
        CompanionPackService.DeletePack("mighty-hero-test");
        Assert.False(Directory.Exists(importRes.TargetDirectory));
    }

    [Fact]
    public void ImportZip_PathTraversalEntry_IsRejected()
    {
        string zipPath = CreatePackZip("traversal.zip", archive =>
        {
            var badEntry = archive.CreateEntry("../evil.png");
            using (var s = badEntry.Open())
            {
                s.WriteByte(1);
            }

            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write("{\"name\":\"bad\"}");
            }
        });

        var importRes = CompanionPackService.ImportZip(zipPath);
        Assert.False(importRes.Success);
        Assert.Contains("path traversal", importRes.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportZip_ExecutableFile_IsRejected()
    {
        string zipPath = CreatePackZip("executable.zip", archive =>
        {
            var exeEntry = archive.CreateEntry("malicious.exe");
            using (var s = exeEntry.Open())
            {
                s.WriteByte(0x4D); // MZ
            }

            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write("{\"name\":\"trojan\"}");
            }
        });

        var importRes = CompanionPackService.ImportZip(zipPath);
        Assert.False(importRes.Success);
        Assert.Contains("blocked executable", importRes.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeletePack_RefusesToDeleteProtectedBuiltinMascot()
    {
        Assert.True(CompanionPackService.IsProtectedPack("neku"));
        Assert.True(CompanionPackService.IsProtectedPack("cat"));
        Assert.True(CompanionPackService.IsProtectedPack("billu"));
        Assert.True(CompanionPackService.IsProtectedPack("neku-personal-local"));

        Assert.False(CompanionPackService.DeletePack("neku"));
        Assert.False(CompanionPackService.DeletePack("neku-personal-local"));
        Assert.False(CompanionPackService.DeletePack("cat"));
    }

    [Fact]
    public void SanitizePackId_ProducesSafeAlphanumericSlug()
    {
        Assert.Equal("shin-chan", CompanionPackService.SanitizePackId("Shin Chan!"));
        Assert.Equal("doraemon-v1", CompanionPackService.SanitizePackId("Doraemon (v1)"));
        Assert.Equal("super-hero", CompanionPackService.SanitizePackId("  Super_Hero  "));
    }

    [Fact]
    public void MissingAnimations_FallBackSafelyViaFallbackChain()
    {
        // Pack defines only "idle", but code plays "wave", "pout", "typing", "sleep"
        var manifest = new PetManifest
        {
            Name = "minimal_hero",
            Sheet = "sheet.png",
            CellWidth = 64,
            CellHeight = 64,
            Columns = 1,
            Animations = new Dictionary<string, AnimationDef>
            {
                ["idle"] = new AnimationDef { Frames = new[] { 0 }, Fps = 1, Loop = true }
            }
        };

        byte[] png = CreateTestPngBytes(64, 64);
        var animator = new SpriteAnimator(manifest, png);

        // Play arbitrary animations that are absent from manifest
        animator.Play("wave");
        Assert.Equal("idle", animator.CurrentAnimationName);

        animator.Play("celebrate");
        Assert.Equal("idle", animator.CurrentAnimationName);

        animator.Play("typing");
        Assert.Equal("idle", animator.CurrentAnimationName);

        animator.Play("sleep");
        Assert.Equal("idle", animator.CurrentAnimationName);

        // Tick returns a valid frame image without throwing
        var frame = animator.Tick(0.1);
        Assert.NotNull(frame);
    }

    [Fact]
    public void PersonalGirlCharacter_IsNeverExposedInAssemblyResources()
    {
        var asm = typeof(AssetSource).Assembly;
        var names = asm.GetManifestResourceNames();

        foreach (var name in names)
        {
            Assert.DoesNotContain("personal", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("neku-personal-local", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("../evil.png")]
    [InlineData("..\\evil.png")]
    [InlineData("folder/../../evil.png")]
    [InlineData("folder\\..\\..\\evil.png")]
    [InlineData("/root_traversal.png")]
    [InlineData("\\root_traversal.png")]
    [InlineData("C:\\rooted\\file.png")]
    [InlineData("C:/rooted/file.png")]
    [InlineData("D:drive_relative.png")]
    [InlineData("evil.png:stream")]
    [InlineData("evil.png\0bad.png")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsPathContained_RejectsDangerousOrMalformedPaths(string candidate)
    {
        string root = Path.Combine(_testRoot, "sandbox_root");
        Directory.CreateDirectory(root);

        bool contained = CompanionPackService.IsPathContained(root, candidate, out string resolved);
        Assert.False(contained);
        Assert.True(string.IsNullOrEmpty(resolved));
    }

    [Fact]
    public void IsPathContained_AcceptsSafeContainedSubpaths()
    {
        string root = Path.Combine(_testRoot, "valid_root");
        Directory.CreateDirectory(root);

        Assert.True(CompanionPackService.IsPathContained(root, "spritesheet.png", out string p1));
        Assert.Equal(Path.Combine(root, "spritesheet.png"), p1);

        Assert.True(CompanionPackService.IsPathContained(root, "subfolder/image.png", out string p2));
        Assert.Equal(Path.Combine(root, "subfolder", "image.png"), p2);

        Assert.True(CompanionPackService.IsPathContained(root, "nested\\deep\\manifest.json", out string p3));
        Assert.Equal(Path.Combine(root, "nested", "deep", "manifest.json"), p3);
    }

    [Fact]
    public void IsDestinationContained_RejectsSiblingDirectoryPrefixCollision()
    {
        // Root: C:\Temp\Neku_Pack_123
        // Sibling collision: C:\Temp\Neku_Pack_123-evil\payload.png
        string rootDir = Path.Combine(_testRoot, "Neku_Pack_123");
        string siblingDir = Path.Combine(_testRoot, "Neku_Pack_123-evil");
        string siblingFile = Path.Combine(siblingDir, "payload.png");

        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(siblingDir);

        // String StartsWith(rootDir) would naively evaluate to true, but path containment must reject it
        Assert.StartsWith(rootDir, siblingFile, StringComparison.OrdinalIgnoreCase);
        Assert.False(CompanionPackService.IsDestinationContained(rootDir, siblingFile));

        // Valid child within rootDir must succeed
        string validFile = Path.Combine(rootDir, "valid.png");
        Assert.True(CompanionPackService.IsDestinationContained(rootDir, validFile));
    }

    [Fact]
    public void ImportZip_BackslashTraversal_IsRejected()
    {
        string zipPath = CreatePackZip("backslash_traversal.zip", archive =>
        {
            var badEntry = archive.CreateEntry("..\\evil.png");
            using (var s = badEntry.Open())
            {
                s.WriteByte(1);
            }

            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write("{\"name\":\"bad\"}");
            }
        });

        var importRes = CompanionPackService.ImportZip(zipPath);
        Assert.False(importRes.Success);
        Assert.Contains("path traversal", importRes.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportZip_RootedAndDrivePrefixedEntries_AreRejected()
    {
        string zipPath = CreatePackZip("rooted_entries.zip", archive =>
        {
            var entry1 = archive.CreateEntry("/rooted_payload.png");
            using (var s = entry1.Open()) { s.WriteByte(1); }

            var entry2 = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(entry2.Open())) { writer.Write("{\"name\":\"bad\"}"); }
        });

        var importRes = CompanionPackService.ImportZip(zipPath);
        Assert.False(importRes.Success);
        Assert.Contains("path traversal", importRes.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportZip_DisallowedExtension_IsRejected()
    {
        string zipPath = CreatePackZip("disallowed_ext.zip", archive =>
        {
            var badEntry = archive.CreateEntry("payload.unknown");
            using (var s = badEntry.Open()) { s.WriteByte(1); }

            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open())) { writer.Write("{\"name\":\"bad\"}"); }
        });

        var importRes = CompanionPackService.ImportZip(zipPath);
        Assert.False(importRes.Success);
        Assert.Contains("unsupported file", importRes.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizePackId_HandlesReservedWindowsNames_AndLengthCapping()
    {
        Assert.Equal("con-pack", CompanionPackService.SanitizePackId("CON"));
        Assert.Equal("nul-pack", CompanionPackService.SanitizePackId("nul"));
        Assert.Equal("prn-pack", CompanionPackService.SanitizePackId("PRN"));
        Assert.Equal("aux-pack", CompanionPackService.SanitizePackId("aux"));
        Assert.Equal("com1-pack", CompanionPackService.SanitizePackId("COM1"));
        Assert.Equal("lpt1-pack", CompanionPackService.SanitizePackId("LPT1"));

        string longName = new string('a', 60);
        string sanitized = CompanionPackService.SanitizePackId(longName);
        Assert.True(sanitized.Length <= 40);
        Assert.StartsWith("aaaa", sanitized);
    }

    [Fact]
    public void ImportFolder_AtomicOverwrite_LeavesNoStaleFiles()
    {
        // 1. Initial pack with an extra stale file
        string initialPackDir = CreateValidPackDirectory("atomic_test_pack", 64, 64, 2);
        File.WriteAllText(Path.Combine(initialPackDir, "old_stale_notes.txt"), "stale documentation");

        var initialImport = CompanionPackService.ImportFolder(initialPackDir, overwrite: true, customId: "atomic-test-pack");
        Assert.True(initialImport.Success, initialImport.ErrorMessage);
        Assert.NotNull(initialImport.TargetDirectory);
        Assert.True(File.Exists(Path.Combine(initialImport.TargetDirectory, "old_stale_notes.txt")));

        // 2. Updated pack version WITHOUT the stale file
        string updatedPackDir = Path.Combine(_testRoot, "atomic_test_pack_v2");
        Directory.CreateDirectory(updatedPackDir);
        File.Copy(Path.Combine(initialPackDir, "manifest.json"), Path.Combine(updatedPackDir, "manifest.json"));
        File.Copy(Path.Combine(initialPackDir, "sheet.png"), Path.Combine(updatedPackDir, "sheet.png"));

        // Import with overwrite
        var updatedImport = CompanionPackService.ImportFolder(updatedPackDir, overwrite: true, customId: "atomic-test-pack");
        Assert.True(updatedImport.Success, updatedImport.ErrorMessage);

        // 3. Verify target directory contains only current files and ZERO stale files from previous version
        Assert.False(File.Exists(Path.Combine(updatedImport.TargetDirectory!, "old_stale_notes.txt")));
        Assert.True(File.Exists(Path.Combine(updatedImport.TargetDirectory!, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(updatedImport.TargetDirectory!, "sheet.png")));

        // Clean up
        CompanionPackService.DeletePack("atomic-test-pack");
    }

    [Fact]
    public void EnumerateSafeFiles_TraversesDirectoryWithoutReparsePoints()
    {
        string safeDir = Path.Combine(_testRoot, "safe_dir");
        Directory.CreateDirectory(safeDir);
        string subDir = Path.Combine(safeDir, "sub");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(safeDir, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(subDir, "b.png"), "image");

        var files = CompanionPackService.EnumerateSafeFiles(safeDir);
        Assert.Equal(2, files.Count);
        Assert.Contains(Path.Combine(safeDir, "a.txt"), files);
        Assert.Contains(Path.Combine(subDir, "b.png"), files);
    }

    [Fact]
    public void ConcurrentImports_ExecuteSafelyUnderLock()
    {
        int concurrency = 5;
        var results = new PackImportResult[concurrency];

        Parallel.For(0, concurrency, i =>
        {
            string packDir = CreateValidPackDirectory($"concurrent_pack_{i}", 64, 64, 2);
            results[i] = CompanionPackService.ImportFolder(packDir, overwrite: true, customId: $"concurrent-pack-{i}");
        });

        for (int i = 0; i < concurrency; i++)
        {
            Assert.True(results[i].Success, results[i].ErrorMessage);
            CompanionPackService.DeletePack($"concurrent-pack-{i}");
        }
    }

    [Fact]
    public void ValidateDirectory_ValidBmpSpritesheet_Succeeds()
    {
        string dir = Path.Combine(_testRoot, "valid_bmp_pack");
        Directory.CreateDirectory(dir);

        var manifest = new
        {
            name = "valid_bmp_pack",
            sheet = "sheet.bmp",
            cellWidth = 64,
            cellHeight = 64,
            columns = 2,
            animations = new Dictionary<string, object>
            {
                ["idle"] = new { frames = new[] { 0, 1, 2, 3 }, fps = 2.0, loop = true }
            }
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllBytes(Path.Combine(dir, "sheet.bmp"), CreateTestBmpBytes(128, 128));

        var result = CompanionPackService.ValidateDirectory(dir);
        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(128, result.ImageWidth);
        Assert.Equal(128, result.ImageHeight);
        Assert.Equal(4, result.MaxFramesCapacity);
    }

    [Fact]
    public void ValidateDirectory_BmpFrameIndexOutOfBounds_Fails()
    {
        string dir = Path.Combine(_testRoot, "bad_bmp_frames");
        Directory.CreateDirectory(dir);

        // 64x64 sheet with 64x64 cells = 1 cell total (index 0)
        var manifest = new
        {
            name = "bad_bmp_frames",
            sheet = "sheet.bmp",
            cellWidth = 64,
            cellHeight = 64,
            columns = 1,
            animations = new Dictionary<string, object>
            {
                ["idle"] = new { frames = new[] { 0, 99 }, fps = 2.0, loop = true }
            }
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllBytes(Path.Combine(dir, "sheet.bmp"), CreateTestBmpBytes(64, 64));

        var result = CompanionPackService.ValidateDirectory(dir);
        Assert.False(result.IsValid);
        Assert.Contains("only has 1 cells", result.ErrorMessage);
    }

    [Fact]
    public void ValidateDirectory_CorruptOrUnreadableImage_Fails()
    {
        string dir = Path.Combine(_testRoot, "corrupt_image_pack");
        Directory.CreateDirectory(dir);

        var manifest = new
        {
            name = "corrupt_image_pack",
            sheet = "sheet.png",
            cellWidth = 64,
            cellHeight = 64,
            columns = 1,
            animations = new Dictionary<string, object>
            {
                ["idle"] = new { frames = new[] { 0 }, fps = 2.0, loop = true }
            }
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        // Truncated invalid header
        File.WriteAllBytes(Path.Combine(dir, "sheet.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x00 });

        var result = CompanionPackService.ValidateDirectory(dir);
        Assert.False(result.IsValid);
        Assert.Contains("Could not determine valid dimensions", result.ErrorMessage);
    }

    [Fact]
    public void ImportZip_ExceedingDecompressedSingleFileLimit_IsRejected()
    {
        string zipPath = CreatePackZip("oversized_file.zip", archive =>
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write("{\"name\":\"oversized\",\"sheet\":\"sheet.png\",\"cellWidth\":64,\"cellHeight\":64,\"columns\":1,\"animations\":{\"idle\":{\"frames\":[0],\"fps\":2}}}");
            }

            var sheetEntry = archive.CreateEntry("sheet.png", CompressionLevel.Optimal);
            using var entryStream = sheetEntry.Open();
            byte[] chunk = new byte[64 * 1024];
            for (int i = 0; i < 336; i++) // 336 * 64KB = 21.5 MB uncompressed (> 20 MB)
            {
                entryStream.Write(chunk, 0, chunk.Length);
            }
        });

        var result = CompanionPackService.ImportZip(zipPath);
        Assert.False(result.Success);
        Assert.Contains("exceeds maximum allowed single-file size", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Staging directory must be cleaned up
        string packsDir = CompanionPackService.CustomPacksDirectory;
        Assert.Empty(Directory.GetDirectories(packsDir, ".zipstage_*"));
    }

    [Fact]
    public void ImportZip_ExceedingArchiveUncompressedLimit_IsRejected()
    {
        string zipPath = CreatePackZip("oversized_archive.zip", archive =>
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write("{\"name\":\"oversized_archive\",\"sheet\":\"sheet.png\",\"cellWidth\":64,\"cellHeight\":64,\"columns\":1,\"animations\":{\"idle\":{\"frames\":[0],\"fps\":2}}}");
            }

            var sheetEntry = archive.CreateEntry("sheet.png", CompressionLevel.Optimal);
            using (var es = sheetEntry.Open())
            {
                es.Write(CreateTestPngBytes(64, 64));
            }

            // Add three 18MB files = 54MB (> MaxArchiveUncompressedBytes = 50MB)
            // Each single file is 18MB (< MaxSingleFileBytes = 20MB)
            byte[] chunk = new byte[64 * 1024];
            for (int f = 1; f <= 3; f++)
            {
                var extraEntry = archive.CreateEntry($"extra_{f}.txt", CompressionLevel.Optimal);
                using var es = extraEntry.Open();
                for (int i = 0; i < 288; i++) // 288 * 64KB = 18.4MB
                {
                    es.Write(chunk, 0, chunk.Length);
                }
            }
        });

        var result = CompanionPackService.ImportZip(zipPath);
        Assert.False(result.Success);
        Assert.Contains("maximum permitted uncompressed size limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Staging directory must be cleaned up
        string packsDir = CompanionPackService.CustomPacksDirectory;
        Assert.Empty(Directory.GetDirectories(packsDir, ".zipstage_*"));
    }

    [Fact]
    public void ImportFolder_RollbackFailure_PreservesBackupDirectory()
    {
        string packsDir = CompanionPackService.CustomPacksDirectory;
        string packId = "preserved-rollback-pack";
        string targetDir = Path.Combine(packsDir, packId);
        Directory.CreateDirectory(targetDir);

        File.WriteAllText(Path.Combine(targetDir, "manifest.json"),
            "{\"name\":\"" + packId + "\",\"sheet\":\"sheet.png\",\"cellWidth\":64,\"cellHeight\":64,\"columns\":1,\"animations\":{\"idle\":{\"frames\":[0],\"fps\":2}}}");
        File.WriteAllBytes(Path.Combine(targetDir, "sheet.png"), CreateTestPngBytes(64, 64));
        File.WriteAllText(Path.Combine(targetDir, "user_original_data.txt"), "Important user data to preserve");

        string srcDir = Path.Combine(_testRoot, "update_source");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "manifest.json"),
            "{\"name\":\"" + packId + "\",\"sheet\":\"sheet.png\",\"cellWidth\":64,\"cellHeight\":64,\"columns\":1,\"animations\":{\"idle\":{\"frames\":[0],\"fps\":2}}}");
        File.WriteAllBytes(Path.Combine(srcDir, "sheet.png"), CreateTestPngBytes(64, 64));

        FileStream? lockHandle = null;
        CompanionPackService.SetOnDeploymentStartedForTesting(() =>
        {
            Directory.CreateDirectory(targetDir);
            string lockPath = Path.Combine(targetDir, "locked_rollback.tmp");
            lockHandle = new FileStream(lockPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            throw new IOException("Simulated disk error during companion deployment.");
        });

        PackImportResult result;
        try
        {
            result = CompanionPackService.ImportFolder(srcDir, overwrite: true);
        }
        finally
        {
            CompanionPackService.SetOnDeploymentStartedForTesting(null);
            lockHandle?.Dispose();
        }

        Assert.False(result.Success);
        Assert.Contains("Your original pack was preserved in", result.ErrorMessage);

        string[] remainingBackups = Directory.GetDirectories(packsDir, $".backup_{packId}_*");
        Assert.Single(remainingBackups);
        Assert.True(File.Exists(Path.Combine(remainingBackups[0], "user_original_data.txt")));

        foreach (var b in remainingBackups)
        {
            try { Directory.Delete(b, true); } catch { }
        }
    }

    [Theory]
    [InlineData(0xC0, true)]  // SOF0 (Baseline)
    [InlineData(0xC1, true)]  // SOF1 (Extended sequential)
    [InlineData(0xC2, true)]  // SOF2 (Progressive)
    [InlineData(0xC3, true)]  // SOF3 (Lossless)
    [InlineData(0xC4, false)] // DHT (Huffman Table) - NOT SOF
    [InlineData(0xC5, true)]  // SOF5
    [InlineData(0xC6, true)]  // SOF6
    [InlineData(0xC7, true)]  // SOF7
    [InlineData(0xC8, false)] // JPG extensions - NOT SOF
    [InlineData(0xC9, true)]  // SOF9
    [InlineData(0xCA, true)]  // SOF10
    [InlineData(0xCB, true)]  // SOF11
    [InlineData(0xCC, false)] // DAC - NOT SOF
    [InlineData(0xCD, true)]  // SOF13
    [InlineData(0xCE, true)]  // SOF14
    [InlineData(0xCF, true)]  // SOF15
    [InlineData(0xD8, false)] // SOI
    [InlineData(0xD9, false)] // EOI
    [InlineData(0xDA, false)] // SOS
    [InlineData(0xDB, false)] // DQT
    public void IsJpegStartOfFrameMarker_RecognizesOnlyActualSofMarkers(byte marker, bool expected)
    {
        Assert.Equal(expected, AssetSource.IsJpegStartOfFrameMarker(marker));
    }

    private static byte[] CreateJpegWithSof(byte sofMarker, int width, int height, int componentCount, int? overrideSegmentLength = null, bool truncateBuffer = false)
    {
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]); // SOI

        // Optional standard APP0 marker
        ms.Write([0xFF, 0xE0, 0x00, 0x10]);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("JFIF\0\x01\x01\0\0\x01\0\x01\0\0"));

        // SOF Marker
        ms.Write([0xFF, sofMarker]);

        int calculatedLength = 8 + 3 * componentCount;
        int declaredLength = overrideSegmentLength ?? calculatedLength;

        // Lf (2 bytes)
        ms.Write([(byte)(declaredLength >> 8), (byte)(declaredLength & 0xFF)]);
        // P (1 byte, sample precision = 8)
        ms.WriteByte(8);
        // Y (2 bytes, Height)
        ms.Write([(byte)(height >> 8), (byte)(height & 0xFF)]);
        // X (2 bytes, Width)
        ms.Write([(byte)(width >> 8), (byte)(width & 0xFF)]);
        // Nf (1 byte, Number of components)
        ms.WriteByte((byte)componentCount);

        // Component records (3 bytes per component: Ci, Hi/Vi, Tqi)
        for (int c = 1; c <= componentCount; c++)
        {
            ms.Write([(byte)c, 0x11, 0x00]);
        }

        // If extra padding was declared in overrideSegmentLength
        int extraBytes = declaredLength - calculatedLength;
        if (extraBytes > 0)
        {
            ms.Write(new byte[extraBytes]);
        }

        // SOS marker
        ms.Write([0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]);

        byte[] result = ms.ToArray();
        if (truncateBuffer)
        {
            // Truncate so buffer ends before full declared segment is available
            int truncatedLength = result.Length - (overrideSegmentLength ?? calculatedLength) + 5;
            return result[..truncatedLength];
        }

        return result;
    }

    [Fact]
    public void TryGetImageDimensions_JpegWithDhtAndDac_SkipsNonSofAndParsesSof()
    {
        // Construct JPEG bytes: SOI -> APP0 -> DHT (0xC4) -> DAC (0xCC) -> JPG (0xC8) -> SOF0 (0xC0, 128x64) -> SOS
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);

        // APP0 (length = 4, payload = 2 bytes)
        ms.Write([0xFF, 0xE0, 0x00, 0x04, 0x01, 0x02]);

        // DHT 0xC4 (length = 5, payload = 3 bytes)
        ms.Write([0xFF, 0xC4, 0x00, 0x05, 0x00, 0x01, 0x02]);

        // DAC 0xCC (length = 4, payload = 2 bytes)
        ms.Write([0xFF, 0xCC, 0x00, 0x04, 0xAA, 0xBB]);

        // JPG 0xC8 (length = 4, payload = 2 bytes)
        ms.Write([0xFF, 0xC8, 0x00, 0x04, 0xCC, 0xDD]);

        // SOF0 0xC0 (length = 11, precision=8, H=64, W=128, components=1, 3-byte component record)
        ms.Write([0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x40, 0x00, 0x80, 0x01, 0x01, 0x11, 0x00]);

        // SOS 0xDA
        ms.Write([0xFF, 0xDA]);

        var dims = AssetSource.TryGetImageDimensions(ms.ToArray());
        Assert.NotNull(dims);
        Assert.Equal(128, dims.Value.Width);
        Assert.Equal(64, dims.Value.Height);
    }

    [Fact]
    public void TryGetImageDimensions_JpegSofZeroComponents_ReturnsNull()
    {
        byte[] jpeg = CreateJpegWithSof(0xC0, 64, 64, componentCount: 0, overrideSegmentLength: 8);
        var dims = AssetSource.TryGetImageDimensions(jpeg);
        Assert.Null(dims);
    }

    [Fact]
    public void TryGetImageDimensions_JpegSofSegmentLengthTooShortForComponents_ReturnsNull()
    {
        // Declared 3 components requires 8 + 3 * 3 = 17 bytes, but declared length is only 14
        byte[] jpeg = CreateJpegWithSof(0xC0, 64, 64, componentCount: 3, overrideSegmentLength: 14);
        var dims = AssetSource.TryGetImageDimensions(jpeg);
        Assert.Null(dims);
    }

    [Fact]
    public void TryGetImageDimensions_JpegSofSegmentLengthLongerThanRequired_ReturnsNull()
    {
        // Declared 3 components requires 8 + 3 * 3 = 17 bytes, but declared length is 20
        byte[] jpeg = CreateJpegWithSof(0xC0, 64, 64, componentCount: 3, overrideSegmentLength: 20);
        var dims = AssetSource.TryGetImageDimensions(jpeg);
        Assert.Null(dims);
    }

    [Fact]
    public void TryGetImageDimensions_JpegSofTruncatedComponentRecords_ReturnsNull()
    {
        // Declared length is 17 for 3 components, but buffer is truncated before end of declared segment
        byte[] jpeg = CreateJpegWithSof(0xC0, 64, 64, componentCount: 3, truncateBuffer: true);
        var dims = AssetSource.TryGetImageDimensions(jpeg);
        Assert.Null(dims);
    }

    [Fact]
    public void TryGetImageDimensions_JpegSofValidOneComponent_ReturnsDimensions()
    {
        // 1 component (grayscale), Lf = 8 + 3 * 1 = 11
        byte[] jpeg = CreateJpegWithSof(0xC0, width: 128, height: 64, componentCount: 1);
        var dims = AssetSource.TryGetImageDimensions(jpeg);
        Assert.NotNull(dims);
        Assert.Equal(128, dims.Value.Width);
        Assert.Equal(64, dims.Value.Height);
    }

    [Fact]
    public void TryGetImageDimensions_JpegSofValidThreeComponents_ReturnsDimensions()
    {
        // 3 components (YCbCr/RGB), Lf = 8 + 3 * 3 = 17
        byte[] jpeg = CreateJpegWithSof(0xC0, width: 200, height: 100, componentCount: 3);
        var dims = AssetSource.TryGetImageDimensions(jpeg);
        Assert.NotNull(dims);
        Assert.Equal(200, dims.Value.Width);
        Assert.Equal(100, dims.Value.Height);
    }

    [Theory]
    [InlineData(0xC0)] // SOF0 (Baseline)
    [InlineData(0xC1)] // SOF1 (Extended sequential)
    [InlineData(0xC2)] // SOF2 (Progressive)
    [InlineData(0xC3)] // SOF3 (Lossless)
    [InlineData(0xC5)] // SOF5 (Differential sequential)
    [InlineData(0xC6)] // SOF6 (Differential progressive)
    [InlineData(0xC7)] // SOF7 (Differential lossless)
    [InlineData(0xC9)] // SOF9 (Extended sequential, arithmetic)
    [InlineData(0xCA)] // SOF10 (Progressive, arithmetic)
    [InlineData(0xCB)] // SOF11 (Lossless, arithmetic)
    [InlineData(0xCD)] // SOF13 (Differential sequential, arithmetic)
    [InlineData(0xCE)] // SOF14 (Differential progressive, arithmetic)
    [InlineData(0xCF)] // SOF15 (Differential lossless, arithmetic)
    public void TryGetImageDimensions_AllRecognizedSofMarkers_ParseDimensionsSuccessfully(byte sofMarker)
    {
        byte[] jpeg = CreateJpegWithSof(sofMarker, width: 160, height: 90, componentCount: 3);
        var dims = AssetSource.TryGetImageDimensions(jpeg);
        Assert.NotNull(dims);
        Assert.Equal(160, dims.Value.Width);
        Assert.Equal(90, dims.Value.Height);
    }

    [Fact]
    public void TryGetImageDimensions_JpegTruncatedAndMalformedHeaders_SafelyReturnNull()
    {
        // 1. Incomplete SOI
        Assert.Null(AssetSource.TryGetImageDimensions(new byte[] { 0xFF, 0xD8 }));

        // 2. SOI with marker but no length bytes
        Assert.Null(AssetSource.TryGetImageDimensions(new byte[] { 0xFF, 0xD8, 0xFF, 0xC0 }));

        // 3. Segment length < 2 (malformed)
        Assert.Null(AssetSource.TryGetImageDimensions(new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x01 }));

        // 4. Segment length claiming bytes beyond buffer end
        Assert.Null(AssetSource.TryGetImageDimensions(new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0xFF, 0x01, 0x02 }));

        // 5. SOF segment with length < 8 (truncated SOF header)
        Assert.Null(AssetSource.TryGetImageDimensions(new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x04, 0x08, 0x01 }));

        // 6. Zero width/height in SOF
        Assert.Null(AssetSource.TryGetImageDimensions(new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01 }));
    }
}
