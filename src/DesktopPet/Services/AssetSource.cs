using System.IO;
using System.Reflection;
using DesktopPet.Engine;

namespace DesktopPet.Services;

/// <summary>
/// Where the pet's art comes from.
///
/// Assets are compiled into the executable so a release is genuinely one file — a published
/// Neku.exe with no Assets folder beside it still has all seven cats. A matching file on
/// disk always wins, though, which keeps the edit-and-rerun loop working during development and
/// lets anyone drop in a custom pet pack without rebuilding.
/// </summary>
public static class AssetSource
{
    private static readonly Assembly Asm = typeof(AssetSource).Assembly;

    /// <summary>Embedded resources are named by their project path with separators as dots.</summary>
    private static string ResourceName(string relativePath) =>
        "DesktopPet." + relativePath.Replace('\\', '/').Replace('/', '.');

    public static string DiskPath(string relativePath) =>
        Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static IEnumerable<string> CandidateDiskPaths(string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);

        // 1. Directly in AppContext.BaseDirectory (portable deployment beside exe)
        yield return Path.Combine(AppContext.BaseDirectory, normalized);

        // 2. User-customizable AppData directory (%AppData%\Neku\...)
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku", normalized);
        yield return appData;

        // 3. Search upward from AppContext.BaseDirectory (up to 8 parent levels)
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++)
        {
            yield return Path.Combine(dir.FullName, normalized);
            yield return Path.Combine(dir.FullName, "src", "DesktopPet", normalized);
            dir = dir.Parent;
        }
    }

    /// <summary>Returns the resolved source location (disk path or embedded resource name) for diagnostics.</summary>
    public static string ResolveSource(string relativePath)
    {
        try
        {
            foreach (string disk in CandidateDiskPaths(relativePath))
            {
                if (File.Exists(disk)) return disk;
            }
        }
        catch { }

        string res = ResourceName(relativePath);
        if (Asm.GetManifestResourceNames().Contains(res))
            return $"[embedded:{res}]";

        return "[not found]";
    }

    /// <summary>Open an asset, preferring a file on disk over the embedded copy. Null if neither exists.</summary>
    public static Stream? TryOpen(string relativePath)
    {
        try
        {
            foreach (string disk in CandidateDiskPaths(relativePath))
            {
                if (File.Exists(disk)) return File.OpenRead(disk);
            }
        }
        catch { /* unreadable on disk — fall through to the embedded copy */ }

        return Asm.GetManifestResourceStream(ResourceName(relativePath));
    }

    /// <summary>Open an asset or throw — for assets whose absence is a broken build.</summary>
    public static Stream Open(string relativePath) =>
        TryOpen(relativePath) ?? throw new FileNotFoundException($"Missing asset: {relativePath}");

    public static bool Exists(string relativePath)
    {
        using var s = TryOpen(relativePath);
        return s != null;
    }

    /// <summary>Read an asset fully into memory. Callers that need seeking (image decoders) want this.</summary>
    public static byte[] ReadAllBytes(string relativePath)
    {
        using var s = Open(relativePath);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static string ReadAllText(string relativePath)
    {
        using var s = Open(relativePath);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>
    /// Every pet pack that can be loaded, by folder name (e.g. "cat", "cat-orange"): the built-in
    /// embedded packs plus anything the user has dropped into Assets\pets next to the exe.
    /// </summary>
    public static IReadOnlyList<string> ListPets()
    {
        var pets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        const string prefix = "DesktopPet.Assets.pets.";
        const string suffix = ".manifest.json";
        foreach (var name in Asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            pets.Add(name[prefix.Length..^suffix.Length]);
        }

        try
        {
            foreach (var petDir in CandidateDiskPaths("Assets/pets"))
            {
                if (Directory.Exists(petDir))
                {
                    foreach (var d in Directory.EnumerateDirectories(petDir))
                    {
                        if (File.Exists(Path.Combine(d, "manifest.json")))
                            pets.Add(Path.GetFileName(d));
                    }
                }
            }
        }
        catch { /* best-effort */ }

        return pets.ToList();
    }

    /// <summary>
    /// Validates a companion pack by ID, verifying that its manifest exists and is valid JSON,
    /// cell dimensions and columns are positive, required 'idle' animation exists with frames,
    /// frame indices are non-negative, and the spritesheet image exists and is readable.
    /// </summary>
    public static (bool IsValid, string? Error) ValidatePack(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
            return (false, "Pack ID cannot be empty");

        string dir = $"Assets/pets/{packId}";
        string manifestRel = $"{dir}/manifest.json";

        if (!Exists(manifestRel))
            return (false, $"Manifest not found at {manifestRel}");

        PetManifest manifest;
        try
        {
            string json = ReadAllText(manifestRel);
            manifest = PetManifest.Parse(json, manifestRel);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid manifest: {ex.Message}");
        }

        if (manifest.CellWidth <= 0 || manifest.CellHeight <= 0)
            return (false, $"Invalid cell dimensions: {manifest.CellWidth}x{manifest.CellHeight}");

        if (manifest.Columns <= 0)
            return (false, $"Invalid column count: {manifest.Columns}");

        if (string.IsNullOrWhiteSpace(manifest.Sheet))
            return (false, "Manifest sheet filename is missing");

        string sheetRel = $"{dir}/{manifest.Sheet}";
        if (!Exists(sheetRel))
            return (false, $"Spritesheet '{manifest.Sheet}' not found");

        if (manifest.Animations == null || manifest.Animations.Count == 0)
            return (false, "No animations defined");

        if (!manifest.Animations.TryGetValue("idle", out var idleAnim) || idleAnim.Frames == null || idleAnim.Frames.Length == 0)
            return (false, "Missing required 'idle' animation or idle frames are empty");

        foreach (var (name, anim) in manifest.Animations)
        {
            if (anim.Frames == null || anim.Frames.Length == 0)
                return (false, $"Animation '{name}' has no frames defined");

            foreach (int idx in anim.Frames)
            {
                if (idx < 0)
                    return (false, $"Animation '{name}' has negative frame index: {idx}");
            }
        }

        try
        {
            byte[] bytes = ReadAllBytes(sheetRel);
            if (bytes.Length == 0)
                return (false, $"Spritesheet '{manifest.Sheet}' is empty");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to read spritesheet '{manifest.Sheet}': {ex.Message}");
        }

        return (true, null);
    }

    /// <summary>
    /// Discovers all available companion packs (embedded and disk-based), validates each,
    /// and returns them in a consistent display order (primary Neku companion first,
    /// local personal companion if present, other custom packs, then built-in standard cats).
    /// </summary>
    public static IReadOnlyList<CompanionPackInfo> DiscoverPacks()
    {
        var packIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Embedded packs
        const string prefix = "DesktopPet.Assets.pets.";
        const string suffix = ".manifest.json";
        var embeddedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            string id = name[prefix.Length..^suffix.Length];
            embeddedSet.Add(id);
            packIds.Add(id);
        }

        // 2. Disk packs
        try
        {
            foreach (var petDir in CandidateDiskPaths("Assets/pets"))
            {
                if (Directory.Exists(petDir))
                {
                    foreach (var d in Directory.EnumerateDirectories(petDir))
                    {
                        if (File.Exists(Path.Combine(d, "manifest.json")))
                        {
                            packIds.Add(Path.GetFileName(d));
                        }
                    }
                }
            }
        }
        catch { /* best-effort */ }

        var standardOrder = new[]
        {
            "cat",
            "cat-orange",
            "cat-silver",
            "cat-cream",
            "cat-chocolate",
            "cat-blue",
            "cat-janet"
        };

        var result = new List<CompanionPackInfo>();

        void AddPack(string id)
        {
            if (!packIds.Contains(id)) return;
            packIds.Remove(id);

            bool isCustom = !embeddedSet.Contains(id) || id.Equals("neku-personal-local", StringComparison.OrdinalIgnoreCase);
            var (isValid, error) = ValidatePack(id);

            string displayName = GetDefaultDisplayName(id);
            if (displayName == id && isValid)
            {
                try
                {
                    string manifestRel = $"Assets/pets/{id}/manifest.json";
                    var m = PetManifest.Parse(ReadAllText(manifestRel), manifestRel);
                    if (!string.IsNullOrWhiteSpace(m.Name) && !m.Name.Equals("cat", StringComparison.OrdinalIgnoreCase))
                    {
                        displayName = m.Name;
                    }
                }
                catch { }
            }

            if (isCustom && id != "neku-personal-local" && !displayName.EndsWith("(Custom)", StringComparison.OrdinalIgnoreCase))
            {
                displayName = $"{displayName} (Custom)";
            }

            result.Add(new CompanionPackInfo(id, displayName, isCustom, isValid, error));
        }

        AddPack("neku");
        AddPack("neku-personal-local");

        var customPacks = packIds
            .Where(id => !embeddedSet.Contains(id) && !standardOrder.Contains(id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var id in customPacks)
        {
            AddPack(id);
        }

        foreach (var id in standardOrder)
        {
            AddPack(id);
        }

        foreach (var id in packIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList())
        {
            AddPack(id);
        }

        return result;
    }

    public static string GetDefaultDisplayName(string id) => id.ToLowerInvariant() switch
    {
        "neku" => "Neku (Goblin companion)",
        "neku-personal-local" => "Personal Companion (Local)",
        "cat" => "Classic Gray Cat",
        "cat-orange" => "Orange Cat",
        "cat-silver" => "Silver Tabby Cat",
        "cat-cream" => "Cream Cat",
        "cat-chocolate" => "Chocolate Cat",
        "cat-blue" => "Blue Cat",
        "cat-janet" => "Janet Cat",
        _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(id.Replace('-', ' ').Replace('_', ' '))
    };
}

/// <summary>
/// Metadata and validation status for a companion pack.
/// </summary>
public sealed record CompanionPackInfo(
    string Id,
    string DisplayName,
    bool IsCustom,
    bool IsValid,
    string? Error = null);
