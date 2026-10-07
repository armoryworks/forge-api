using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.Jobs;

internal sealed record BomExplosionLine(
    Part ChildPart,
    decimal Quantity,
    BOMSourceType SourceType,
    int? LeadTimeDays,
    string? LineUom);
