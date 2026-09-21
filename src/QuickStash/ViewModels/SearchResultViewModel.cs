using QuickStash.Models;

namespace QuickStash.ViewModels;

/// <summary>A global search hit: topic, title and a snippet split around the first match so the view can highlight it.</summary>
public sealed class SearchResultViewModel
{
    private const int ContextBefore = 30;
    private const int ContextAfter = 90;

    public SearchResultViewModel(NoteSearchResult result, string query)
    {
        Note = result.Note;
        TopicName = result.TopicName;
        Title = Note.Title.Length > 0 ? Note.Title : "Screenshot";
        (SnippetBefore, SnippetMatch, SnippetAfter) = BuildSnippet(Note.Text, query);
    }

    public Note Note { get; }
    public string TopicName { get; }
    public string Title { get; }
    public DateTime CreatedAt => Note.CreatedAt;
    public bool HasImage => Note.ImagePath is not null;

    public string SnippetBefore { get; }
    public string SnippetMatch { get; }
    public string SnippetAfter { get; }

    /// <summary>Single-line excerpt around the first occurrence of any query term.</summary>
    internal static (string Before, string Match, string After) BuildSnippet(string text, string query)
    {
        string flat = string.Join("  ·  ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        int index = -1, length = 0;
        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            int i = flat.IndexOf(term, StringComparison.CurrentCultureIgnoreCase);
            if (i >= 0 && (index < 0 || i < index))
            {
                index = i;
                length = term.Length;
            }
        }
        if (index < 0) return (Truncate(flat, ContextBefore + ContextAfter), string.Empty, string.Empty);

        int start = Math.Max(0, index - ContextBefore);
        int end = Math.Min(flat.Length, index + length + ContextAfter);
        string before = (start > 0 ? "…" : string.Empty) + flat[start..index];
        string after = flat[(index + length)..end] + (end < flat.Length ? "…" : string.Empty);
        return (before, flat.Substring(index, length), after);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
