namespace Forge.Core.Models.Communications;

/// <summary>What this communication was filed against.</summary>
public record CommunicationLinkResponseModel(int Id, string EntityType, int EntityId);
