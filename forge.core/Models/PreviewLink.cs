namespace Forge.Core.Models;

/// <summary>A jump target to a related record in an <see cref="EntityPreviewModel"/>.
/// <paramref name="Type"/> matches the frontend entity-link type slug (e.g. "customer").</summary>
public record PreviewLink(string Type, int Id, string Label);
