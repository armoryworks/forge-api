using FluentValidation;
using FluentValidation.Results;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Validation;

/// <summary>
/// Shared guard for every path that accepts a typed part number: direct create, the part
/// workflow, and renames. Each failure is a <see cref="ValidationException"/> on the
/// <c>partNumber</c> field so the middleware returns a 400 the UI can pin to the input,
/// instead of a silently replaced number or a unique-index 500.
/// </summary>
public static class PartNumberCheck
{
    /// <summary>Field name the failures are reported against.</summary>
    public const string FieldName = "partNumber";

    /// <summary>System setting that gates typed part numbers.</summary>
    public const string ManualNumbersKey = "parts.allow_manual_numbers";

    /// <summary>Whether an admin has turned on manual part numbers.</summary>
    public static async Task<bool> ManualNumbersAllowedAsync(ISystemSettingRepository settings, CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(ManualNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }

    /// <summary>
    /// Throws when <paramref name="partNumber"/> is held by another part, live or soft-deleted.
    /// </summary>
    public static async Task EnsureAvailableAsync(
        IPartRepository repo, string partNumber, int? excludeId, CancellationToken ct)
    {
        var status = await repo.PartNumberStatusAsync(partNumber, excludeId, ct);
        switch (status)
        {
            case PartNumberStatus.Active:
                throw Fail($"Part number '{partNumber}' is already in use.");
            case PartNumberStatus.Deleted:
                throw Fail($"Part number '{partNumber}' belongs to a deleted part. Restore that part or choose another number.");
        }
    }

    /// <summary>A number was typed on a new part while manual part numbers are off.</summary>
    public static ValidationException ManualNumbersOffOnCreate() =>
        Fail("Manual part numbers are turned off. Leave Part Number blank to auto-number, or ask an admin to turn them on in Admin > Settings > Numbering.");

    /// <summary>An existing part was renumbered while manual part numbers are off.</summary>
    public static ValidationException ManualNumbersOffOnRename() =>
        Fail("Manual part numbers are turned off, so the part number can't be changed. Ask an admin to turn them on in Admin > Settings > Numbering.");

    private static ValidationException Fail(string message) =>
        new(message, [new ValidationFailure(FieldName, message)]);
}
