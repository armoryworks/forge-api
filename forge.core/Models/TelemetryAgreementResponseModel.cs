namespace Forge.Core.Models;

/// <summary>
/// The agreement an operator is shown before remote health monitoring is switched on,
/// together with the exact payload their install would send. Served from the install
/// itself rather than fetched from Armory Works: consent to a vendor reading your
/// system shouldn't depend on text that vendor can silently change afterwards.
/// </summary>
/// <param name="Version">Bumped whenever the terms or the payload change materially; a bump re-asks.</param>
/// <param name="SamplePayload">Verbatim example of what leaves the building, so "health only" is checkable rather than a promise.</param>
public sealed record TelemetryAgreementResponseModel(
    string Version,
    string Title,
    IReadOnlyList<string> Shared,
    IReadOnlyList<string> NotShared,
    IReadOnlyList<string> Terms,
    string SamplePayload);
