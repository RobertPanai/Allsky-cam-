using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AllSky.Core;
using AllSky.Publish;
using Xunit;

namespace AllSky.Publish.Tests;

public class DiskQueueTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 21, 0, 0, TimeSpan.Zero);

    private readonly string _root;
    private readonly string _folder;
    private readonly FixedClock _clock = new() { UtcNow = Start };

    public DiskQueueTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "allsky-publish-dq-" + Guid.NewGuid().ToString("N"));
        _folder = Path.Combine(_root, "queue");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Round_trip_keeps_metadata_and_rebuilds_image_path()
    {
        var queue = new DiskQueue(_folder, _clock);
        string id = Id(1);
        var item = new QueuedItem(id, Start, 175.0, 2.5, PublishMode.Twilight, -4.5, "ignored");

        queue.Enqueue(item, Bytes(1));

        Assert.Equal(1, queue.Count);
        QueuedItem stored = Assert.Single(queue.List());
        Assert.Equal(id, stored.CaptureId);
        Assert.Equal(Start, stored.CapturedAtUtc);
        Assert.Equal(175.0, stored.ExposureMs);
        Assert.Equal(2.5, stored.Gain);
        Assert.Equal(PublishMode.Twilight, stored.Mode);
        Assert.Equal(-4.5, stored.TemperatureC);
        Assert.Equal(Path.Combine(_folder, id + ".jpg"), stored.ImagePath);
        Assert.Equal(Bytes(1), queue.ReadImage(stored));
    }

    [Fact]
    public void List_is_sorted_oldest_first()
    {
        var queue = new DiskQueue(_folder, _clock);
        queue.Enqueue(Item(3, Start.AddMinutes(2)), Bytes(3));
        queue.Enqueue(Item(1, Start), Bytes(1));
        queue.Enqueue(Item(2, Start.AddMinutes(1)), Bytes(2));

        IReadOnlyList<QueuedItem> list = queue.List();

        Assert.Equal(new[] { Id(1), Id(2), Id(3) }, list.Select(i => i.CaptureId).ToArray());
    }

    [Fact]
    public void Limit_of_20_evicts_oldest_items_and_their_files()
    {
        var queue = new DiskQueue(_folder, _clock);
        for (int n = 1; n <= 22; n++)
        {
            queue.Enqueue(Item(n, Start.AddMinutes(n)), Bytes(n));
        }

        Assert.Equal(20, queue.Count);
        Assert.Equal(Id(3), queue.List()[0].CaptureId);
        Assert.Equal(Id(22), queue.List()[19].CaptureId);
        Assert.False(File.Exists(Path.Combine(_folder, Id(1) + ".jpg")));
        Assert.False(File.Exists(Path.Combine(_folder, Id(2) + ".json")));
    }

    [Fact]
    public void Limit_evicts_by_capture_time_not_insertion_order()
    {
        var queue = new DiskQueue(_folder, _clock, maxItems: 2);
        queue.Enqueue(Item(1, Start.AddMinutes(10)), Bytes(1));
        queue.Enqueue(Item(2, Start.AddMinutes(20)), Bytes(2));
        queue.Enqueue(Item(3, Start.AddMinutes(5)), Bytes(3)); // inserito ultimo ma e' il piu' vecchio

        Assert.Equal(new[] { Id(1), Id(2) }, queue.List().Select(i => i.CaptureId).ToArray());
        Assert.Null(queue.ReadImage(Item(3, Start.AddMinutes(5))));
    }

    [Fact]
    public void Restart_reloads_the_same_items()
    {
        var first = new DiskQueue(_folder, _clock);
        for (int n = 1; n <= 5; n++)
        {
            first.Enqueue(Item(n, Start.AddMinutes(n)), Bytes(n));
        }

        var second = new DiskQueue(_folder, _clock);

        Assert.Equal(5, second.Count);
        Assert.Equal(first.List().Select(i => i.CaptureId), second.List().Select(i => i.CaptureId));
        Assert.Equal(Bytes(4), second.ReadImage(second.List()[3]));
        Assert.Null(second.LastChangedUtc);
    }

    [Fact]
    public void Constructor_removes_orphans_and_keeps_valid_items_and_foreign_files()
    {
        var queue = new DiskQueue(_folder, _clock);
        queue.Enqueue(Item(1, Start), Bytes(1));

        // jpg senza json.
        File.WriteAllBytes(Path.Combine(_folder, Id(100) + ".jpg"), Bytes(100));
        // json illeggibile con il suo jpg.
        File.WriteAllText(Path.Combine(_folder, Id(101) + ".json"), "{not json");
        File.WriteAllBytes(Path.Combine(_folder, Id(101) + ".jpg"), Bytes(101));
        // .tmp residui.
        File.WriteAllBytes(Path.Combine(_folder, Id(102) + ".jpg.tmp"), Bytes(102));
        File.WriteAllText(Path.Combine(_folder, Id(102) + ".json.tmp"), "{}");
        // json valido senza jpg.
        File.WriteAllText(Path.Combine(_folder, Id(103) + ".json"), MetaJson(Id(103), Start));
        // file estraneo: non e' della coda, deve restare.
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "tenere");

        var reloaded = new DiskQueue(_folder, _clock);

        Assert.Equal(new[] { Id(1) }, reloaded.List().Select(i => i.CaptureId).ToArray());
        Assert.False(File.Exists(Path.Combine(_folder, Id(100) + ".jpg")));
        Assert.False(File.Exists(Path.Combine(_folder, Id(101) + ".json")));
        Assert.False(File.Exists(Path.Combine(_folder, Id(101) + ".jpg")));
        Assert.False(File.Exists(Path.Combine(_folder, Id(102) + ".jpg.tmp")));
        Assert.False(File.Exists(Path.Combine(_folder, Id(102) + ".json.tmp")));
        Assert.False(File.Exists(Path.Combine(_folder, Id(103) + ".json")));
        Assert.True(File.Exists(Path.Combine(_folder, "notes.txt")));
    }

    [Fact]
    public void Enqueue_with_same_capture_id_overwrites_it()
    {
        var queue = new DiskQueue(_folder, _clock);

        queue.Enqueue(Item(1, Start), Bytes(1));
        queue.Enqueue(Item(1, Start), Bytes(9));

        Assert.Equal(1, queue.Count);
        Assert.Equal(Bytes(9), queue.ReadImage(Item(1, Start)));
    }

    [Fact]
    public void Enqueue_leaves_no_tmp_files_behind()
    {
        var queue = new DiskQueue(_folder, _clock);

        queue.Enqueue(Item(1, Start), Bytes(1));
        queue.Enqueue(Item(2, Start), Bytes(2));

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        Assert.Equal(4, Directory.GetFiles(_folder).Length);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("..\\x")]
    [InlineData("a/b")]
    [InlineData("00000000-0000-4000-8000-00000000000A")]
    [InlineData("00000000-0000-4000-8000-000000000001\n")]
    [InlineData("")]
    [InlineData("00000000000040008000000000000001")]
    public void Malicious_or_malformed_capture_id_is_rejected(string captureId)
    {
        var queue = new DiskQueue(_folder, _clock);
        var item = new QueuedItem(captureId, Start, null, null, PublishMode.Day, null, "x");

        Assert.Throws<ArgumentException>(() => queue.Enqueue(item, Bytes(1)));
        Assert.Throws<ArgumentException>(() => queue.ReadImage(item));
        Assert.Throws<ArgumentException>(() => queue.Remove(captureId));
    }

    [Fact]
    public void Remove_never_touches_files_outside_the_folder()
    {
        Directory.CreateDirectory(_root);
        string outside = Path.Combine(_root, "outside.jpg");
        File.WriteAllBytes(outside, Bytes(7));
        var queue = new DiskQueue(_folder, _clock);

        Assert.Throws<ArgumentException>(() => queue.Remove("../outside"));
        Assert.Throws<ArgumentException>(() => queue.Remove(Path.Combine("..", "outside")));

        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Remove_deletes_item_and_reports_whether_it_existed()
    {
        var queue = new DiskQueue(_folder, _clock);
        queue.Enqueue(Item(1, Start), Bytes(1));

        Assert.True(queue.Remove(Id(1)));
        Assert.False(queue.Remove(Id(1)));
        Assert.Equal(0, queue.Count);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Null(queue.ReadImage(Item(1, Start)));
    }

    [Fact]
    public void TrimToNewest_removes_oldest_beyond_keep()
    {
        var queue = new DiskQueue(_folder, _clock);
        for (int n = 1; n <= 5; n++)
        {
            queue.Enqueue(Item(n, Start.AddMinutes(n)), Bytes(n));
        }

        Assert.Equal(3, queue.TrimToNewest(2));
        Assert.Equal(new[] { Id(4), Id(5) }, queue.List().Select(i => i.CaptureId).ToArray());
        Assert.Equal(0, queue.TrimToNewest(10));
    }

    [Fact]
    public void TrimToNewest_rejects_negative_keep()
    {
        var queue = new DiskQueue(_folder, _clock);

        Assert.Throws<ArgumentOutOfRangeException>(() => queue.TrimToNewest(-1));
    }

    [Fact]
    public void Read_of_unknown_but_valid_id_returns_null()
    {
        var queue = new DiskQueue(_folder, _clock);

        Assert.Null(queue.ReadImage(Item(42, Start)));
    }

    [Fact]
    public void Constructor_rejects_non_positive_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiskQueue(_folder, _clock, maxItems: 0));
    }

    [Fact]
    public void Last_changed_is_set_by_enqueue_from_the_clock()
    {
        var queue = new DiskQueue(_folder, _clock);
        _clock.UtcNow = Start.AddMinutes(3);

        queue.Enqueue(Item(1, Start), Bytes(1));

        Assert.Equal(Start.AddMinutes(3), queue.LastChangedUtc);
    }

    private static string Id(int n) => $"00000000-0000-4000-8000-{n:x12}";

    private static QueuedItem Item(int n, DateTimeOffset capturedAtUtc) =>
        new(Id(n), capturedAtUtc, 175.0, 0.0, PublishMode.Night, null, "ignored");

    private static byte[] Bytes(int n) => new[] { (byte)n, (byte)1, (byte)2, (byte)3 };

    private static string MetaJson(string id, DateTimeOffset at) =>
        "{\"captureId\":\"" + id + "\",\"capturedAtUtc\":\"" + at.ToString("O") +
        "\",\"exposureMs\":null,\"gain\":null,\"mode\":\"night\",\"temperatureC\":null}";

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
