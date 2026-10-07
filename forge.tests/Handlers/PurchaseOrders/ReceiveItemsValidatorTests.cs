using FluentAssertions;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class ReceiveItemsValidatorTests
{
    private static ReceiveItemsCommand Command(string? lot = null, string? notes = null)
        => new(1, [new ReceiveLineModel(LineId: 1, Quantity: 1m, StorageLocationId: null, Notes: notes, LotNumber: lot)]);

    [Fact]
    public void LotOfOneHundredCharacters_AfterTrimming_IsValid()
        => new ReceiveItemsValidator().Validate(Command(lot: $"  {new string('x', 100)}  ")).IsValid.Should().BeTrue();

    [Fact]
    public void LotOverOneHundredCharacters_IsRejected()
        => new ReceiveItemsValidator().Validate(Command(lot: new string('x', 101))).IsValid.Should().BeFalse();

    [Fact]
    public void NotesOverOneThousandCharacters_AreRejected()
        => new ReceiveItemsValidator().Validate(Command(notes: new string('x', 1001))).IsValid.Should().BeFalse();

    [Fact]
    public void LineWithoutLotOrNotes_IsValid()
        => new ReceiveItemsValidator().Validate(Command()).IsValid.Should().BeTrue();
}
