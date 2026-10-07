using FluentAssertions;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class ReceiveInterPlantTransferHandlerTests
{
    [Fact]
    public async Task Handle_NotYetShipped_ThrowsPlainMessage()
    {
        using var db = TestDbContextFactory.Create();
        var transfer = new InterPlantTransfer
        {
            TransferNumber = "IPT-0001",
            FromPlantId = 1,
            ToPlantId = 2,
            Status = InterPlantTransferStatus.Draft,
        };
        db.InterPlantTransfers.Add(transfer);
        await db.SaveChangesAsync();
        var handler = new ReceiveInterPlantTransferHandler(db, new SystemClock());

        var act = () => handler.Handle(new ReceiveInterPlantTransferCommand(transfer.Id, []), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This transfer can be received once it has shipped.");
        transfer.Status.Should().Be(InterPlantTransferStatus.Draft);
    }
}
