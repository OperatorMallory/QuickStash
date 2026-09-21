namespace QuickStash.Models;

/// <summary>A group of notes, e.g. "TLD". Optionally linked to one or more executables so it is auto-selected.</summary>
public sealed class Topic
{
    public long Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public DateTime LastUsed { get; set; }
    public List<string> ProcessNames { get; init; } = new();
}
