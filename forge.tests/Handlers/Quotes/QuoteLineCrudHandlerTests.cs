using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quotes;

// SALES-LINE-CRUD: coverage for the new add/delete-line paths on quotes.
public class QuoteLineCrudHandlerTests
{
    private readonly Mock<IQuoteRepository> _repo = new();
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly Mock<IMediator> _mediator = new();

    public QuoteLineCrudHandlerTests()
    {
        _partRepo
            .Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new Part { Id = id, Status = PartStatus.Active });
        _mediator
            .Setup(m => m.Send(It.IsAny<GetQuoteByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuoteDetailResponseModel)null!);
    }

    private static Quote DraftQuote(params QuoteLine[] lines)
    {
        var quote = new Quote { Id = 1, Status = QuoteStatus.Draft };
        foreach (var l in lines) quote.Lines.Add(l);
        return quote;
    }

    [Fact]
    public async Task AddLine_AppendsLumpSumLine_WithNextLineNumber()
    {
        var quote = DraftQuote(new QuoteLine { Id = 1, LineNumber = 1, Description = "Existing", Quantity = 1, UnitPrice = 10 });
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(quote);
        using var db = TestDbContextFactory.Create();
        var handler = new AddQuoteLineHandler(_repo.Object, _partRepo.Object, db, _mediator.Object);

        // PartId null = lump-sum / ad-hoc line (an "unknown").
        await handler.Handle(new AddQuoteLineCommand(1, new CreateQuoteLineModel(null, "TBD tooling", 1, 500m, "estimate")), default);

        quote.Lines.Should().HaveCount(2);
        var added = quote.Lines.Single(l => l.Description == "TBD tooling");
        added.LineNumber.Should().Be(2);
        added.PartId.Should().BeNull();
        added.UnitPrice.Should().Be(500m);
        db.ActivityLogs.Local.Should().ContainSingle(a => a.Action == "line-added");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddLine_WithCatalogPart_ChecksPartActive()
    {
        var quote = DraftQuote(new QuoteLine { Id = 1, LineNumber = 1, Description = "Existing", Quantity = 1, UnitPrice = 10 });
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(quote);
        using var db = TestDbContextFactory.Create();
        var handler = new AddQuoteLineHandler(_repo.Object, _partRepo.Object, db, _mediator.Object);

        await handler.Handle(new AddQuoteLineCommand(1, new CreateQuoteLineModel(7, "Catalog part", 3, 20m, null)), default);

        quote.Lines.Should().Contain(l => l.PartId == 7);
        _partRepo.Verify(r => r.FindAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddLine_NonDraftQuote_Throws()
    {
        var quote = new Quote { Id = 1, Status = QuoteStatus.Sent };
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(quote);
        using var db = TestDbContextFactory.Create();
        var handler = new AddQuoteLineHandler(_repo.Object, _partRepo.Object, db, _mediator.Object);

        var act = () => handler.Handle(new AddQuoteLineCommand(1, new CreateQuoteLineModel(null, "x", 1, 1m, null)), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*draft*");
    }

    [Fact]
    public async Task DeleteLine_RemovesLine_WhenMoreThanOne()
    {
        var quote = DraftQuote(
            new QuoteLine { Id = 1, LineNumber = 1, Description = "Keep", Quantity = 1, UnitPrice = 10 },
            new QuoteLine { Id = 2, LineNumber = 2, Description = "Drop", Quantity = 1, UnitPrice = 20 });
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(quote);
        using var db = TestDbContextFactory.Create();
        var handler = new DeleteQuoteLineHandler(_repo.Object, db, _mediator.Object);

        await handler.Handle(new DeleteQuoteLineCommand(1, 2), default);

        quote.Lines.Should().ContainSingle().Which.Id.Should().Be(1);
        db.ActivityLogs.Local.Should().ContainSingle(a => a.Action == "line-removed");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteLine_LastRemainingLine_Throws()
    {
        var quote = DraftQuote(new QuoteLine { Id = 1, LineNumber = 1, Description = "Only", Quantity = 1, UnitPrice = 10 });
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(quote);
        using var db = TestDbContextFactory.Create();
        var handler = new DeleteQuoteLineHandler(_repo.Object, db, _mediator.Object);

        var act = () => handler.Handle(new DeleteQuoteLineCommand(1, 1), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*at least one line*");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
