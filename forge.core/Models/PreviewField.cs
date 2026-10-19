namespace Forge.Core.Models;

/// <summary>
/// A single label/value row in an <see cref="EntityPreviewModel"/>. <see cref="Value"/> is display-ready
/// English; <see cref="Key"/> is a stable camelCase id for translating <see cref="Label"/>, and
/// <see cref="Kind"/> ("enum" or "money") tells the client it may format <see cref="RawValue"/>
/// itself (the enum name, or an invariant decimal in <see cref="Currency"/>, null meaning the base currency).
/// </summary>
public record PreviewField(
    string Label,
    string Value,
    string? Key = null,
    string? Kind = null,
    string? RawValue = null,
    string? Currency = null);
