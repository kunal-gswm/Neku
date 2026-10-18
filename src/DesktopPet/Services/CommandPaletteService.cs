namespace DesktopPet.Services;

/// <summary>
/// Represents an executable action in the Neku Command Palette with metadata for search,
/// display, categories, and keyboard hints.
/// </summary>
public sealed class PaletteCommand
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public string Icon { get; }
    public string ShortcutHint { get; }
    public IReadOnlyList<string> Keywords { get; }
    public Action Action { get; }
    public Func<bool>? CanExecute { get; }

    public bool HasShortcutHint => !string.IsNullOrWhiteSpace(ShortcutHint);
    public bool IsExecutable => CanExecute?.Invoke() ?? true;

    public PaletteCommand(
        string id,
        string title,
        string description,
        string category,
        Action action,
        string icon = "⚡",
        string shortcutHint = "",
        IEnumerable<string>? keywords = null,
        Func<bool>? canExecute = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Description = description ?? "";
        Category = string.IsNullOrWhiteSpace(category) ? "General" : category;
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Icon = string.IsNullOrWhiteSpace(icon) ? "⚡" : icon;
        ShortcutHint = shortcutHint ?? "";
        Keywords = keywords != null
            ? keywords.Where(k => !string.IsNullOrWhiteSpace(k)).ToList()
            : Array.Empty<string>();
        CanExecute = canExecute;
    }
}

/// <summary>
/// Central registry and search provider for the Neku Command Palette.
/// Supports deterministic multi-tier ranking (exact match > prefix match > substring > fuzzy),
/// recent command tracking, execution routing, and dynamic registration.
/// </summary>
public sealed class CommandPaletteService
{
    private readonly Dictionary<string, PaletteCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _commandOrder = new();
    private readonly List<string> _recentCommandIds = new();

    /// <summary>Event raised whenever a command is executed through the service.</summary>
    public event Action<PaletteCommand>? CommandExecuted;

    /// <summary>
    /// Registers a new command. Replaces any previously registered command with the same ID.
    /// </summary>
    public void Register(PaletteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_commands.ContainsKey(command.Id))
        {
            _commands[command.Id] = command;
            return;
        }

        _commands[command.Id] = command;
        _commandOrder.Add(command.Id);
    }

    /// <summary>
    /// Registers a batch of commands preserving order.
    /// </summary>
    public void RegisterRange(IEnumerable<PaletteCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (var cmd in commands)
        {
            Register(cmd);
        }
    }

    /// <summary>
    /// Unregisters a command by its unique ID.
    /// </summary>
    public bool Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        bool removed = _commands.Remove(id);
        _commandOrder.Remove(id);
        _recentCommandIds.Remove(id);
        return removed;
    }

    /// <summary>
    /// Clears all registered commands and history.
    /// </summary>
    public void Clear()
    {
        _commands.Clear();
        _commandOrder.Clear();
        _recentCommandIds.Clear();
    }

    /// <summary>
    /// Returns the command with the specified ID, or null if not found.
    /// </summary>
    public PaletteCommand? GetCommand(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _commands.TryGetValue(id, out var cmd) ? cmd : null;
    }

    /// <summary>
    /// Returns all registered commands in registration order.
    /// </summary>
    public IReadOnlyList<PaletteCommand> GetAllCommands()
    {
        return _commandOrder
            .Where(id => _commands.ContainsKey(id))
            .Select(id => _commands[id])
            .ToList();
    }

    /// <summary>
    /// Returns the count of registered commands.
    /// </summary>
    public int Count => _commands.Count;

    /// <summary>
    /// Executes a registered command by its ID. Returns false if not found or cannot execute.
    /// </summary>
    public bool Execute(string id)
    {
        var cmd = GetCommand(id);
        if (cmd == null || !cmd.IsExecutable) return false;
        Execute(cmd);
        return true;
    }

    /// <summary>
    /// Executes the specified command and notifies listeners.
    /// </summary>
    public void Execute(PaletteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        RecordRecent(command.Id);
        command.Action();
        CommandExecuted?.Invoke(command);
    }

    /// <summary>
    /// Searches registered commands matching the query using deterministic scoring:
    /// Exact match > Prefix match > Acronym/Keyword match > Substring match > Fuzzy match.
    /// </summary>
    public IReadOnlyList<PaletteCommand> Search(string? query)
    {
        var all = GetAllCommands();
        if (string.IsNullOrWhiteSpace(query))
        {
            return all;
        }

        string q = query.Trim().ToLowerInvariant();
        var scored = new List<(PaletteCommand Command, int Score)>();

        foreach (var cmd in all)
        {
            int score = CalculateMatchScore(cmd, q);
            if (score > 0)
            {
                // Slight boost for recently executed commands
                int recentIndex = _recentCommandIds.IndexOf(cmd.Id);
                if (recentIndex >= 0)
                {
                    score += Math.Max(1, 20 - (recentIndex * 2));
                }

                scored.Add((cmd, score));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Command.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Command)
            .ToList();
    }

    /// <summary>
    /// Calculates match score for a command against a search query.
    /// Returns 0 if there is no match.
    /// </summary>
    public static int CalculateMatchScore(PaletteCommand cmd, string q)
    {
        string title = cmd.Title.ToLowerInvariant();
        string desc = cmd.Description.ToLowerInvariant();
        string cat = cmd.Category.ToLowerInvariant();

        // 1. Exact match on title
        if (title == q) return 1000;

        // 2. Title starts with query
        if (title.StartsWith(q, StringComparison.Ordinal)) return 600;

        // 3. Word start in title (e.g. "focus" matching "Start Focus Session")
        string[] titleWords = title.Split(new[] { ' ', '-', '_', ':', '•' }, StringSplitOptions.RemoveEmptyEntries);
        if (titleWords.Any(w => w.StartsWith(q, StringComparison.Ordinal)))
        {
            return 500;
        }

        // 4. Acronym match (e.g. "oph" -> "Open Productivity Hub")
        if (q.Length >= 2 && titleWords.Length >= q.Length)
        {
            string acronym = string.Concat(titleWords.Take(q.Length).Select(w => w[0]));
            if (acronym.Equals(q, StringComparison.OrdinalIgnoreCase))
            {
                return 450;
            }
        }

        // 5. Keyword exact match
        foreach (var kw in cmd.Keywords)
        {
            string k = kw.ToLowerInvariant();
            if (k == q) return 400;
        }

        // 6. Keyword prefix match
        foreach (var kw in cmd.Keywords)
        {
            string k = kw.ToLowerInvariant();
            if (k.StartsWith(q, StringComparison.Ordinal)) return 350;
        }

        // 7. Title contains substring
        int titleIdx = title.IndexOf(q, StringComparison.Ordinal);
        if (titleIdx >= 0)
        {
            return 300 - Math.Min(titleIdx * 5, 50);
        }

        // 8. Category starts with query
        if (cat.StartsWith(q, StringComparison.Ordinal)) return 250;

        // 9. Category exact match
        if (cat == q) return 260;

        // 10. Keyword substring match
        if (cmd.Keywords.Any(k => k.ToLowerInvariant().Contains(q, StringComparison.Ordinal)))
        {
            return 200;
        }

        // 11. Description contains query substring
        int descIdx = desc.IndexOf(q, StringComparison.Ordinal);
        if (descIdx >= 0)
        {
            return 150 - Math.Min(descIdx * 2, 40);
        }

        // 12. Fuzzy subsequence match on title
        if (FuzzyMatch(title, q, out int fuzzyScore))
        {
            return 80 + Math.Clamp(fuzzyScore, 0, 50);
        }

        // 13. Fuzzy subsequence match on description
        if (FuzzyMatch(desc, q, out int descFuzzyScore))
        {
            return 40 + Math.Clamp(descFuzzyScore / 2, 0, 30);
        }

        return 0;
    }

    private static bool FuzzyMatch(string text, string pattern, out int score)
    {
        score = 0;
        if (pattern.Length == 0 || text.Length < pattern.Length) return false;

        int pIdx = 0;
        int consecutive = 0;

        for (int tIdx = 0; tIdx < text.Length && pIdx < pattern.Length; tIdx++)
        {
            if (text[tIdx] == pattern[pIdx])
            {
                pIdx++;
                consecutive++;
                score += 10 + (consecutive * 5);
            }
            else
            {
                consecutive = 0;
            }
        }

        return pIdx == pattern.Length;
    }

    private void RecordRecent(string id)
    {
        _recentCommandIds.Remove(id);
        _recentCommandIds.Insert(0, id);
        if (_recentCommandIds.Count > 10)
        {
            _recentCommandIds.RemoveAt(_recentCommandIds.Count - 1);
        }
    }
}
