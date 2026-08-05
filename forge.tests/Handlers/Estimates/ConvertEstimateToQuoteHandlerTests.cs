using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.Estimates;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Estimates;

public class ConvertEstimateToQuoteHandlerTests
{
    private readonly Mock<IQuoteRepository> _quoteRepo = new();
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly ConvertEstimateToQuoteHandler _handler;
    private readonly Data.Context.AppDbContext _db;
    private readonly Faker _faker = new();

    public ConvertEstimateToQuoteHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("QUO-0001");
        // Resolves parts straight from the test context so replace-with-part
        // resolutions see exactly what each test seeded (null when absent).
        _partRepo.Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => _db.Parts.FirstOrDefault(p => p.Id == id));
        _handler = new ConvertEstimateToQuoteHandler(
            _db, _quoteRepo.Object, _partRepo.Object, new CustomerPriceResolver(_db, new SystemClock()));
    }

    private async Task<(Customer Customer, Quote Estimate)> SeedEstimateAsync(params QuoteLine[] lines)
    {
        var customer = new Customer { Name = _faker.Company.CompanyName() };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var estimate = new Quote
        {
            Type = QuoteType.Estimate,
            Title = "Test Estimate",
            CustomerId = customer.Id,
            Status = QuoteStatus.Sent,
        };
        foreach (var line in lines) estimate.Lines.Add(line);
        _db.Quotes.Add(estimate);
        await _db.SaveChangesAsync();
        return (customer, estimate);
    }

    [Fact]
    public async Task Handle_ValidEstimate_CreatesQuoteWithSourceLink()
    {
        // Arrange
        var (_, estimate) = await SeedEstimateAsync();
        estimate.EstimatedAmount = 3000m;
        await _db.SaveChangesAsync();

        var command = new ConvertEstimateToQuoteCommand(estimate.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        // Original estimate should be marked as converted
        var updatedEstimate = _db.Quotes.First(q => q.Id == estimate.Id);
        updatedEstimate.Status.Should().Be(QuoteStatus.ConvertedToQuote);

        // New quote should link back via SourceEstimateId
        var newQuote = _db.Quotes.First(q => q.Id == result.Id);
        newQuote.Type.Should().Be(QuoteType.Quote);
        newQuote.SourceEstimateId.Should().Be(estimate.Id);
    }

    [Fact] // #24 — line items (incl. lump-sum, PartId == null) must carry into the new quote.
    public async Task Handle_EstimateWithLines_CopiesLinesIntoQuote()
    {
        var part = new Part { PartNumber = "P-24", Name = "Widget" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var (_, estimate) = await SeedEstimateAsync(
            new QuoteLine { PartId = part.Id, Description = "Catalog line", Quantity = 2m, UnitPrice = 10m, LineNumber = 1 },
            new QuoteLine { PartId = null, Description = "Lump-sum line", Quantity = 1m, UnitPrice = 250m, LineNumber = 2 });

        var result = await _handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id), CancellationToken.None);

        result.LineCount.Should().Be(2, "both estimate lines must transition into the quote");
        result.Total.Should().Be(270m, "2×10 + 1×250");

        var copied = _db.QuoteLines.Where(l => l.QuoteId == result.Id).OrderBy(l => l.LineNumber).ToList();
        copied.Should().HaveCount(2);
        copied[0].PartId.Should().Be(part.Id);
        copied[1].PartId.Should().BeNull("without a resolution, lump-sum lines copy as-is (backward-compatible default)");
        copied[1].Description.Should().Be("Lump-sum line");
    }

    [Fact] // #24 — Eliminate resolution drops the lump-sum line and renumbers the survivors.
    public async Task Handle_EliminateResolution_DropsLineAndRenumbers()
    {
        var part = new Part { PartNumber = "P-24", Name = "Widget" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var (_, estimate) = await SeedEstimateAsync(
            new QuoteLine { PartId = null, Description = "Lump-sum line", Quantity = 1m, UnitPrice = 250m, LineNumber = 1 },
            new QuoteLine { PartId = part.Id, Description = "Catalog line", Quantity = 2m, UnitPrice = 10m, LineNumber = 2 });
        var lumpSumLineId = estimate.Lines.First(l => l.PartId == null).Id;

        var result = await _handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id, new[]
        {
            new EstimateLineResolutionModel(lumpSumLineId, EstimateLineResolutionAction.Eliminate, null, null),
        }), CancellationToken.None);

        result.LineCount.Should().Be(1);
        result.Total.Should().Be(20m, "only the catalog line survives");

        var copied = _db.QuoteLines.Where(l => l.QuoteId == result.Id).ToList();
        copied.Should().HaveCount(1);
        copied[0].PartId.Should().Be(part.Id);
        copied[0].LineNumber.Should().Be(1, "surviving lines renumber sequentially");
    }

    [Fact] // #24 — ReplaceWithPart attaches the part and honors the caller's explicit price.
    public async Task Handle_ReplaceResolutionWithExplicitPrice_AttachesPartAndPrice()
    {
        var part = new Part { PartNumber = "P-77", Name = "Bracket", Status = PartStatus.Active };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var (_, estimate) = await SeedEstimateAsync(
            new QuoteLine { PartId = null, Description = "Unknown bracket", Quantity = 4m, UnitPrice = 100m, LineNumber = 1 });
        var lineId = estimate.Lines.Single().Id;

        var result = await _handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id, new[]
        {
            new EstimateLineResolutionModel(lineId, EstimateLineResolutionAction.ReplaceWithPart, part.Id, 42.5m),
        }), CancellationToken.None);

        var copied = _db.QuoteLines.Single(l => l.QuoteId == result.Id);
        copied.PartId.Should().Be(part.Id);
        copied.UnitPrice.Should().Be(42.5m, "an explicit caller price always wins");
        copied.Quantity.Should().Be(4m, "quantity carries over from the estimate line");
        result.Total.Should().Be(170m);
    }

    [Fact] // #24 / AUDIT-19-S1 — replace without a price falls through to the customer's price list.
    public async Task Handle_ReplaceResolutionWithoutPrice_PrefillsFromCustomerPriceList()
    {
        var part = new Part { PartNumber = "P-88", Name = "Gasket", Status = PartStatus.Active };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var (customer, estimate) = await SeedEstimateAsync(
            new QuoteLine { PartId = null, Description = "Unknown gasket", Quantity = 2m, UnitPrice = 500m, LineNumber = 1 });
        var lineId = estimate.Lines.Single().Id;

        _db.PriceLists.Add(new PriceList
        {
            Name = "Customer list",
            CustomerId = customer.Id,
            IsActive = true,
            Entries = { new PriceListEntry { PartId = part.Id, UnitPrice = 12.34m } },
        });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id, new[]
        {
            new EstimateLineResolutionModel(lineId, EstimateLineResolutionAction.ReplaceWithPart, part.Id, null),
        }), CancellationToken.None);

        var copied = _db.QuoteLines.Single(l => l.QuoteId == result.Id);
        copied.PartId.Should().Be(part.Id);
        copied.UnitPrice.Should().Be(12.34m, "the customer's price-list price prefills when the caller omits one");
    }

    [Fact] // #24 — replacing with a part that doesn't exist must fail the convert.
    public async Task Handle_ReplaceResolutionWithUnknownPart_ThrowsKeyNotFoundException()
    {
        var (_, estimate) = await SeedEstimateAsync(
            new QuoteLine { PartId = null, Description = "Lump sum", Quantity = 1m, UnitPrice = 100m, LineNumber = 1 });
        var lineId = estimate.Lines.Single().Id;

        var act = () => _handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id, new[]
        {
            new EstimateLineResolutionModel(lineId, EstimateLineResolutionAction.ReplaceWithPart, 99999, null),
        }), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _db.Quotes.Count(q => q.Type == QuoteType.Quote).Should().Be(0, "no quote may be created on a failed convert");
    }

    [Fact] // #24 — a resolution must target a line that belongs to the estimate.
    public async Task Handle_ResolutionForForeignLine_ThrowsKeyNotFoundException()
    {
        var (_, estimate) = await SeedEstimateAsync(
            new QuoteLine { PartId = null, Description = "Lump sum", Quantity = 1m, UnitPrice = 100m, LineNumber = 1 });

        var act = () => _handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id, new[]
        {
            new EstimateLineResolutionModel(424242, EstimateLineResolutionAction.Eliminate, null, null),
        }), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*424242*");
    }

    [Fact] // #24 — eliminating every line would create an unusable empty quote.
    public async Task Handle_EliminateAllLines_ThrowsInvalidOperationException()
    {
        var (_, estimate) = await SeedEstimateAsync(
            new QuoteLine { PartId = null, Description = "Only line", Quantity = 1m, UnitPrice = 100m, LineNumber = 1 });
        var lineId = estimate.Lines.Single().Id;

        var act = () => _handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id, new[]
        {
            new EstimateLineResolutionModel(lineId, EstimateLineResolutionAction.Eliminate, null, null),
        }), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact] // Activity-logging rule: the convert must leave an audit row on both quote rows.
    public async Task Handle_Convert_EmitsActivityLogOnEstimateAndQuote()
    {
        var (_, estimate) = await SeedEstimateAsync(
            new QuoteLine { PartId = null, Description = "Lump sum", Quantity = 1m, UnitPrice = 100m, LineNumber = 1 });

        var result = await _handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id), CancellationToken.None);

        _db.ActivityLogs.Count(a => a.Action == "converted-to-quote" && a.EntityType == "Quote" && a.EntityId == estimate.Id)
            .Should().Be(1);
        _db.ActivityLogs.Count(a => a.Action == "converted-to-quote" && a.EntityType == "Quote" && a.EntityId == result.Id)
            .Should().Be(1);
    }

    [Fact]
    public async Task Handle_NonExistentEstimate_ThrowsKeyNotFoundException()
    {
        var command = new ConvertEstimateToQuoteCommand(99999);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact] // #24 — the validator requires a part when the action is ReplaceWithPart.
    public void Validator_ReplaceWithPartWithoutPartId_Fails()
    {
        var validator = new ConvertEstimateToQuoteValidator();

        var invalid = validator.Validate(new ConvertEstimateToQuoteCommand(1, new[]
        {
            new EstimateLineResolutionModel(1, EstimateLineResolutionAction.ReplaceWithPart, null, null),
        }));
        var valid = validator.Validate(new ConvertEstimateToQuoteCommand(1, new[]
        {
            new EstimateLineResolutionModel(1, EstimateLineResolutionAction.ReplaceWithPart, 5, 10m),
            new EstimateLineResolutionModel(2, EstimateLineResolutionAction.Eliminate, null, null),
        }));

        invalid.IsValid.Should().BeFalse();
        valid.IsValid.Should().BeTrue();
    }
}
