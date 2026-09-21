using CommunityToolkit.Mvvm.ComponentModel;
using QuickStash.Models;

namespace QuickStash.ViewModels;

/// <summary>A row in the topic list.</summary>
public sealed partial class TopicItemViewModel : ObservableObject
{
    public TopicItemViewModel(Topic topic)
    {
        Topic = topic;
        _name = topic.Name;
    }

    public Topic Topic { get; }
    public long Id => Topic.Id;

    [ObservableProperty] private string _name;

    /// <summary>A fresh copy each time so bound lists refresh after <see cref="RefreshProcesses"/>.</summary>
    public IReadOnlyList<string> ProcessNames => Topic.ProcessNames.ToArray();

    /// <summary>Short secondary line for the list, e.g. "tld.exe" or "tld.exe +1".</summary>
    public string ProcessSummary => Topic.ProcessNames.Count switch
    {
        0 => string.Empty,
        1 => Topic.ProcessNames[0],
        _ => $"{Topic.ProcessNames[0]} +{Topic.ProcessNames.Count - 1}",
    };

    public bool IsLinkedTo(string? processName) =>
        processName is not null && Topic.ProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase);

    public bool Matches(string filter) =>
        string.IsNullOrWhiteSpace(filter)
        || Name.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase)
        || Topic.ProcessNames.Any(p => p.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Call after the underlying topic's process links changed.</summary>
    public void RefreshProcesses()
    {
        OnPropertyChanged(nameof(ProcessNames));
        OnPropertyChanged(nameof(ProcessSummary));
    }

    partial void OnNameChanged(string value) => Topic.Name = value;
}
