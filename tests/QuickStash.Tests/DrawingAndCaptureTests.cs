using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using QuickStash.Data;
using QuickStash.Services;

namespace QuickStash.Tests;

public class DrawingAndCaptureTests
{
    [Fact]
    public void Version_1_database_is_upgraded_in_place()
    {
        string folder = Path.Combine(Path.GetTempPath(), "QuickStashTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "quickstash.db");
        try
        {
            // A v0.1 database with one note.
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE Topics (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL COLLATE NOCASE, LastUsed TEXT NOT NULL);
                    CREATE TABLE TopicProcesses (TopicId INTEGER NOT NULL, ProcessName TEXT NOT NULL, PRIMARY KEY (TopicId, ProcessName));
                    CREATE TABLE Notes (Id INTEGER PRIMARY KEY AUTOINCREMENT, TopicId INTEGER NOT NULL, Text TEXT NOT NULL DEFAULT '',
                        ImagePath TEXT NULL, IsPinned INTEGER NOT NULL DEFAULT 0, PinX REAL NULL, PinY REAL NULL,
                        CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                    INSERT INTO Topics (Name, LastUsed) VALUES ('TLD', '2025-01-01T00:00:00.0000000Z');
                    INSERT INTO Notes (TopicId, Text, ImagePath, CreatedAt, UpdatedAt)
                        VALUES (1, 'old note', 'images\old.png', '2025-01-01T00:00:00.0000000Z', '2025-01-01T00:00:00.0000000Z');
                    PRAGMA user_version = 1;
                    """;
                cmd.ExecuteNonQuery();
            }

            using var db = new Database(path);
            Assert.Equal(Database.SchemaVersion, db.Scalar<long>("PRAGMA user_version;"));
            var note = new NoteRepository(db).GetByTopic(1).Single();
            Assert.Equal("old note", note.Text);
            Assert.Equal(@"images\old.png", note.ImagePath);
            Assert.Null(note.OriginalImagePath);
            Assert.Null(note.PinWidth);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Drawing_keeps_the_original_and_cleans_up_replaced_files()
    {
        using var folder = new TestDataFolder();
        var topic = folder.Topics.Create("TLD");
        var note = folder.Notes.Add(topic.Id, "map", @"images\shot.png");

        Assert.Empty(folder.Notes.SetDrawing(note.Id, @"images\drawn1.png", @"images\ink1.isf"));
        var drawn = folder.Notes.Get(note.Id)!;
        Assert.Equal(@"images\drawn1.png", drawn.ImagePath);
        Assert.Equal(@"images\shot.png", drawn.OriginalImagePath);
        Assert.True(drawn.HasDrawing);

        // Editing the drawing again replaces the previous drawn copy and strokes, never the original.
        Assert.Equal(new[] { @"images\drawn1.png", @"images\ink1.isf" },
            folder.Notes.SetDrawing(note.Id, @"images\drawn2.png", @"images\ink2.isf"));
        Assert.Equal(@"images\shot.png", folder.Notes.Get(note.Id)!.OriginalImagePath);

        Assert.Equal(new[] { @"images\drawn2.png", @"images\ink2.isf" }, folder.Notes.RemoveDrawing(note.Id));
        var reverted = folder.Notes.Get(note.Id)!;
        Assert.Equal(@"images\shot.png", reverted.ImagePath);
        Assert.Null(reverted.OriginalImagePath);
        Assert.Null(reverted.AnnotationPath);
    }

    [Fact]
    public void Deleting_a_drawn_note_reports_all_its_files()
    {
        using var folder = new TestDataFolder();
        var topic = folder.Topics.Create("TLD");
        var note = folder.Notes.Add(topic.Id, "", @"images\shot.png");
        folder.Notes.SetDrawing(note.Id, @"images\drawn.png", @"images\ink.isf");

        Assert.Equal(new[] { @"images\drawn.png", @"images\shot.png", @"images\ink.isf" }, folder.Notes.Delete(note.Id));
    }

    [Fact]
    public void Quick_capture_topics_are_created_and_linked_once()
    {
        using var folder = new TestDataFolder();
        var first = folder.Topics.GetOrCreateForProcess("tld.exe");
        var second = folder.Topics.GetOrCreateForProcess("tld.exe");

        Assert.Equal("tld", first.Name);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(new[] { "tld.exe" }, first.ProcessNames);

        // An existing topic with the same name gets linked instead of duplicated.
        folder.Topics.Create("skyrim");
        var skyrim = folder.Topics.GetOrCreateForProcess("skyrim.exe");
        Assert.Equal("skyrim", skyrim.Name);
        Assert.Contains("skyrim.exe", skyrim.ProcessNames);

        Assert.Equal(folder.Topics.GetOrCreateInbox().Id, folder.Topics.GetOrCreateInbox().Id);
    }

    [Fact]
    public void Pin_width_persists()
    {
        using var folder = new TestDataFolder();
        var topic = folder.Topics.Create("TLD");
        var note = folder.Notes.Add(topic.Id, "", null);
        folder.Notes.SetPinWidth(note.Id, 640);
        Assert.Equal(640, folder.Notes.Get(note.Id)!.PinWidth);
    }

    [Fact]
    public void Flatten_draws_strokes_at_the_screenshot_pixel_size()
    {
        var pixels = Enumerable.Repeat((byte)0, 200 * 100 * 4).ToArray();
        var black = BitmapSource.Create(200, 100, 96, 96, PixelFormats.Bgr32, null, pixels, 200 * 4);
        var stroke = new Stroke(new StylusPointCollection(new[] { new StylusPoint(10, 50), new StylusPoint(190, 50) }),
            new DrawingAttributes { Color = Colors.White, Width = 10, Height = 10 });

        var flat = DrawingService.Flatten(black, new StrokeCollection { stroke });

        Assert.Equal(200, flat.PixelWidth);
        Assert.Equal(100, flat.PixelHeight);
        var probe = new byte[4];
        flat.CopyPixels(new Int32Rect(100, 50, 1, 1), probe, 4, 0);
        Assert.True(probe[0] > 200 && probe[1] > 200 && probe[2] > 200, "stroke pixel should be white");
        flat.CopyPixels(new Int32Rect(100, 5, 1, 1), probe, 4, 0);
        Assert.True(probe[0] < 20, "background pixel should stay black");
    }

    [Fact]
    public void Strokes_round_trip_through_the_image_store()
    {
        using var folder = new TestDataFolder();
        var store = new ImageStore(folder.Path);
        var strokes = new StrokeCollection
        {
            new Stroke(new StylusPointCollection(new[] { new StylusPoint(1, 2), new StylusPoint(30, 40) })),
        };

        string path = store.SaveStrokes(strokes);
        var loaded = store.LoadStrokes(path);

        Assert.EndsWith(".isf", path);
        Assert.Single(loaded);
        Assert.Equal(30, loaded[0].StylusPoints[1].X, precision: 1);
        Assert.Empty(store.LoadStrokes(@"images\missing.isf"));
    }
}
