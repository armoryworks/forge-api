namespace Forge.Core.Models;

/// <summary>regulatory-watchtower: a single item fetched from a regulatory source feed.</summary>
public record RegulatoryFeedItem(string Title, string? Url, string? Details);
