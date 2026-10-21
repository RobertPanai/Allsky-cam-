using System;
using AllSky.Publish;
using Xunit;

namespace AllSky.Publish.Tests;

public class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 900)]
    [InlineData(7, 900)]
    [InlineData(30, 900)]
    [InlineData(1000, 900)]
    public void Full_jitter_doubles_from_30_seconds_and_caps_at_15_minutes(int attempt, int expectedSeconds)
    {
        var policy = new RetryPolicy(new FixedJitter(1.0));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), policy.GetDelay(attempt));
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(6, 450)]
    public void Zero_jitter_gives_half_of_the_computed_delay(int attempt, int expectedSeconds)
    {
        var policy = new RetryPolicy(new FixedJitter(0.0));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), policy.GetDelay(attempt));
    }

    [Fact]
    public void Middle_jitter_gives_three_quarters_of_the_computed_delay()
    {
        var policy = new RetryPolicy(new FixedJitter(0.5));

        Assert.Equal(TimeSpan.FromSeconds(22.5), policy.GetDelay(1));
    }

    [Fact]
    public void Attempts_below_one_are_treated_as_one()
    {
        var policy = new RetryPolicy(new FixedJitter(1.0));

        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(-3));
    }

    [Fact]
    public void Larger_retry_after_wins_even_above_maximum()
    {
        var policy = new RetryPolicy(new FixedJitter(1.0));

        Assert.Equal(TimeSpan.FromMinutes(5), policy.GetDelay(1, TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromHours(2), policy.GetDelay(6, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Smaller_retry_after_is_ignored()
    {
        var policy = new RetryPolicy(new FixedJitter(1.0));

        Assert.Equal(TimeSpan.FromSeconds(120), policy.GetDelay(3, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Retry_after_is_capped_at_24_hours()
    {
        var policy = new RetryPolicy(new FixedJitter(1.0));

        Assert.Equal(TimeSpan.FromHours(24), policy.GetDelay(1, TimeSpan.FromHours(48)));
    }

    [Fact]
    public void Custom_base_and_maximum_are_used()
    {
        var policy = new RetryPolicy(new FixedJitter(1.0), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(8), policy.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetDelay(5));
    }

    [Fact]
    public void Constructor_rejects_null_jitter_and_non_positive_delays()
    {
        Assert.Throws<ArgumentNullException>(() => new RetryPolicy(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(new FixedJitter(1.0), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(new FixedJitter(1.0), null, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void System_jitter_returns_values_in_zero_to_one()
    {
        var jitter = new SystemJitter();

        for (int i = 0; i < 100; i++)
        {
            double value = jitter.NextDouble();
            Assert.True(value >= 0.0 && value < 1.0, $"Valore di jitter fuori da [0, 1): {value}");
        }
    }

    private sealed class FixedJitter : IJitter
    {
        private readonly double _value;

        public FixedJitter(double value)
        {
            _value = value;
        }

        public double NextDouble() => _value;
    }
}
