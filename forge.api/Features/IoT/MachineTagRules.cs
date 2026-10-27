using FluentValidation;

using Forge.Core.Models;

namespace Forge.Api.Features.IoT;

public static class MachineTagRules
{
    public static void Apply(InlineValidator<CreateMachineTagRequestModel> tag)
    {
        tag.RuleFor(t => t.TagName).NotEmpty().MaximumLength(200);
        tag.RuleFor(t => t.OpcNodeId).NotEmpty().MaximumLength(500);
        tag.RuleFor(t => t.DataType).NotEmpty().MaximumLength(50);
        tag.RuleFor(t => t.Unit).MaximumLength(50);
    }

    public static bool HaveUniqueNodeIds(List<CreateMachineTagRequestModel> tags) =>
        tags.Select(t => t.OpcNodeId.Trim()).Distinct(StringComparer.Ordinal).Count() == tags.Count;
}
