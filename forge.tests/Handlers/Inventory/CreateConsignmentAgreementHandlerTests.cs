using FluentAssertions;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class CreateConsignmentAgreementHandlerTests
{
    [Theory]
    [InlineData(ConsignmentDirection.Inbound, "Choose the vendor for this consignment.")]
    [InlineData(ConsignmentDirection.Outbound, "Choose the customer for this consignment.")]
    public async Task Handle_MissingCounterparty_ThrowsPlainMessage(ConsignmentDirection direction, string expected)
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-200", Name = "Spacer" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var handler = new CreateConsignmentAgreementHandler(db);

        var act = () => handler.Handle(
            new CreateConsignmentAgreementCommand(new CreateConsignmentAgreementRequestModel
            {
                Direction = direction,
                PartId = part.Id,
                AgreedUnitPrice = 1m,
                EffectiveFrom = new DateOnly(2026, 1, 1),
            }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(expected);
        db.ConsignmentAgreements.Should().BeEmpty();
    }
}
