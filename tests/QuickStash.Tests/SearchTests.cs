using QuickStash.Data;
using QuickStash.ViewModels;

namespace QuickStash.Tests;

public class SearchTests
{
    [Theory]
    [InlineData("Bear cave near the dam", "bear", true)]
    [InlineData("Bear cave near the dam", "DAM cave", true)]        // all terms, any order
    [InlineData("Bear cave near the dam", "bear wolf", false)]
    [InlineData("Ölfass im Keller", "ölfass", true)]               // Unicode case-insensitive (SQLite LIKE is ASCII-only)
    [InlineData("100% loot_rate", "100% loot_", true)]             // % and _ are literal, not wildcards
    [InlineData("anything", "%", false)]
    [InlineData("anything", "   ", false)]
    public void Matches_all_terms_ignoring_case(string text, string query, bool expected)
    {
        Assert.Equal(expected, Database.Matches(text, query));
    }

    [Fact]
    public void Search_spans_topics_newest_first_with_topic_name()
    {
        using var folder = new TestDataFolder();
        var tld = folder.Topics.Create("TLD");
        var sky = folder.Topics.Create("Skyrim");
        folder.Notes.Add(tld.Id, "Rifle in the hunting lodge", null);
        Thread.Sleep(5);
        folder.Notes.Add(sky.Id, "Hunting bow from the Riverwood trader", null);
        Thread.Sleep(5);
        folder.Notes.Add(sky.Id, "Dragonstone", null);

        var results = folder.Notes.Search("hunting");

        Assert.Equal(2, results.Count);
        Assert.Equal("Skyrim", results[0].TopicName);
        Assert.Equal("TLD", results[1].TopicName);
        Assert.Empty(folder.Notes.Search("wolf"));
        Assert.Empty(folder.Notes.Search(""));
    }

    [Fact]
    public void Search_respects_limit()
    {
        using var folder = new TestDataFolder();
        var topic = folder.Topics.Create("T");
        for (int i = 0; i < 15; i++) folder.Notes.Add(topic.Id, $"note {i}", null);
        Assert.Equal(10, folder.Notes.Search("note", limit: 10).Count);
    }

    [Fact]
    public void Snippet_splits_around_first_match_and_flattens_lines()
    {
        var (before, match, after) = SearchResultViewModel.BuildSnippet("Bear cave\nnear the DAM\nbring rifle", "dam");
        Assert.Equal("Bear cave  ·  near the ", before);
        Assert.Equal("DAM", match);
        Assert.Equal("  ·  bring rifle", after);
    }

    [Fact]
    public void Snippet_trims_long_context_with_ellipses()
    {
        string text = new string('a', 100) + "TARGET" + new string('b', 200);
        var (before, match, after) = SearchResultViewModel.BuildSnippet(text, "target");
        Assert.StartsWith("…", before);
        Assert.Equal("TARGET", match);
        Assert.EndsWith("…", after);
        Assert.True(before.Length <= 31 && after.Length <= 91);
    }
}
