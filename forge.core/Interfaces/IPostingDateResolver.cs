namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-3 — late-posting fallback (§12). Resolves the date a posting should actually land on: the desired
/// date when its period is Open, otherwise a dated catch-up into the next Open period. Operational handlers
/// that may receive a document dated into a closed period use this so the entry posts forward instead of
/// failing on a closed period.
/// </summary>
public interface IPostingDateResolver
{
    /// <summary>
    /// Returns <paramref name="desiredDate"/> when its fiscal period is Open; otherwise the start date of the
    /// next Open period on/after it (the catch-up date). Throws if no open period exists on/after the date.
    /// </summary>
    Task<DateOnly> ResolveOpenPostingDateAsync(int bookId, DateOnly desiredDate, CancellationToken ct = default);
}
