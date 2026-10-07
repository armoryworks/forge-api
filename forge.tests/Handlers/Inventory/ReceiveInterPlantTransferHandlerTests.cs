using FluentAssertions;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class ReceiveInterPlantTransferHandlerTests
{
    [Theory]
    [InlineData(InterPlantTransferStatus.Draft, "This transfer can be received once it has shipped.")]
    [InlineData(InterPlantTransferStatus.Approved, "This transfer can be received once it has shipped.")]
    [InlineData(InterPlantTransferStatus.Received, "This transfer has already been received.")]
    [InlineData(InterPlantTransferStatus.Cancelled, "This transfer was cancelled.")]
    public async Task Handle_NotReceivable_ThrowsPlainMessageForStatus(InterPlantTransferStatus status, string expectedMessage)
    {
        using var db = TestDbContextFactory.Create();
        var transfer = new InterPlantTransfer
        {
            TransferNumber = "IPT-0001",
            FromPlantId = 1,
            ToPlantId = 2,
            Status = status,
        };
        db.InterPlantTransfers.Add(transfer);
        await db.SaveChangesAsync();
        var handler = new ReceiveInterPlantTransferHandler(db, new SystemClock());

        var act = () => handler.Handle(new ReceiveInterPlantTransferCommand(transfer.Id, []), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(expectedMessage);
        transfer.Status.Should().Be(status);
    }

    [Theory]
    [InlineData(InterPlantTransferStatus.Shipped)]
    [InlineData(InterPlantTransferStatus.InTransit)]
    public async Task Handle_ShippedOrInTransit_MarksReceived(InterPlantTransferStatus status)
    {
        using var db = TestDbContextFactory.Create();
        var transfer = new InterPlantTransfer
        {
            TransferNumber = "IPT-0002",
            FromPlantId = 1,
            ToPlantId = 2,
            Status = status,
        };
        db.InterPlantTransfers.Add(transfer);
        await db.SaveChangesAsync();
        var handler = new ReceiveInterPlantTransferHandler(db, new SystemClock());

        await handler.Handle(new ReceiveInterPlantTransferCommand(transfer.Id, []), CancellationToken.None);

        transfer.Status.Should().Be(InterPlantTransferStatus.Received);
        transfer.ReceivedAt.Should().NotBeNull();
    }
}
