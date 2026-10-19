using FluentAssertions;

using Forge.Api.Features.Terminology;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Terminology;

public class TerminologyLabelsTests
{
    private static TermLabels Labels(params (string Key, string Label)[] overrides) =>
        new(overrides.ToDictionary(o => o.Key, o => o.Label));

    [Fact]
    public void Defaults_UseTheCatalogTerms()
    {
        TermLabels.Defaults.Singular("job").Should().Be("Job");
        TermLabels.Defaults.Plural("job").Should().Be("Jobs");
        TermLabels.Defaults.Singular("sales_order").Should().Be("Sales Order");
    }

    [Fact]
    public void AnOverriddenSingular_DerivesItsPlural()
    {
        var labels = Labels(("entity_job", "Work Order"));

        labels.Singular("job").Should().Be("Work Order");
        labels.Plural("job").Should().Be("Work Orders");
        labels.ForCount("job", 1).Should().Be("Work Order");
        labels.ForCount("job", 3).Should().Be("Work Orders");
    }

    [Fact]
    public void AnExplicitPlural_WinsAndBlankLabelsAreIgnored()
    {
        Labels(("entity_job", "Engagement"), ("label_jobs", "Tasks")).Plural("job").Should().Be("Tasks");
        Labels(("entity_job", "  ")).Singular("job").Should().Be("Job");
    }

    [Theory]
    [InlineData("Company", "Companies")]
    [InlineData("Batch", "Batches")]
    [InlineData("Day", "Days")]
    [InlineData("Work Order", "Work Orders")]
    public void Pluralize_MirrorsTheUiRules(string singular, string plural) =>
        TermLabels.Pluralize(singular).Should().Be(plural);

    [Fact]
    public void Substitute_RenamesCatalogWordsAndKeepsTheirCase()
    {
        var labels = Labels(("entity_job", "Work Order"), ("entity_customer", "Client"));

        labels.Substitute("Jobs").Should().Be("Work Orders");
        labels.Substitute("Parent Job Number").Should().Be("Parent Work Order Number");
        labels.Substitute("Rework Job #").Should().Be("Rework Work Order #");
        labels.Substitute("Customer Name").Should().Be("Client Name");
        labels.Substitute("2 jobs created").Should().Be("2 work orders created");
        labels.Substitute("Reworked").Should().Be("Reworked");
    }

    [Fact]
    public void Substitute_LeavesProtectedPhrasesAndUnrenamedTermsAlone()
    {
        var labels = Labels(("entity_lead", "Prospect"));

        labels.Substitute("Lead Time Days").Should().Be("Lead Time Days");
        labels.Substitute("Lead Source").Should().Be("Prospect Source");
        labels.Substitute("Job Number").Should().Be("Job Number");
        TermLabels.Defaults.Substitute("Job Number").Should().Be("Job Number");
    }

    [Fact]
    public async Task GetAsync_ReadsTheInstallOverrides()
    {
        using var db = TestDbContextFactory.Create();
        db.TerminologyEntries.AddRange(
            new TerminologyEntry { Key = "entity_job", Label = "Work Order" },
            new TerminologyEntry { Key = "entity_customer", Label = string.Empty },
            new TerminologyEntry { Key = "entity_job:es", Label = "Orden de trabajo" });
        await db.SaveChangesAsync();

        var labels = await new TerminologyLabels(db).GetAsync(CancellationToken.None);

        labels.Singular("job").Should().Be("Work Order");
        labels.Plural("job").Should().Be("Work Orders");
        labels.Singular("customer").Should().Be("Customer");
    }
}
