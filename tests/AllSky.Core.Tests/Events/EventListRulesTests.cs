using AllSky.Core.Events;
using Xunit;

namespace AllSky.Core.Tests.Events;

public sealed class EventListRulesTests
{
    private static EventListItem Item(string night, string id, EventCategory category, int minute) => new(
        night,
        id,
        category,
        new DateTimeOffset(2026, 10, 19, 21, 0, 0, TimeSpan.Zero).AddMinutes(minute),
        3.0,
        EventLabelChoice.None,
        null);

    private static readonly EventListItem[] Sample =
    [
        Item("2026-10-19", "meteor-a", EventCategory.Meteor, 0),
        Item("2026-10-19", "bolide-a", EventCategory.Bolide, 10),
        Item("2026-10-20", "transit-b", EventCategory.Transit, 30),
        Item("2026-10-20", "meteor-b", EventCategory.Meteor, 5),
    ];

    [Fact]
    public void Filter_WithoutCriteria_KeepsEverything_NewestFirst()
    {
        IReadOnlyList<EventListItem> result = EventListRules.Filter(Sample, new EventFilter());

        Assert.Equal(new[] { "transit-b", "bolide-a", "meteor-b", "meteor-a" }, result.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Filter_ByNight_KeepsOnlyThatNight()
    {
        IReadOnlyList<EventListItem> result = EventListRules.Filter(Sample, new EventFilter("2026-10-20"));

        Assert.Equal(new[] { "transit-b", "meteor-b" }, result.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Filter_ByCategory_KeepsOnlyThatType()
    {
        IReadOnlyList<EventListItem> result = EventListRules.Filter(Sample, new EventFilter(Category: EventCategory.Meteor));

        Assert.Equal(new[] { "meteor-b", "meteor-a" }, result.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Filter_NightAndCategoryTogether_Intersect()
    {
        IReadOnlyList<EventListItem> result = EventListRules.Filter(Sample, new EventFilter("2026-10-19", EventCategory.Bolide));

        Assert.Equal(new[] { "bolide-a" }, result.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Filter_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(EventListRules.Filter(Sample, new EventFilter("2026-01-01")));
        Assert.Empty(EventListRules.Filter([], new EventFilter()));
    }

    [Fact]
    public void Matches_RejectsOtherNightAndCategory()
    {
        EventListItem meteor = Sample[0];

        Assert.True(EventListRules.Matches(meteor, new EventFilter()));
        Assert.False(EventListRules.Matches(meteor, new EventFilter("2026-10-20")));
        Assert.False(EventListRules.Matches(meteor, new EventFilter(Category: EventCategory.Transit)));
    }

    [Fact]
    public void Sort_BreaksTiesByIdDescending()
    {
        EventListItem a = Item("2026-10-19", "aaa", EventCategory.Meteor, 0);
        EventListItem b = Item("2026-10-19", "bbb", EventCategory.Meteor, 0);

        IReadOnlyList<EventListItem> result = EventListRules.Sort([a, b]);

        Assert.Equal(new[] { "bbb", "aaa" }, result.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Nights_AreDistinctAndNewestFirst()
    {
        IReadOnlyList<string> nights = EventListRules.Nights(Sample);

        Assert.Equal(new[] { "2026-10-20", "2026-10-19" }, nights.ToArray());
    }

    [Fact]
    public void Nights_OfNothing_IsEmpty()
    {
        Assert.Empty(EventListRules.Nights([]));
    }
}
