using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.DomainEvents;

public class OnSalesOrderConfirmedAutoCreateJobsTests
{
    [Fact]
    public async Task Confirmed_order_creates_jobs_for_all_of_its_lines()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateJobsForSalesOrderLinesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateJobsForSalesOrderLinesResponseModel(2, []));
        var handler = new OnSalesOrderConfirmed_AutoCreateJobs(
            mediator.Object, NullLogger<OnSalesOrderConfirmed_AutoCreateJobs>.Instance);

        await handler.Handle(new SalesOrderConfirmedEvent(501, 1), CancellationToken.None);

        mediator.Verify(m => m.Send(
            It.Is<CreateJobsForSalesOrderLinesCommand>(c => c.SalesOrderId == 501 && c.LineIds == null && c.FromConfirmation),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
