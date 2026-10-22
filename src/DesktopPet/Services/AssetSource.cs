using System.IO;

using System.Reflection;

using System.Windows.Media;

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

        string appData = normalized.StartsWith("Assets" + Path.DirectorySeparatorChar + "pets", StringComparison.OrdinalIgnoreCase) ? (normalized.Length > 11 ? Path.Combine(CompanionPackService.CustomPacksDirectory, normalized[12..]) : CompanionPackService.CustomPacksDirectory) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku", normalized);

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
                        if (Path.GetFileName(d).StartsWith('.')) continue;

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



        byte[] bytes;

        try

        {

            bytes = ReadAllBytes(sheetRel);

            if (bytes.Length == 0)

                return (false, $"Spritesheet '{manifest.Sheet}' is empty");

        }

        catch (Exception ex)

        {

            return (false, $"Failed to read spritesheet '{manifest.Sheet}': {ex.Message}");

        }



        // Validate frame capacity from PNG dimensions if available

        var dims = TryGetImageDimensions(bytes);

        int maxFrames = int.MaxValue;

        if (dims is { Width: > 0, Height: > 0 } d)

        {

            int cols = d.Width / manifest.CellWidth;

            int rows = d.Height / manifest.CellHeight;

            maxFrames = cols * rows;

            if (manifest.Columns > cols)

                return (false, $"Manifest columns ({manifest.Columns}) exceeds sheet columns ({cols}) based on width {d.Width}px and cellWidth {manifest.CellWidth}px");

        }



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

                if (idx >= maxFrames)

                    return (false, $"Animation '{name}' references frame {idx}, but spritesheet only holds {maxFrames} frames ({dims!.Value.Width}x{dims!.Value.Height})");

            }

        }



        return (true, null);

    }



    /// <summary>

    /// Parses width and height directly from PNG IHDR chunk (bytes 16..23).

    /// </summary>

    /// <summary>
    /// Safely attempts to determine image dimensions (width, height) from raw image bytes.
    /// Supports PNG, BMP, and JPEG formats via zero-copy header inspection,
    /// falling back to WPF BitmapDecoder with delayed creation.
    /// </summary>
    public static (int Width, int Height)? TryGetImageDimensions(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 10) return null;

        // 1. PNG inspection (IHDR chunk at bytes 16..23)
        if (bytes.Length >= 24 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[12] == (byte)'I' && bytes[13] == (byte)'H' && bytes[14] == (byte)'D' && bytes[15] == (byte)'R')
        {
            int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            if (w > 0 && h > 0) return (w, h);
        }

        // 2. BMP inspection ('BM' magic header)
        if (bytes.Length >= 26 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            int dibSize = BitConverter.ToInt32(bytes, 14);
            if (dibSize >= 40 && bytes.Length >= 26)
            {
                int w = BitConverter.ToInt32(bytes, 18);
                int h = Math.Abs(BitConverter.ToInt32(bytes, 22));
                if (w > 0 && h > 0) return (w, h);
            }
            else if (dibSize == 12 && bytes.Length >= 22)
            {
                int w = BitConverter.ToUInt16(bytes, 18);
                int h = BitConverter.ToUInt16(bytes, 20);
                if (w > 0 && h > 0) return (w, h);
            }
        }

        // 3. JPEG inspection (SOI marker 0xFF 0xD8)
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            int i = 2;
            while (i < bytes.Length)
            {
                if (bytes[i] != 0xFF) { i++; continue; }
                while (i < bytes.Length && bytes[i] == 0xFF) i++;
                if (i >= bytes.Length) break;
                byte marker = bytes[i++];

                // Standalone markers without payload/length
                if (marker is 0xD9 or 0xDA) break; // EOI or SOS (start of entropy data)
                if (marker is (>= 0xD0 and <= 0xD7) or 0x00 or 0x01) continue; // RSTn or stuffed byte

                if (i + 2 > bytes.Length) break; // Truncated length field
                int len = (bytes[i] << 8) | bytes[i + 1];
                if (len < 2 || i + len > bytes.Length) break; // Malformed or truncated segment

                if (IsJpegStartOfFrameMarker(marker))
                {
                    // 1. Must have at least 8 bytes in declared segment to read Nf (Lf:2, P:1, Y:2, X:2, Nf:1)
                    //    and the full declared segment must be inside the input buffer.
                    if (len < 8 || i + len > bytes.Length)
                    {
                        return null;
                    }

                    // Read component count Nf (proven to be inside declared segment and input buffer)
                    int nf = bytes[i + 7];

                    // 2. Reject zero component count
                    if (nf <= 0)
                    {
                        return null;
                    }

                    // 3. SOF segment length must strictly equal 8 + 3 * Nf using checked arithmetic
                    int expectedLen;
                    try
                    {
                        expectedLen = checked(8 + 3 * nf);
                    }
                    catch (OverflowException)
                    {
                        return null;
                    }

                    if (len != expectedLen)
                    {
                        return null;
                    }

                    // 4. Confirm the full declared segment is inside the input buffer before accessing component records
                    // (already guaranteed by len == expectedLen and i + len <= bytes.Length)

                    int h = (bytes[i + 3] << 8) | bytes[i + 4];
                    int w = (bytes[i + 5] << 8) | bytes[i + 6];
                    if (w > 0 && h > 0)
                    {
                        return (w, h);
                    }

                    return null;
                }

                i += len;
            }
        }

        // 4. Fallback to WPF BitmapDecoder with DelayCreation
        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                ms,
                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                System.Windows.Media.Imaging.BitmapCacheOption.None);
            if (decoder.Frames.Count > 0)
            {
                var frame = decoder.Frames[0];
                if (frame.PixelWidth > 0 && frame.PixelHeight > 0)
                {
                    return (frame.PixelWidth, frame.PixelHeight);
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Checks whether a JPEG marker byte is a Start Of Frame (SOF) marker containing dimensions.
    /// Markers 0xC4 (DHT), 0xC8 (JPG extensions), and 0xCC (DAC) are explicitly excluded as they are not SOF markers.
    /// </summary>
    public static bool IsJpegStartOfFrameMarker(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    /// <summary>
    /// Parses width and height directly from PNG IHDR chunk (bytes 16..23).
    /// Kept for backward compatibility.
    /// </summary>
    public static (int Width, int Height)? TryGetPngDimensions(byte[] bytes) => TryGetImageDimensions(bytes);



    /// <summary>

    /// Creates an isolated SpriteAnimator for a companion pack, or null if loading fails.

    /// </summary>

    public static SpriteAnimator? CreateAnimator(string packId)

    {

        try

        {

            string dir = $"Assets/pets/{packId}";

            string manifestRel = $"{dir}/manifest.json";

            if (!Exists(manifestRel)) return null;



            var manifest = PetManifest.Parse(ReadAllText(manifestRel), manifestRel);

            string sheetRel = $"{dir}/{manifest.Sheet}";

            if (!Exists(sheetRel)) return null;



            byte[] bytes = ReadAllBytes(sheetRel);

            return new SpriteAnimator(manifest, bytes);

        }

        catch

        {

            return null;

        }

    }



    /// <summary>

    /// Loads a preview ImageSource of the pack's idle frame for UI previews.

    /// </summary>

    public static ImageSource? GetPackPreview(string packId)

    {

        try

        {

            return CreateAnimator(packId)?.GetPreviewFrame("idle");

        }

        catch

        {

            return null;

        }

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

            "billu",

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

            string? description = GetDefaultDescription(id);

            int animCount = 0;

            IReadOnlyList<string>? animations = null;

            int cellW = 64;

            int cellH = 64;

            double scale = 1.0;

            string category = id == "neku" ? "Mascot" : (id == "neku-personal-local" ? "Personal" : (standardOrder.Contains(id, StringComparer.OrdinalIgnoreCase) ? "Legacy Cats" : "Imported"));

            string? author = null;

            string? version = null;

            string? license = null;

            string? sourceUrl = null;



            if (isValid)

            {

                try

                {

                    string manifestRel = $"Assets/pets/{id}/manifest.json";

                    var m = PetManifest.Parse(ReadAllText(manifestRel), manifestRel);

                    if (displayName == id && !string.IsNullOrWhiteSpace(m.Name) && !m.Name.Equals("cat", StringComparison.OrdinalIgnoreCase))

                    {

                        displayName = m.Name;

                    }

                    if (!string.IsNullOrWhiteSpace(m.Description))

                    {

                        description = m.Description;

                    }

                    if (!string.IsNullOrWhiteSpace(m.Category))

                    {

                        category = m.Category;

                    }

                    author = m.Author;

                    version = m.Version;

                    license = m.License;

                    sourceUrl = m.SourceUrl;

                    animCount = m.Animations?.Count ?? 0;

                    animations = m.Animations?.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

                    cellW = m.CellWidth;

                    cellH = m.CellHeight;

                    scale = m.Scale;

                }

                catch { }

            }



            if (isCustom && id != "neku-personal-local" && !displayName.EndsWith("(Custom)", StringComparison.OrdinalIgnoreCase))

            {

                displayName = $"{displayName} (Custom)";

            }



            string? diskDir = null;

            try

            {

                diskDir = CandidateDiskPaths($"Assets/pets/{id}").FirstOrDefault(Directory.Exists);

            }

            catch { }



            bool isRemovable = isCustom && !CompanionPackService.IsProtectedPack(id);



            result.Add(new CompanionPackInfo(

                id,

                displayName,

                isCustom,

                isValid,

                error,

                description,

                animCount,

                animations,

                cellW,

                cellH,

                scale,

                category,

                author,

                version,

                license,

                sourceUrl,

                isRemovable,

                diskDir));

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

        "billu" => "Billu",

        "cat-silver" => "Silver Tabby Cat",

        "cat-cream" => "Cream Cat",

        "cat-chocolate" => "Chocolate Cat",

        "cat-blue" => "Blue Cat",

        "cat-janet" => "Janet Cat",

        _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(id.Replace('-', ' ').Replace('_', ' '))

    };



    public static string GetDefaultDescription(string id) => id.ToLowerInvariant() switch

    {

        "neku" => "The spirited green goblin companion who reacts to your focus sessions and code builds.",

        "neku-personal-local" => "Your private personalized companion loaded securely from your local AppData.",

        "cat" => "The classic pixel cat companion who loves to nap, walk, and chase your cursor.",

        "cat-orange" => "Energetic orange tabby companion who loves playful naps and exploration.",

        "billu" => "Reel Goblin — chaotic orange cat with doomscrolling Reels humor, dramatic side-eyes, and buffering brain cells.",

        "cat-silver" => "Sleek silver tabby companion with a calm and gentle demeanor.",

        "cat-cream" => "Soft cream companion that radiates warmth and gentle curiosity.",

        "cat-chocolate" => "Playful chocolate brown cat who loves treats and lounging.",

        "cat-blue" => "Quiet dusky blue companion with expressive reactions.",

        "cat-janet" => "Calico companion with vibrant patches and inquisitive charm.",

        _ => "Custom desktop companion pack."

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

    string? Error = null,

    string? Description = null,

    int AnimationCount = 0,

    IReadOnlyList<string>? Animations = null,

    int CellWidth = 64,

    int CellHeight = 64,

    double Scale = 1.0,

    string Category = "Original",

    string? Author = null,

    string? Version = null,

    string? License = null,

    string? SourceUrl = null,

    bool IsRemovable = false,

    string? DiskDirectory = null);
