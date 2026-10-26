using FluentAssertions;
using Moq;

using Forge.Api.Features.Barcodes;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Barcodes;

public class AssignPartGtinTests
{
    private readonly AssignPartGtinValidator _validator = new();

    [Theory]
    [InlineData("4006381333931")]
    [InlineData(" 4006381333931 ")]
    [InlineData(null)]
    [InlineData("")]
    public void ValidOrBlankGtin_PassesValidation(string? gtin)
    {
        _validator.Validate(new AssignPartGtinCommand(1, gtin)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("4006381333932")]
    [InlineData("12345")]
    [InlineData("40063813339AB")]
    public void BadGtin_IsAValidationFailureOnManualGtin(string gtin)
    {
        var result = _validator.Validate(new AssignPartGtinCommand(1, gtin));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(AssignPartGtinCommand.ManualGtin));
    }

    [Fact]
    public async Task GtinAlreadyOnAnotherPart_IsAConflict()
    {
        using var db = TestDbContextFactory.Create();
        db.Parts.AddRange(
            new Part { PartNumber = "GTIN-A", Name = "A", Gtin = "4006381333931" },
            new Part { PartNumber = "GTIN-B", Name = "B" });
        await db.SaveChangesAsync();
        var target = db.Parts.Single(p => p.PartNumber == "GTIN-B");
        var handler = new AssignPartGtinHandler(db, new SystemSettingRepository(db), Mock.Of<IBarcodeService>());

        var act = () => handler.Handle(new AssignPartGtinCommand(target.Id, "4006381333931"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already assigned to another part*");
    }
}
