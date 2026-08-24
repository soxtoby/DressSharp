using System.Collections.Immutable;
using DressSharp.Rules;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DressSharp.UnitTests;

public class RuleContextTests
{
    [Fact]
    public void Malformed_region_index_preserves_empty_span_intersection_semantics()
    {
        var regions = new MalformedRegionIndex(ImmutableArray.Create(
            new TextSpan(10, 5),
            new TextSpan(20, 0),
            new TextSpan(30, 5)));

        Assert.True(regions.Intersects(new TextSpan(0, 10)));
        Assert.True(regions.Intersects(new TextSpan(0, 11)));
        Assert.True(regions.Intersects(new TextSpan(15, 0)));
        Assert.True(regions.Intersects(new TextSpan(15, 4)));
        Assert.False(regions.Intersects(new TextSpan(16, 3)));
        Assert.True(regions.Intersects(new TextSpan(15, 5)));
        Assert.True(regions.Intersects(new TextSpan(20, 1)));
        Assert.False(regions.Intersects(new TextSpan(21, 1)));
    }

    [Fact]
    public void Malformed_region_index_uses_prior_overlapping_region_end()
    {
        var regions = new MalformedRegionIndex(ImmutableArray.Create(
            new TextSpan(0, 100),
            new TextSpan(50, 1)));

        Assert.True(regions.Intersects(new TextSpan(90, 1)));
        Assert.True(regions.Intersects(new TextSpan(100, 1)));
        Assert.True(regions.Intersects(new TextSpan(100, 0)));
    }
}
