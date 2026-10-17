using System.IO;
using System.Text.Json;
using DesktopPet.Engine;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class CompanionPackTests
{
    [Fact]
    public void PetManifest_parses_valid_json()
    {
        string json = """
        {
          "name": "test_companion",
          "sheet": "spritesheet.png",
          "cellWidth": 128,
          "cellHeight": 128,
          "columns": 4,
          "scale": 1.0,
          "animations": {
            "idle": { "frames": [0, 1], "fps": 2.0, "loop": true },
            "yawn": { "frames": [2, 3], "fps": 1.5, "loop": false }
          }
        }
        """;

        var manifest = PetManifest.Parse(json, "in-memory");
        Assert.Equal("test_companion", manifest.Name);
        Assert.Equal(128, manifest.CellWidth);
        Assert.Equal(128, manifest.CellHeight);
        Assert.Equal(4, manifest.Columns);
        Assert.Equal(1.0, manifest.Scale);
        Assert.True(manifest.Animations.ContainsKey("idle"));
        Assert.True(manifest.Animations.ContainsKey("yawn"));
        Assert.False(manifest.Animations["yawn"].Loop);
    }

    [Fact]
    public void Builtin_neku_manifest_has_all_required_animations()
    {
        string text = AssetSource.ReadAllText("Assets/pets/neku/manifest.json");
        var manifest = PetManifest.Parse(text, "Assets/pets/neku/manifest.json");

        string[] required =
        [
            "idle", "walk", "sleep", "fall", "drag", "eat",
            "pet", "stretch", "typing", "play", "talk", "blink",
            "lookaround", "sit", "happy", "annoyed", "surprised"
        ];

        foreach (var req in required)
        {
            Assert.True(manifest.Animations.ContainsKey(req), $"Manifest is missing required animation: {req}");
            Assert.NotEmpty(manifest.Animations[req].Frames);
            Assert.True(manifest.Animations[req].Fps > 0);
        }
    }

    [Fact]
    public void Local_personal_pack_if_present_is_valid_and_complete()
    {
        if (!AssetSource.Exists("Assets/pets/neku-personal-local/manifest.json"))
        {
            // Local personal pack is excluded from public builds; skip if absent
            return;
        }

        string text = AssetSource.ReadAllText("Assets/pets/neku-personal-local/manifest.json");
        var manifest = PetManifest.Parse(text, "Assets/pets/neku-personal-local/manifest.json");

        Assert.Equal(128, manifest.CellWidth);
        Assert.Equal(128, manifest.CellHeight);
        Assert.Equal(4, manifest.Columns);

        string[] required =
        [
            "idle", "walk", "sleep", "fall", "drag", "eat",
            "pet", "stretch", "typing", "play", "talk", "blink",
            "lookaround", "sit", "happy", "annoyed", "surprised"
        ];

        foreach (var req in required)
        {
            Assert.True(manifest.Animations.ContainsKey(req), $"Personal pack missing animation: {req}");
            Assert.NotEmpty(manifest.Animations[req].Frames);
        }

        // Verify all frame indices are in bounds (0..47)
        foreach (var (name, anim) in manifest.Animations)
        {
            foreach (int idx in anim.Frames)
            {
                Assert.InRange(idx, 0, 47);
            }
        }
    }

    [Fact]
    public void ActivePet_persists_in_settings()
    {
        var settings = new AppSettings();
        settings.ActivePet = "neku-personal-local";

        string json = JsonSerializer.Serialize(settings, SettingsService.Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, SettingsService.Options);

        Assert.NotNull(restored);
        Assert.Equal("neku-personal-local", restored.ActivePet);
    }

    [Fact]
    public void Missing_pack_falls_back_safely_to_available_candidate()
    {
        // Test fallback resolution order: non-existent pack -> neku -> cat
        string missingPet = "non-existent-pack-" + Guid.NewGuid();
        string resolved = "";

        foreach (string candidate in new[] { missingPet, "neku", "cat" })
        {
            if (AssetSource.Exists($"Assets/pets/{candidate}/manifest.json"))
            {
                resolved = candidate;
                break;
            }
        }

        Assert.Equal("neku", resolved);
    }
}
