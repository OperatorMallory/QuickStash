using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickStash.Data;
using QuickStash.Services;

namespace QuickStash.Tests;

public class StorageTests
{
    [Fact]
    public void Schema_is_created_and_reopening_is_idempotent()
    {
        using var folder = new TestDataFolder();
        Assert.Equal(Database.SchemaVersion, folder.Db.Scalar<long>("PRAGMA user_version;"));
        Assert.Equal(3, folder.Db.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('Topics','TopicProcesses','Notes')"));

        folder.Topics.Create("TLD");
        using var again = new Database(Path.Combine(folder.Path, "quickstash.db"));
        Assert.Single(new TopicRepository(again).GetAll());
    }

    [Fact]
    public void Topics_are_ordered_by_most_recently_used()
    {
        using var folder = new TestDataFolder();
        var a = folder.Topics.Create("Alpha");
        Thread.Sleep(5);
        var b = folder.Topics.Create("Bravo");
        Assert.Equal(new[] { "Bravo", "Alpha" }, folder.Topics.GetAll().Select(t => t.Name));

        Thread.Sleep(5);
        folder.Topics.Touch(a.Id);
        Assert.Equal(new[] { "Alpha", "Bravo" }, folder.Topics.GetAll().Select(t => t.Name));
    }

    [Fact]
    public void Topic_names_are_unique_ignoring_case()
    {
        using var folder = new TestDataFolder();
        folder.Topics.Create("TLD");
        Assert.NotNull(folder.Topics.FindByName("tld"));
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() => folder.Topics.Create("tld"));
    }

    [Fact]
    public void Process_links_find_the_topic()
    {
        using var folder = new TestDataFolder();
        var tld = folder.Topics.Create("TLD", linkProcess: "TLD.exe");
        folder.Topics.LinkProcess(tld.Id, "tld_dx12.exe");
        folder.Topics.LinkProcess(tld.Id, "tld.exe"); // duplicate ignored

        Assert.Equal(tld.Id, folder.Topics.FindByProcess("tld.exe")!.Id);
        Assert.Equal(tld.Id, folder.Topics.FindByProcess("TLD_DX12.EXE")!.Id);
        Assert.Equal(new[] { "tld.exe", "tld_dx12.exe" }, folder.Topics.Get(tld.Id)!.ProcessNames);
        Assert.Null(folder.Topics.FindByProcess("notepad.exe"));

        folder.Topics.UnlinkProcess(tld.Id, "tld.exe");
        Assert.Null(folder.Topics.FindByProcess("tld.exe"));
    }

    [Fact]
    public void When_several_topics_share_a_process_the_most_recent_wins()
    {
        using var folder = new TestDataFolder();
        var first = folder.Topics.Create("Run 1", "game.exe");
        Thread.Sleep(5);
        var second = folder.Topics.Create("Run 2", "game.exe");
        Assert.Equal(second.Id, folder.Topics.FindByProcess("game.exe")!.Id);

        Thread.Sleep(5);
        folder.Topics.Touch(first.Id);
        Assert.Equal(first.Id, folder.Topics.FindByProcess("game.exe")!.Id);
    }

    [Fact]
    public void Notes_crud_newest_first()
    {
        using var folder = new TestDataFolder();
        var topic = folder.Topics.Create("TLD");
        var n1 = folder.Notes.Add(topic.Id, "Bear cave\nnear the dam", null);
        Thread.Sleep(5);
        var n2 = folder.Notes.Add(topic.Id, "Rifle in the hunting lodge", @"images\a.png");

        var notes = folder.Notes.GetByTopic(topic.Id);
        Assert.Equal(new[] { n2.Id, n1.Id }, notes.Select(n => n.Id));
        Assert.Equal("Bear cave", notes[1].Title);
        Assert.Equal(@"images\a.png", notes[0].ImagePath);

        folder.Notes.UpdateText(n1.Id, "Bear cave (cleared)");
        var updated = folder.Notes.Get(n1.Id)!;
        Assert.Equal("Bear cave (cleared)", updated.Text);
        Assert.True(updated.UpdatedAt >= updated.CreatedAt);

        Assert.Equal(@"images\a.png", folder.Notes.SetImage(n2.Id, null));
        Assert.Null(folder.Notes.Get(n2.Id)!.ImagePath);

        Assert.Null(folder.Notes.Delete(n1.Id));
        Assert.Single(folder.Notes.GetByTopic(topic.Id));
    }

    [Fact]
    public void Deleting_a_topic_cascades_and_reports_images()
    {
        using var folder = new TestDataFolder();
        var topic = folder.Topics.Create("TLD", "tld.exe");
        folder.Notes.Add(topic.Id, "one", @"images\one.png");
        folder.Notes.Add(topic.Id, "two", null);

        var images = folder.Topics.Delete(topic.Id);

        Assert.Equal(new[] { @"images\one.png" }, images);
        Assert.Equal(0, folder.Db.Scalar<long>("SELECT count(*) FROM Notes"));
        Assert.Equal(0, folder.Db.Scalar<long>("SELECT count(*) FROM TopicProcesses"));
    }

    [Fact]
    public void Pin_state_and_position_persist()
    {
        using var folder = new TestDataFolder();
        var topic = folder.Topics.Create("TLD");
        var a = folder.Notes.Add(topic.Id, "a", null);
        var b = folder.Notes.Add(topic.Id, "b", null);

        folder.Notes.SetPinned(a.Id, true);
        folder.Notes.SetPinPosition(a.Id, 120.5, 64);
        folder.Notes.SetPinned(b.Id, true);

        var pinned = folder.Notes.GetPinned();
        Assert.Equal(2, pinned.Count);
        Assert.Equal(120.5, pinned[0].PinX);
        Assert.Equal(64, pinned[0].PinY);
        Assert.Null(pinned[1].PinX);

        Assert.Equal(2, folder.Notes.UnpinAll());
        Assert.Empty(folder.Notes.GetPinned());
        Assert.Equal(120.5, folder.Notes.Get(a.Id)!.PinX); // position remembered for next pin
    }

    [Fact]
    public async Task Image_store_saves_loads_and_deletes_png()
    {
        using var folder = new TestDataFolder();
        var store = new ImageStore(folder.Path);

        var pixels = Enumerable.Repeat((byte)0x80, 64 * 32 * 4).ToArray();
        var source = BitmapSource.Create(64, 32, 96, 96, PixelFormats.Bgr32, null, pixels, 64 * 4);
        source.Freeze();

        string relative = await store.SaveAsync(source);
        Assert.StartsWith("images", relative);
        Assert.EndsWith(".png", relative);
        Assert.True(store.Exists(relative));

        var loaded = store.Load(relative)!;
        Assert.Equal(64, loaded.PixelWidth);
        Assert.Equal(16, store.Load(relative, decodePixelWidth: 16)!.PixelWidth);

        store.Delete(relative);
        Assert.False(store.Exists(relative));
        Assert.Null(store.Load(relative));
    }

    [Fact]
    public void Image_store_never_deletes_outside_images_folder()
    {
        using var folder = new TestDataFolder();
        var store = new ImageStore(folder.Path);
        string outside = Path.Combine(folder.Path, "keep.txt");
        File.WriteAllText(outside, "x");

        store.Delete("keep.txt");
        store.Delete(@"images\..\keep.txt");
        store.Delete(outside);

        Assert.True(File.Exists(outside));
    }
}
