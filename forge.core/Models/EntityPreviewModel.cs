namespace Forge.Core.Models;

/// <summary>
/// Lightweight, non-sensitive summary of a linked record, shown in the
/// entity-link hover preview popover. Carries only list-row-visible basics
/// (identity, status, a date, a headline figure) plus jump links to related
/// records — never costs, margins, tax ids, credit, or bank details.
/// </summary>
public record EntityPreviewModel(
    string Type,
    int Id,
    string Title,
    string? Subtitle,
    IReadOnlyList<PreviewField> Fields,
    IReadOnlyList<PreviewLink> Links);
