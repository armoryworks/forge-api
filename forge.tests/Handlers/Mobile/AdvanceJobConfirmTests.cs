using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;
using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Mobile;

public class AdvanceJobConfirmTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly AdvanceJobHandler _handler;

    public AdvanceJobConfirmTests()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.Zero));
        var collapse = new ScanCollapseService(
            new MemoryCache(new MemoryCacheOptions()), clock.Object, NullLogger<ScanCollapseService>.Instance);
        _handler = new AdvanceJobHandler(_mediator.Object, collapse);
    }

    private void NextStageIs(string name, bool irreversible, AccountingDocumentType? document)
    {
        var status = new JobStatusResponseModel(
            7, "JOB-0007", "Bracket", null, 8, "Shipped", "#000000", null, false,
            9, name, 7, "QC", 1, new List<ActivityResponseModel>())
        {
            NextStageIsIrreversible = irreversible,
            NextStageAccountingDocument = document,
        };
        _mediator.Setup(m => m.Send(It.IsAny<GetJobStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
    }

    private void VerifyMoved(Times times) =>
        _mediator.Verify(m => m.Send(It.IsAny<MoveJobStageCommand>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Advancing_into_invoiced_without_confirming_is_refused_with_a_plain_message()
    {
        NextStageIs("Invoiced/Sent", true, AccountingDocumentType.Invoice);

        var act = () => _handler.Handle(new AdvanceJobCommand(7, "device-1", null), CancellationToken.None);

        await act.Should().ThrowAsync<ConfirmationRequiredException>()
            .WithMessage("Moving JOB-0007 to Invoiced/Sent can't be undone and queues an invoice for your accounting system. Confirm to continue.");
        VerifyMoved(Times.Never());
    }

    [Fact]
    public async Task Advancing_into_invoiced_with_confirmation_moves_the_job()
    {
        NextStageIs("Invoiced/Sent", true, AccountingDocumentType.Invoice);

        var result = await _handler.Handle(
            new AdvanceJobCommand(7, "device-1", null, Confirmed: true), CancellationToken.None);

        result.Collapsed.Should().BeFalse();
        _mediator.Verify(m => m.Send(
            It.Is<MoveJobStageCommand>(c => c.JobId == 7 && c.StageId == 9), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_status_that_only_queues_an_accounting_document_needs_confirming()
    {
        NextStageIs("Order Confirmed", false, AccountingDocumentType.SalesOrder);

        var act = () => _handler.Handle(new AdvanceJobCommand(7, "device-1", null), CancellationToken.None);

        await act.Should().ThrowAsync<ConfirmationRequiredException>()
            .WithMessage("Moving JOB-0007 to Order Confirmed queues a sales order for your accounting system. Confirm to continue.");
        VerifyMoved(Times.Never());
    }

    [Fact]
    public async Task An_irreversible_status_without_a_document_needs_confirming()
    {
        NextStageIs("Closed", true, null);

        var act = () => _handler.Handle(new AdvanceJobCommand(7, "device-1", null), CancellationToken.None);

        await act.Should().ThrowAsync<ConfirmationRequiredException>()
            .WithMessage("Moving JOB-0007 to Closed can't be undone. Confirm to continue.");
    }

    [Fact]
    public async Task An_ordinary_status_moves_without_confirming()
    {
        NextStageIs("Machining", false, null);

        var result = await _handler.Handle(new AdvanceJobCommand(7, "device-1", null), CancellationToken.None);

        result.Collapsed.Should().BeFalse();
        VerifyMoved(Times.Once());
    }

    [Fact]
    public async Task A_confirmed_resend_of_the_same_scan_is_not_collapsed_as_a_duplicate()
    {
        NextStageIs("Invoiced/Sent", true, AccountingDocumentType.Invoice);
        var refused = () => _handler.Handle(new AdvanceJobCommand(7, "device-1", "JOB-0007"), CancellationToken.None);
        await refused.Should().ThrowAsync<ConfirmationRequiredException>();

        var result = await _handler.Handle(
            new AdvanceJobCommand(7, "device-1", "JOB-0007", Confirmed: true), CancellationToken.None);

        result.Collapsed.Should().BeFalse();
        VerifyMoved(Times.Once());
    }

    [Fact]
    public async Task A_double_tapped_confirmation_moves_the_job_once()
    {
        NextStageIs("Invoiced/Sent", true, AccountingDocumentType.Invoice);

        await _handler.Handle(new AdvanceJobCommand(7, "device-1", "JOB-0007", Confirmed: true), CancellationToken.None);
        var second = await _handler.Handle(
            new AdvanceJobCommand(7, "device-1", "JOB-0007", Confirmed: true), CancellationToken.None);

        second.Collapsed.Should().BeTrue();
        VerifyMoved(Times.Once());
    }

    [Fact]
    public async Task A_confirmation_for_the_stage_that_is_still_next_moves_the_job()
    {
        NextStageIs("Invoiced/Sent", true, AccountingDocumentType.Invoice);

        await _handler.Handle(
            new AdvanceJobCommand(7, "device-1", null, Confirmed: true, ConfirmedStageId: 9), CancellationToken.None);

        VerifyMoved(Times.Once());
    }

    [Fact]
    public async Task A_confirmation_for_a_stage_that_is_no_longer_next_is_asked_again()
    {
        NextStageIs("Payment Received", true, AccountingDocumentType.Payment);

        var act = () => _handler.Handle(
            new AdvanceJobCommand(7, "device-1", null, Confirmed: true, ConfirmedStageId: 8), CancellationToken.None);

        await act.Should().ThrowAsync<ConfirmationRequiredException>()
            .WithMessage("Moving JOB-0007 to Payment Received can't be undone and queues a payment for your accounting system. Confirm to continue.");
        VerifyMoved(Times.Never());
    }
}
