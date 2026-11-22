using System.IO;
using System.Text.Json;

namespace Skein;

/// <summary>
/// Posts you've opened, newest first, for the 🕘 History feed (this PC only). Each keeps the
/// post as Reddit sent it, so the list shows it like any other feed without asking Reddit again.
/// </summary>
public sealed class HistoryStore
{
    public const int MaxEntries = 2000;

    readonly string _file;
    readonly List<Entry> _entries;
    DateTime _lastSave = DateTime.MinValue;

    public sealed record Entry(string FullName, long At, string Raw);

    public HistoryStore() : this(Path.Combine(Paths.Local, "history.json")) { }

    /// <summary>Uses <paramref name="file"/> for storage (tests use a temporary one).</summary>
    public HistoryStore(string file)
    {
        _file = file;
        try
        {
            _entries = File.Exists(file) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(file)) ?? [] : [];
        }
        catch
        {
            _entries = [];
        }
    }

    public int Count => _entries.Count;

    /// <summary>Notes that a post was opened (moving it to the top if it was there already).</summary>
    public void Record(string fullName, string postRaw, DateTimeOffset? at = null)
    {
        if (!fullName.StartsWith("t3_")) return;
        _entries.RemoveAll(e => e.FullName == fullName);
        _entries.Insert(0, new Entry(fullName, (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(), postRaw));
        if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        // Opening posts quickly one after another shouldn't write the file each time.
        if (DateTime.UtcNow - _lastSave > TimeSpan.FromSeconds(10)) Save();
    }

    public void Save()
    {
        _lastSave = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_entries));
        }
        catch
        {
            // Best-effort.
        }
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    /// <summary>Opened posts, newest first; with a query, those whose title, community or author match every word.</summary>
    public List<(Post Post, DateTimeOffset Opened)> List(string query = "")
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = new List<(Post, DateTimeOffset)>();
        foreach (var entry in _entries)
        {
            try
            {
                using var doc = JsonDocument.Parse(entry.Raw);
                var post = Post.FromLink(doc.RootElement);
                var haystack = $"{post.Title} {post.Subreddit} {post.Author} {post.Domain}";
                if (words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    found.Add((post, DateTimeOffset.FromUnixTimeSeconds(entry.At)));
            }
            catch
            {
                // Skip anything unreadable.
            }
        }
        return found;
    }
}
