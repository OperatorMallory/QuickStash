namespace QuickStash.Models;

/// <summary>A plain-text note with an optional image. Timestamps are UTC.</summary>
public sealed class Note
{
    public long Id { get; init; }
    public long TopicId { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>Path relative to the data folder (e.g. "images\2025….png"), or null.</summary>
    public string? ImagePath { get; set; }

    public bool IsPinned { get; set; }
    public double? PinX { get; set; }
    public double? PinY { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>First non-empty line, used as the note's title in lists.</summary>
    public string Title => GetTitle(Text);

    public static string GetTitle(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return string.Empty;
    }
}

/// <summary>A note found by global search, with the name of its topic.</summary>
public sealed record NoteSearchResult(Note Note, string TopicName);
