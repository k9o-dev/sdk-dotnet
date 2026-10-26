using System;
using System.Collections.Generic;
using K9o;
using Xunit;

namespace K9o.Tests;

public class KErrorTests
{
    [Fact]
    public void ConstructsWithRequiredFields()
    {
        var e = new KError("K9O_INVALID_FIELD_VALUE", "bad");
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
        Assert.Equal("bad", e.Message);
        Assert.Empty(e.Context);
        Assert.Null(e.Recommendation);
        Assert.Null(e.Status);
    }

    [Fact]
    public void IsException()
    {
        Assert.True(typeof(Exception).IsAssignableFrom(typeof(KError)));
    }

    [Fact]
    public void ToStringIncludesCodeAndStatus()
    {
        var e = new KError("K9O_TIER_CAP_HIT", "cap", null, null, 429, null);
        var s = e.ToString();
        Assert.Contains("K9O_TIER_CAP_HIT", s);
        Assert.Contains("429", s);
    }

    [Fact]
    public void WithRecommendationAddsHint()
    {
        var e = new KError("X", "y").WithRecommendation("do this");
        Assert.Equal("do this", e.Recommendation);
    }

    [Fact]
    public void ContextIsReadOnly()
    {
        var e = new KError("X", "y", new Dictionary<string, object?> { ["k"] = "v" }, null, null, null);
        Assert.Equal("v", e.Context["k"]);
        // IReadOnlyDictionary doesn't expose .Add — compile-time guarantee.
    }
}
