namespace Forge.Api.Features.Documents;

/// <summary>An entity a document set is linked to (polymorphic), e.g. ("Shipment", 5).</summary>
public record DocumentLinkTarget(string EntityType, int EntityId);
