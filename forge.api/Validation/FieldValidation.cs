using FluentValidation;
using FluentValidation.Results;

namespace Forge.Api.Validation;

/// <summary>
/// Builds a <see cref="ValidationException"/> for one field so a handler-side input check
/// returns the 400 validation envelope (with the field highlighted) instead of a 409.
/// </summary>
public static class FieldValidation
{
    /// <summary>
    /// A validation failure on <paramref name="field"/> carrying <paramref name="message"/>.
    /// </summary>
    public static ValidationException Fail(string field, string message) =>
        new(message, [new ValidationFailure(field, message)]);
}
