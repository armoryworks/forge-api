using FluentAssertions;
using MediatR;
using Moq;
using Forge.Api.Controllers;
using Forge.Api.Features.Reports;
using Forge.Core.Models;

namespace Forge.Tests.Reports;

/// <summary>
/// The reports UI sends the range end as a date-only value (end-day 00:00Z). The controller
/// must widen that to the last instant of the end day so activity later on the end day is
/// included, while leaving a real timestamp end (e.g. the "now" default) untouched.
/// </summary>
public class ReportsControllerDateRangeTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndDay = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastInstantOfEndDay = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero).AddTicks(-10);

    private static (ReportsController Controller, List<object> Sent) CreateController()
    {
        var sent = new List<object>();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetExpenseSummaryReportQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<List<ExpenseSummaryReportItem>>, CancellationToken>((q, _) => sent.Add(q))
            .ReturnsAsync([]);
        mediator.Setup(m => m.Send(It.IsAny<GetRdReportQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<List<RdReportItem>>, CancellationToken>((q, _) => sent.Add(q))
            .ReturnsAsync([]);
        mediator.Setup(m => m.Send(It.IsAny<GetQuoteToCloseReportQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<List<QuoteToCloseReportItem>>, CancellationToken>((q, _) => sent.Add(q))
            .ReturnsAsync([]);
        return (new ReportsController(mediator.Object), sent);
    }

    [Fact]
    public async Task DateOnlyEnd_IsWidenedToLastInstantOfEndDay()
    {
        var (controller, sent) = CreateController();

        await controller.GetExpenseSummary(Start, EndDay);

        var query = sent.Should().ContainSingle().Which.Should().BeOfType<GetExpenseSummaryReportQuery>().Subject;
        query.Start.Should().Be(Start);
        query.End.Should().Be(LastInstantOfEndDay);
        DateOnly.FromDateTime(query.End.UtcDateTime).Should().Be(new DateOnly(2026, 11, 20));
    }

    [Fact]
    public async Task TimestampEnd_IsLeftUnchanged()
    {
        var (controller, sent) = CreateController();
        var now = new DateTimeOffset(2026, 11, 20, 17, 45, 12, TimeSpan.Zero);

        await controller.GetExpenseSummary(Start, now);

        sent.Should().ContainSingle().Which.As<GetExpenseSummaryReportQuery>().End.Should().Be(now);
    }

    [Fact]
    public async Task NullableEnd_IsWidenedWhenDateOnly_AndStaysNullWhenOmitted()
    {
        var (controller, sent) = CreateController();

        await controller.GetRdReport(null, EndDay);
        await controller.GetRdReport(null, null);

        sent.Should().HaveCount(2);
        sent[0].As<GetRdReportQuery>().End.Should().Be(LastInstantOfEndDay);
        sent[1].As<GetRdReportQuery>().End.Should().BeNull();
    }

    [Fact]
    public async Task DefaultedRange_WidensExplicitDateOnlyEnd()
    {
        var (controller, sent) = CreateController();

        await controller.GetQuoteToClose(null, EndDay);

        var query = sent.Should().ContainSingle().Which.As<GetQuoteToCloseReportQuery>();
        query.End.Should().Be(LastInstantOfEndDay);
        query.Start.Should().Be(EndDay.AddMonths(-12));
    }
}
