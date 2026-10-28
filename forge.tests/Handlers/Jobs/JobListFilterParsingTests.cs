using FluentAssertions;

using Forge.Core.Models;

namespace Forge.Tests.Handlers.Jobs;

public class JobListFilterParsingTests
{
    [Theory]
    [InlineData("jobNumber:contains:J-1")]
    [InlineData("TITLE:notEquals:widget")]
    [InlineData("customerName:startsWith:acme")]
    [InlineData("currentStageId:in:3|4")]
    [InlineData("priorityName:in:High|urgent")]
    [InlineData("disposition:in:Scrap")]
    [InlineData("billingStatus:in:Invoiced")]
    [InlineData("isOverdue:in:true")]
    [InlineData("dueDate:range:2026-01-01..2026-01-31")]
    [InlineData("createdAt:range:..2026-01-31T00:00:00Z")]
    [InlineData("quantity:range:5..")]
    [InlineData("quotedPrice:range:1.5..2.25")]
    public void Accepts_whitelisted_fields_with_fitting_operators(string raw)
    {
        JobListFilter.Parse(raw, out var error).Should().NotBeNull();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("jobNumber")]
    [InlineData("jobNumber:contains")]
    [InlineData(":contains:x")]
    [InlineData("description:contains:x")]
    [InlineData("estimatedMaterialCost:range:1..2")]
    [InlineData("jobNumber:in:J-1")]
    [InlineData("jobNumber:contains:   ")]
    [InlineData("currentStageId:in:three")]
    [InlineData("priorityName:in:Critical")]
    [InlineData("priorityName:in:1")]
    [InlineData("billingStatus:in:Paid")]
    [InlineData("isOverdue:in:yes")]
    [InlineData("dueDate:range:2026-01-01")]
    [InlineData("dueDate:range:..")]
    [InlineData("dueDate:range:soon..later")]
    [InlineData("quantity:contains:5")]
    public void Refuses_unknown_fields_bad_operators_and_bad_values(string raw)
    {
        JobListFilter.Parse(raw, out var error).Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_date_range_covers_whole_utc_days()
    {
        var range = JobListFilterValues.DateRange("2026-03-01..2026-03-31")!.Value;

        range.From.Should().Be(DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
        range.ToExclusive.Should().Be(DateTimeOffset.Parse("2026-04-01T00:00:00Z"));
    }

    [Fact]
    public void Every_field_key_is_unique_regardless_of_case()
    {
        JobListFields.All.Select(f => f.Key.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        JobListFields.Find("JOBNUMBER")!.Key.Should().Be("jobNumber");
        JobListFields.Find("nope").Should().BeNull();
    }
}
