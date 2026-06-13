using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Estimates;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Estimates;

// SALES-LINE-CRUD: estimates are Quote(Type=Estimate) rows; these cover the new
// line CRUD that itemizes them (catalog parts + lump-sum unknowns) and keeps
// EstimatedAmount synced to the line sum.
public class EstimateLineCrudHandlerTests
{
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly Mock<IMediator> _mediator = new();

    public EstimateLineCrudHandlerTests()
    {
        _partRepo
            .Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new Part { Id = id, Status = PartStatus.Active });
        _mediator
            .Setup(m => m.Send(It.IsAny<GetEstimateQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EstimateDetailResponseModel)null!);
    }

    private static AppDbContext SeedEstimate(DateTimeOffset? convertedAt = null, params QuoteLine[] lines)
    {
        var db = TestDbContextFactory.Create();
        db.Customers.Add(new Customer { Id = 1, Name = "Acme" });
        var est = new Quote
        {
            Id = 1,
            Type = QuoteType.Estimate,
            CustomerId = 1,
            Status = QuoteStatus.Draft,
            Title = "Tooling estimate",
            EstimatedAmount = 0,
            ConvertedAt = convertedAt,
        };
        foreach (var l in lines) est.Lines.Add(l);
        db.Quotes.Add(est);
        db.SaveChanges();
        return db;
    }

    private static Quote Reload(AppDbContext db) =>
        db.Quotes.Include(q => q.Lines).First(q => q.Id == 1);

    [Fact]
    public async Task AddLine_LumpSumUnknown_AddsLineAndSyncsEstimatedAmount()
    {
        using var db = SeedEstimate();
        var handler = new AddEstimateLineHandler(db, _partRepo.Object, _mediator.Object);

        // No part = a lump-sum line representing an unknown.
        await handler.Handle(new AddEstimateLineCommand(1, new CreateQuoteLineModel(null, "Unknown machining", 1, 750m, null)), default);

        var est = Reload(db);
        est.Lines.Should().ContainSingle().Which.PartId.Should().BeNull();
        est.EstimatedAmount.Should().Be(750m);
        db.ActivityLogs.Local.Should().ContainSingle(a => a.Action == "line-added");
    }

    [Fact]
    public async Task AddLine_CatalogPartThenLumpSum_SumsBoth()
    {
        using var db = SeedEstimate();
        var handler = new AddEstimateLineHandler(db, _partRepo.Object, _mediator.Object);

        await handler.Handle(new AddEstimateLineCommand(1, new CreateQuoteLineModel(7, "Bracket", 10, 12m, null)), default);
        await handler.Handle(new AddEstimateLineCommand(1, new CreateQuoteLineModel(null, "Unknown finishing", 1, 80m, null)), default);

        var est = Reload(db);
        est.Lines.Should().HaveCount(2);
        est.Lines.Select(l => l.LineNumber).Should().BeEquivalentTo(new[] { 1, 2 });
        est.EstimatedAmount.Should().Be(10 * 12m + 80m);
    }

    [Fact]
    public async Task AddLine_ConvertedEstimate_Throws()
    {
        using var db = SeedEstimate(convertedAt: DateTimeOffset.UtcNow);
        var handler = new AddEstimateLineHandler(db, _partRepo.Object, _mediator.Object);

        var act = () => handler.Handle(new AddEstimateLineCommand(1, new CreateQuoteLineModel(null, "x", 1, 1m, null)), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*converted*");
    }

    [Fact]
    public async Task UpdateLine_ChangesAmount_AndResyncsTotal()
    {
        using var db = SeedEstimate(null, new QuoteLine { Id = 1, LineNumber = 1, Description = "Initial", Quantity = 1, UnitPrice = 100m });
        // EstimatedAmount was seeded as 0; ensure it tracks the line after edit.
        var handler = new UpdateEstimateLineHandler(db, _mediator.Object);

        await handler.Handle(new UpdateEstimateLineCommand(1, 1, new UpdateOrderLineRequestModel("Revised", 2, 150m, null)), default);

        var est = Reload(db);
        est.Lines.Single().UnitPrice.Should().Be(150m);
        est.EstimatedAmount.Should().Be(300m);
    }

    [Fact]
    public async Task DeleteLine_AllowsZeroLines_AndZeroesTotal()
    {
        using var db = SeedEstimate(null, new QuoteLine { Id = 1, LineNumber = 1, Description = "Only", Quantity = 1, UnitPrice = 100m });
        var handler = new DeleteEstimateLineHandler(db, _mediator.Object);

        await handler.Handle(new DeleteEstimateLineCommand(1, 1), default);

        var est = Reload(db);
        est.Lines.Should().BeEmpty();
        est.EstimatedAmount.Should().Be(0m);
        db.ActivityLogs.Local.Should().ContainSingle(a => a.Action == "line-removed");
    }
}
