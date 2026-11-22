namespace Skein;

/// <summary>A single-key shortcut you can change: its id, where it's listed, what it does, its standard key.</summary>
public sealed record KeyAction(string Id, string Group, string Label, string Default);

/// <summary>
/// The single-key shortcuts (j, k, a, z…) and the keys you've changed them to. Keys are what the
/// web's KeyboardEvent.key reports: "j", "J" (that's Shift+J), "/", "?". Ctrl shortcuts, Space,
/// Esc and F11 stay fixed.
/// </summary>
public static class KeyMap
{
    public static readonly IReadOnlyList<KeyAction> Actions =
    [
        new("next", "Moving around", "Next post", "j"),
        new("previous", "Moving around", "Previous post", "k"),
        new("nextComment", "Moving around", "Next top-level comment", "J"),
        new("previousComment", "Moving around", "Previous top-level comment", "K"),
        new("nextNew", "Moving around", "Next comment that's new since your last visit", "N"),
        new("nextOp", "Moving around", "Next comment by the original poster", "P"),
        new("loadMore", "Moving around", "Load more posts", "m"),
        new("home", "Going places", "Home", "h"),
        new("inbox", "Going places", "Inbox", "i"),
        new("search", "Going places", "Search, or go to r/name or u/name", "/"),
        new("newPost", "Going places", "New post", "n"),
        new("shortcuts", "Going places", "Keyboard shortcuts (this window)", "?"),
        new("upvote", "Posts", "Upvote", "a"),
        new("downvote", "Posts", "Downvote", "z"),
        new("save", "Posts", "Save or unsave", "s"),
        new("hide", "Posts", "Hide the post", "x"),
        new("open", "Posts", "Open the link inside Skein", "o"),
        new("openExternal", "Posts", "Open the link in your default browser", "O"),
        new("download", "Posts", "Download the post's pictures or video", "d"),
        new("viewer", "Posts", "Picture viewer", "v"),
        new("readAloud", "Posts", "Read the post and comments aloud", "R"),
        new("refresh", "Posts", "Refresh", "r"),
        new("reading", "View", "Reading mode", "f"),
        new("bigger", "View", "Bigger text", "+"),
        new("smaller", "View", "Smaller text", "-"),
        new("compact", "View", "Compact layout", "1"),
        new("cards", "View", "Cards layout", "2"),
        new("grid", "View", "Grid layout", "3"),
    ];

    /// <summary>Shortcuts that can't be changed, for the list.</summary>
    public static readonly IReadOnlyList<(string Keys, string Label)> Fixed =
    [
        ("Ctrl+K", "Go anywhere: communities, folders, tabs and commands"),
        ("Ctrl+F", "Find in thread"),
        ("Ctrl+T / Ctrl+W", "New tab / close tab"),
        ("Ctrl+Tab", "Next tab (Shift for previous)"),
        ("Ctrl+L", "Search"),
        ("Ctrl+M", "Mini mode"),
        ("Ctrl+R / F5", "Refresh"),
        ("Ctrl+B / I / K", "While writing: bold, italic, add a link"),
        ("Space", "Scroll the post (Shift+Space scrolls back)"),
        ("F11", "Full screen"),
        ("Esc", "Close the browser pane, reading mode or full screen"),
        ("Middle-click", "Open a sidebar entry in a new tab"),
    ];

    /// <summary>Every action's key: yours where you changed it, the standard one otherwise.</summary>
    public static Dictionary<string, string> Effective(IReadOnlyDictionary<string, string> custom) =>
        Actions.ToDictionary(a => a.Id, a => custom.TryGetValue(a.Id, out var key) && IsBindable(key) ? key : a.Default);

    /// <summary>The action a key does, if any ("=" counts as "+", as on most keyboards they share a key).</summary>
    public static string? ActionFor(string key, IReadOnlyDictionary<string, string> custom)
    {
        var keys = Effective(custom);
        var found = keys.FirstOrDefault(p => p.Value == key).Key;
        if (found == null && key == "=") found = keys.FirstOrDefault(p => p.Value == "+").Key;
        return found;
    }

    /// <summary>A key you can give an action: one printable character.</summary>
    public static bool IsBindable(string key) => key.Length == 1 && !char.IsWhiteSpace(key[0]) && !char.IsControl(key[0]);

    /// <summary>"J" → "Shift+J", "/" → "/", "j" → "J"… as shown in the list.</summary>
    public static string Display(string key) =>
        key.Length == 1 && char.IsLetter(key[0])
            ? char.IsUpper(key[0]) ? "Shift+" + key : key.ToUpperInvariant()
            : key;

    /// <summary>
    /// Gives an action a new key. If another action had that key, the two swap, so no key ever
    /// does two things. Only keys that differ from the standard ones are kept.
    /// </summary>
    public static void Bind(Dictionary<string, string> custom, string actionId, string key)
    {
        if (!IsBindable(key) || Actions.All(a => a.Id != actionId)) return;
        var keys = Effective(custom);
        var old = keys[actionId];
        var other = keys.FirstOrDefault(p => p.Value == key && p.Key != actionId).Key;
        keys[actionId] = key;
        if (other != null) keys[other] = old;
        custom.Clear();
        foreach (var action in Actions.Where(a => keys[a.Id] != a.Default)) custom[action.Id] = keys[action.Id];
    }
}
