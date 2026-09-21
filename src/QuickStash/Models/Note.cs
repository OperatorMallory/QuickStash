namespace QuickStash.Models;

/// <summary>A plain-text note with an optional image. Timestamps are UTC.</summary>
public sealed class Note
{
    public long Id { get; init; }
    public long TopicId { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>Path relative to the data folder (e.g. "images\2025….png"), or null.</summary>
    public string? ImagePath { get; set; }

    /// <summary>When the image has a drawing on it: the untouched screenshot (ImagePath is then the drawn-on copy).</summary>
    public string? OriginalImagePath { get; set; }

    /// <summary>When the image has a drawing on it: the saved ink strokes (.isf), so the drawing stays editable.</summary>
    public string? AnnotationPath { get; set; }

    public bool IsPinned { get; set; }
    public double? PinX { get; set; }
    public double? PinY { get; set; }

    /// <summary>Width of the pinned window in device-independent pixels, or null for the default.</summary>
    public double? PinWidth { get; set; }

    public bool HasDrawing => AnnotationPath is not null;

    /// <summary>Every file this note owns, for cleanup on delete.</summary>
    public IEnumerable<string> Files()
    {
        if (ImagePath is not null) yield return ImagePath;
        if (OriginalImagePath is not null) yield return OriginalImagePath;
        if (AnnotationPath is not null) yield return AnnotationPath;
    }
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
