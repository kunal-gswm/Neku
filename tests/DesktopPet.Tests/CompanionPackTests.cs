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

    [Fact]
    public void Personal_pack_is_never_embedded_in_assembly_resources()
    {
        var asm = typeof(AssetSource).Assembly;
        var embedded = asm.GetManifestResourceNames();
        Assert.DoesNotContain(embedded, name => name.Contains("personal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SettingsService_loads_and_persists_personal_companion()
    {
        var svc = new SettingsService();
        svc.Load();
        svc.Current.ActivePet = "neku-personal-local";
        svc.Save();

        var reloaded = new SettingsService();
        reloaded.Load();
        Assert.Equal("neku-personal-local", reloaded.Current.ActivePet);
    }

    [Fact]
    public void DiscoverPacks_returns_neku_first_and_includes_standard_cats()
    {
        var packs = AssetSource.DiscoverPacks();
        Assert.NotEmpty(packs);

        // Primary companion must be neku
        Assert.Equal("neku", packs[0].Id);
        Assert.Equal("Neku (Goblin companion)", packs[0].DisplayName);
        Assert.True(packs[0].IsValid);

        // Standard cats must be included
        Assert.Contains(packs, p => p.Id == "cat");
        Assert.Contains(packs, p => p.Id == "cat-orange");
        Assert.Contains(packs, p => p.Id == "cat-blue");

        // Every discovered pack must have a non-empty display name
        Assert.All(packs, p => Assert.False(string.IsNullOrWhiteSpace(p.DisplayName)));
    }

    [Fact]
    public void ValidatePack_validates_builtin_packs_successfully()
    {
        var (nekuValid, nekuError) = AssetSource.ValidatePack("neku");
        Assert.True(nekuValid, $"Neku pack failed validation: {nekuError}");
        Assert.Null(nekuError);

        var (catValid, catError) = AssetSource.ValidatePack("cat");
        Assert.True(catValid, $"Cat pack failed validation: {catError}");
        Assert.Null(catError);
    }

    [Fact]
    public void ValidatePack_fails_for_nonexistent_and_empty_packs()
    {
        var (emptyValid, emptyError) = AssetSource.ValidatePack("");
        Assert.False(emptyValid);
        Assert.NotNull(emptyError);

        var (fakeValid, fakeError) = AssetSource.ValidatePack("definitely-nonexistent-pack-999");
        Assert.False(fakeValid);
        Assert.NotNull(fakeError);
        Assert.Contains("not found", fakeError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateService_targets_kunal_gswm_neku_repository()
    {
        Assert.Equal("kunal-gswm", UpdateService.Owner);
        Assert.Equal("Neku", UpdateService.Repo);
        Assert.Equal("https://api.github.com/repos/kunal-gswm/Neku/releases/latest", UpdateService.LatestReleaseUrl);
    }

    [Fact]
    public void AppVersion_is_bumped_to_0_2_1()
    {
        Assert.Equal("v0.2.1", AppVersion.Display);
        Assert.Equal(0, AppVersion.Current.Major);
        Assert.Equal(2, AppVersion.Current.Minor);
        Assert.Equal(1, AppVersion.Current.Build);
    }
}
