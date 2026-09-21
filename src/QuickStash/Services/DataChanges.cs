namespace QuickStash.Services;

/// <summary>
/// A tiny in-process notification: "notes or topics changed". The overlay, the companion window and the quick-capture
/// service share one database, so whoever writes raises this and the other open views refresh.
/// </summary>
internal sealed class DataChanges
{
    /// <summary>Sender is whoever made the change (so it can ignore its own notification); the argument is the topic that changed, or null for "anything".</summary>
    public event EventHandler<long?>? Changed;

    public void Raise(object sender, long? topicId) => Changed?.Invoke(sender, topicId);
}
