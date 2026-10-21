using System;
using System.Text.RegularExpressions;
using AllSky.Publish;
using Xunit;

namespace AllSky.Publish.Tests;

public class CaptureIdFactoryTests
{
    // Riferimento calcolato con Python (uuid.uuid5 con lo stesso namespace e lo stesso nome):
    //   python3 -c 'import uuid; ns = uuid.UUID("5b3e7c9a-1d4f-4a8e-9c2b-7f0a6d3e8b15");
    //               print(uuid.uuid5(ns, "cam-01|2026-10-21T21:30:00.000Z"))'
    private const string ExpectedId = "3823e0f8-f2c1-54c3-be5e-c0982aca54dd";

    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 21, 30, 0, TimeSpan.Zero);

    private static readonly Regex CanonicalForm = new(
        "^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\\z");

    [Fact]
    public void Create_matches_python_uuid5_reference()
    {
        Assert.Equal(ExpectedId, CaptureIdFactory.Create("cam-01", T0));
    }

    [Fact]
    public void Create_same_device_and_instant_gives_same_id()
    {
        Assert.Equal(CaptureIdFactory.Create("cam-01", T0), CaptureIdFactory.Create("cam-01", T0));
    }

    [Fact]
    public void Create_one_millisecond_later_gives_different_id()
    {
        string later = CaptureIdFactory.Create("cam-01", T0.AddMilliseconds(1));

        Assert.Equal("dd90d117-707d-59d5-a0e1-52af52e8fc8d", later);
        Assert.NotEqual(ExpectedId, later);
    }

    [Fact]
    public void Create_other_device_gives_different_id()
    {
        Assert.Equal("b96683ff-ec9a-5f22-8ac8-a3e7ab9255a7", CaptureIdFactory.Create("cam-02", T0));
    }

    [Fact]
    public void Create_same_instant_with_other_offset_gives_same_id()
    {
        // 23:30 con offset +02:00 e' lo stesso istante di 21:30 UTC.
        var sameInstant = new DateTimeOffset(2026, 10, 21, 23, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal(ExpectedId, CaptureIdFactory.Create("cam-01", sameInstant));
    }

    [Fact]
    public void Create_has_version_5_and_rfc_variant()
    {
        string id = CaptureIdFactory.Create("cam-01", T0);

        Assert.Equal(36, id.Length);
        Assert.Equal('5', id[14]);
        Assert.True("89ab".IndexOf(id[19]) >= 0, "La variante deve essere 10xx.");
        Assert.True(CanonicalForm.IsMatch(id), "Forma canonica minuscola con trattini attesa.");
    }

    [Fact]
    public void Create_rejects_empty_device_id()
    {
        Assert.Throws<ArgumentException>(() => CaptureIdFactory.Create("", T0));
    }

    [Fact]
    public void Create_rejects_device_id_with_separator()
    {
        Assert.Throws<ArgumentException>(() => CaptureIdFactory.Create("cam|01", T0));
    }

    [Fact]
    public void Create_rejects_null_device_id()
    {
        Assert.Throws<ArgumentNullException>(() => CaptureIdFactory.Create(null!, T0));
    }
}
