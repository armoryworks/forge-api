using FluentAssertions;

using Forge.Core.Entities;
using Forge.Data.Extensions;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Activity;

public class ActivityChangeSummaryTests
{
    [Theory]
    [InlineData("creditLimit", "credit limit")]
    [InlineData("IsFdaRegulated", "FDA regulated")]
    [InlineData("StockUomId", "stock UoM ID")]
    [InlineData("HtsCode", "HTS code")]
    [InlineData("QcInspection", "QC inspection")]
    [InlineData("CustomerPO", "customer PO")]
    [InlineData("set-default", "set default")]
    [InlineData("line1", "line 1")]
    [InlineData("1099-vendor", "1099 vendor")]
    public void Label_humanizes_property_names(string name, string expected) =>
        ActivityChangeSummary.Label(name).Should().Be(expected);

    [Theory]
    [InlineData("QcInspection", "QC Inspection")]
    [InlineData("SalesOrder", "Sales Order")]
    [InlineData("BOMLine", "BOM Line")]
    public void TitleLabel_keeps_acronyms_upper_case(string name, string expected) =>
        ActivityChangeSummary.TitleLabel(name).Should().Be(expected);

    [Fact]
    public void SentenceLabel_capitalizes_only_the_first_word() =>
        ActivityChangeSummary.SentenceLabel("InProgress").Should().Be("In progress");

    [Theory]
    [InlineData("isFdaRegulated: True", "FDA regulated: yes")]
    [InlineData("isActive: False", "active: no")]
    [InlineData("owner: Ada Byron", "owner: Ada Byron")]
    [InlineData("items (1 added, 1 removed)", "items (1 added, 1 removed)")]
    [InlineData("expectedDeliveryDate", "expected delivery date")]
    public void Entry_humanizes_the_name_and_keeps_the_value(string entry, string expected) =>
        ActivityChangeSummary.Entry(entry).Should().Be(expected);

    [Fact]
    public void Updated_reads_naturally_for_one_and_many_fields()
    {
        ActivityChangeSummary.Updated([]).Should().BeNull();
        ActivityChangeSummary.Updated(["creditLimit"]).Should().Be("Updated credit limit");
        ActivityChangeSummary.Updated(["Name", "Description", "Status"])
            .Should().Be("Updated 3 fields: name, description, status");
    }

    [Fact]
    public async Task ChangedProperties_ignores_values_that_were_resent_unchanged()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme", Email = "a@example.com" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        customer.Name = "Acme";
        customer.Email = "b@example.com";
        db.ChangeTracker.DetectChanges();

        ActivityChangeSummary.ChangedProperties(db.Entry(customer)).Should().Equal("Email");
    }
}
