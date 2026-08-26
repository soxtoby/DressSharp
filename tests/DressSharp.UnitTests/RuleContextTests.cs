using System.Collections.Immutable;
using DressSharp.Rules;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using EasyAssertions;

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

        regions.Intersects(new TextSpan(0, 10)).ShouldBe(true);
        regions.Intersects(new TextSpan(0, 11)).ShouldBe(true);
        regions.Intersects(new TextSpan(15, 0)).ShouldBe(true);
        regions.Intersects(new TextSpan(15, 4)).ShouldBe(true);
        regions.Intersects(new TextSpan(16, 3)).ShouldBe(false);
        regions.Intersects(new TextSpan(15, 5)).ShouldBe(true);
        regions.Intersects(new TextSpan(20, 1)).ShouldBe(true);
        regions.Intersects(new TextSpan(21, 1)).ShouldBe(false);
    }

    [Fact]
    public void Malformed_region_index_uses_prior_overlapping_region_end()
    {
        var regions = new MalformedRegionIndex(ImmutableArray.Create(
            new TextSpan(0, 100),
            new TextSpan(50, 1)));

        regions.Intersects(new TextSpan(90, 1)).ShouldBe(true);
        regions.Intersects(new TextSpan(100, 1)).ShouldBe(true);
        regions.Intersects(new TextSpan(100, 0)).ShouldBe(true);
    }
}
