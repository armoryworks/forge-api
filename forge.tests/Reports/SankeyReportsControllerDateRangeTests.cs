using FluentAssertions;
using MediatR;
using Moq;
using Forge.Api.Controllers;
using Forge.Api.Features.Reports.Sankey;
using Forge.Core.Models;

namespace Forge.Tests.Reports;

/// <summary>
/// The sankey page sends the range end as a date-only value, like the reports page. The
/// controller must widen it to the last instant of the end day the same way ReportsController does.
/// </summary>
public class SankeyReportsControllerDateRangeTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndDay = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastInstantOfEndDay = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero).AddTicks(-10);

    private static (SankeyReportsController Controller, List<object> Sent) CreateController()
    {
        var sent = new List<object>();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetCustomerRevenueFlowQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<List<SankeyFlowItem>>, CancellationToken>((q, _) => sent.Add(q))
            .ReturnsAsync([]);
        mediator.Setup(m => m.Send(It.IsAny<GetQuoteToCashFlowQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<List<SankeyFlowItem>>, CancellationToken>((q, _) => sent.Add(q))
            .ReturnsAsync([]);
        mediator.Setup(m => m.Send(It.IsAny<GetExpenseFlowQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<List<SankeyFlowItem>>, CancellationToken>((q, _) => sent.Add(q))
            .ReturnsAsync([]);
        mediator.Setup(m => m.Send(It.IsAny<GetQualityRejectionFlowQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<List<SankeyFlowItem>>, CancellationToken>((q, _) => sent.Add(q))
            .ReturnsAsync([]);
        return (new SankeyReportsController(mediator.Object), sent);
    }

    [Fact]
    public async Task DateOnlyEnd_IsWidenedOnEveryDatedFlow()
    {
        var (controller, sent) = CreateController();

        await controller.GetCustomerRevenue(Start, EndDay);
        await controller.GetQuoteToCash(Start, EndDay);
        await controller.GetExpenseFlow(Start, EndDay);
        await controller.GetQualityRejection(Start, EndDay);

        sent.Should().HaveCount(4);
        sent[0].As<GetCustomerRevenueFlowQuery>().End.Should().Be(LastInstantOfEndDay);
        sent[0].As<GetCustomerRevenueFlowQuery>().Start.Should().Be(Start);
        sent[1].As<GetQuoteToCashFlowQuery>().End.Should().Be(LastInstantOfEndDay);
        sent[2].As<GetExpenseFlowQuery>().End.Should().Be(LastInstantOfEndDay);
        sent[3].As<GetQualityRejectionFlowQuery>().End.Should().Be(LastInstantOfEndDay);
    }

    [Fact]
    public async Task TimestampEnd_IsLeftUnchanged_AndOmittedEndStaysNull()
    {
        var (controller, sent) = CreateController();
        var now = new DateTimeOffset(2026, 11, 20, 17, 45, 12, TimeSpan.Zero);

        await controller.GetCustomerRevenue(Start, now);
        await controller.GetCustomerRevenue(null, null);

        sent[0].As<GetCustomerRevenueFlowQuery>().End.Should().Be(now);
        sent[1].As<GetCustomerRevenueFlowQuery>().End.Should().BeNull();
    }
}
