using FluentAssertions;

using Forge.Api.Features.TrackTypes;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.TrackTypes;

public class CustomFieldDefinitionValidatorTests
{
    private static bool Valid(params CustomFieldDefinitionModel[] fields) =>
        new UpdateCustomFieldDefinitionsValidator().Validate(new UpdateCustomFieldDefinitionsCommand(1, [.. fields])).IsValid;

    [Fact]
    public void Distinct_keys_of_every_type_are_accepted()
    {
        Valid(
            new CustomFieldDefinitionModel("po_ref", "PO ref", "text", true, null),
            new CustomFieldDefinitionModel("qty", "Qty", "number", false, null),
            new CustomFieldDefinitionModel("finish", "Finish", "select", false, ["Matte", "Gloss"]),
            new CustomFieldDefinitionModel("rush", "Rush", "toggle", false, null)).Should().BeTrue();
    }

    [Fact]
    public void A_key_can_be_used_once_regardless_of_case()
    {
        Valid(
            new CustomFieldDefinitionModel("po_ref", "PO ref", "text", false, null),
            new CustomFieldDefinitionModel("PO_REF", "PO", "text", false, null)).Should().BeFalse();
    }
}
