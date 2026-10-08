using FluentAssertions;
using FluentValidation;
using MediatR;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Api.Features.SalesTax;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quotes;

public class DuplicateQuoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IMediator> _taxMediator = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly QuoteRepository _repo;

    public DuplicateQuoteTests()
    {
        _repo = new QuoteRepository(_db);
        _mediator
            .Setup(m => m.Send(It.IsAny<GetQuoteByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<QuoteDetailResponseModel> q, CancellationToken ct) =>
                new GetQuoteByIdHandler(_repo).Handle((GetQuoteByIdQuery)q, ct));
        SetDefaultTaxRate(0.07m);
    }

    private void SetDefaultTaxRate(decimal rate) =>
        _taxMediator
            .Setup(m => m.Send(It.IsAny<GetTaxRateForCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesTaxRateResponseModel(1, "UT", "UT", "UT", rate, Now.AddYears(-1), null, true, true, null));

    private DuplicateQuoteHandler Handler()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return new DuplicateQuoteHandler(
            _db, _repo, _mediator.Object, new TaxOverrideGuard(_db, _taxMediator.Object, clock.Object), _identifiers.Object);
    }

    private async Task<Quote> SeedAsync(QuoteStatus status = QuoteStatus.Accepted, decimal taxRate = 0.07m)
    {
        _db.Customers.Add(new Customer { Id = 2, Name = "Design Partner Co" });
        _db.Parts.Add(new Part { Id = 20, PartNumber = "BRK-100", Name = "Bracket", Status = PartStatus.Active });
        var quote = new Quote
        {
            Id = 5,
            Type = QuoteType.Quote,
            QuoteNumber = "QT-00005",
            CustomerId = 2,
            Status = status,
            Notes = "Net 30, FOB origin",
            TaxRate = taxRate,
            CustomerPO = "CPO-77",
            SentDate = Now.AddDays(-10),
            ExpirationDate = Now.AddDays(-1),
        };
        quote.Lines.Add(new QuoteLine { LineNumber = 2, Description = "Setup", Quantity = 1, UnitPrice = 150m });
        quote.Lines.Add(new QuoteLine { LineNumber = 1, PartId = 20, Description = "Bracket", Quantity = 12.5m, UnitPrice = 4.25m, Notes = "Zinc" });
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();
        return quote;
    }

    [Fact]
    public async Task Copies_the_lines_into_a_new_draft_with_the_next_number()
    {
        await SeedAsync();

        var result = await Handler().Handle(new DuplicateQuoteCommand(5), CancellationToken.None);

        result.Id.Should().NotBe(5);
        result.QuoteNumber.Should().NotBe("QT-00005").And.StartWith("QT");
        result.Status.Should().Be(nameof(QuoteStatus.Draft));
        result.CustomerId.Should().Be(2);
        result.Notes.Should().Be("Net 30, FOB origin");
        result.TaxRate.Should().Be(0.07m);
        result.SentDate.Should().BeNull();
        result.ExpirationDate.Should().BeNull();
        result.CustomerPO.Should().BeNull();
        var lines = result.Lines.OrderBy(l => l.LineNumber).ToList();
        lines.Should().HaveCount(2);
        lines[0].Should().BeEquivalentTo(new { LineNumber = 1, PartId = (int?)20, Description = "Bracket", Quantity = 12.5m, UnitPrice = 4.25m, Notes = "Zinc" });
        lines[1].Should().BeEquivalentTo(new { LineNumber = 2, PartId = (int?)null, Description = "Setup", Quantity = 1m, UnitPrice = 150m });

        var source = await _repo.FindWithDetailsAsync(5, CancellationToken.None);
        source!.Status.Should().Be(QuoteStatus.Accepted);
        source.Lines.Should().HaveCount(2);
        _identifiers.Verify(i => i.IssueAsync(BusinessEntityType.Quote, result.Id, result.QuoteNumber, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Logs_created_with_the_source_number_on_the_new_quote_only()
    {
        await SeedAsync();

        var result = await Handler().Handle(new DuplicateQuoteCommand(5), CancellationToken.None);

        _db.ActivityLogs.Where(a => a.Action == "created").Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                EntityType = "Quote",
                EntityId = result.Id,
                Action = "created",
                Description = "Created (duplicated from QT-00005)",
            });
    }

    [Fact]
    public async Task Copies_the_payment_schedule_as_a_pending_draft()
    {
        await SeedAsync();
        var schedule = new PaymentSchedule { QuoteId = 5, Status = PaymentScheduleStatus.Active };
        schedule.Milestones.Add(new PaymentMilestone { Sequence = 1, Name = "Deposit", Percentage = 40m, Status = PaymentMilestoneStatus.Paid, PaidAmount = 100m });
        schedule.Milestones.Add(new PaymentMilestone { Sequence = 2, Name = "Balance", Percentage = 60m, DueTrigger = PaymentDueTrigger.NetDays, NetDays = 30 });
        _db.PaymentSchedules.Add(schedule);
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new DuplicateQuoteCommand(5), CancellationToken.None);

        var copied = _db.PaymentSchedules.Single(s => s.QuoteId == result.Id);
        copied.Status.Should().Be(PaymentScheduleStatus.Draft);
        _db.PaymentMilestones.Where(m => m.PaymentScheduleId == copied.Id).OrderBy(m => m.Sequence).ToList()
            .Should().SatisfyRespectively(
                deposit => deposit.Should().BeEquivalentTo(new { Name = "Deposit", Percentage = 40m, Status = PaymentMilestoneStatus.Pending, PaidAmount = (decimal?)null }),
                balance => balance.Should().BeEquivalentTo(new { Name = "Balance", Percentage = 60m, DueTrigger = PaymentDueTrigger.NetDays, NetDays = (int?)30 }));
    }

    [Fact]
    public async Task Tax_rate_off_the_default_needs_a_verified_certificate()
    {
        await SeedAsync(taxRate: 0m);

        var act = () => Handler().Handle(new DuplicateQuoteCommand(5), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*verified state tax certificate*");
        _db.Quotes.Should().ContainSingle();
    }

    [Fact]
    public async Task An_obsolete_part_blocks_the_copy()
    {
        await SeedAsync();
        (await _db.Parts.FindAsync(20))!.Status = PartStatus.Obsolete;
        await _db.SaveChangesAsync();

        var act = () => Handler().Handle(new DuplicateQuoteCommand(5), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Unknown_quote_throws_not_found()
    {
        var act = () => Handler().Handle(new DuplicateQuoteCommand(404), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*404*");
    }
}
