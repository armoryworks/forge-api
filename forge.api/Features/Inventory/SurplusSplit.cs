using Forge.Core.Entities;

namespace Forge.Api.Features.Inventory;

public sealed record SurplusSplit(decimal DemandQuantity, decimal SurplusQuantity, StorageLocation? SurplusLocation);
