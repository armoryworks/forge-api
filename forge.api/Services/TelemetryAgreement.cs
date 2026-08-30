using System.Text.Json;

using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// The agreement text an operator sees before remote health monitoring is switched
/// on, and the sample of exactly what their install would transmit.
///
/// It lives in the install's own binary rather than being fetched from Armory Works.
/// Consent to a vendor reading your system is worth very little if that vendor can
/// change the terms afterwards without the customer seeing a thing — shipping the
/// text with the build means the agreement someone accepted is the agreement that was
/// on screen, and changing it requires an upgrade they can decline.
///
/// <see cref="Version"/> gates that: a decision recorded against an older version
/// stops counting, and the operator is asked again rather than carried along.
/// </summary>
public static class TelemetryAgreement
{
    /// <summary>Bump whenever the terms or the payload change materially.</summary>
    public const string Version = "1";

    public static TelemetryAgreementResponseModel Current { get; } = new(
        Version,
        "Share health status with Armory Works",
        Shared:
        [
            "Whether this system is running, checked every few minutes.",
            "Whether each internal component is healthy — database, background jobs, file storage, live updates.",
            "The Forge version this system is running.",
            "How long it has been up since its last restart.",
            "The company name and contact email shown below, so Armory Works knows whose system this is.",
        ],
        NotShared:
        [
            "No business data of any kind — no jobs, parts, customers, orders, invoices, prices or quantities.",
            "No files, documents or attachments.",
            "No user accounts, names, passwords or personal information.",
            "No database contents. Armory Works cannot read, query or export your data through this.",
            "No remote access. This sends information out; it opens no way in.",
        ],
        Terms:
        [
            "This is entirely optional. Forge works exactly the same with it switched off.",
            "It can be switched off at any time, from this screen, without contacting anyone.",
            "Armory Works must also accept this system before anything is sent. Until they do, nothing leaves.",
            "Armory Works uses it for one purpose: to notice this system has a problem, ideally before you do.",
            "If the information shared here ever changes, you will be asked to agree again.",
        ],
        SamplePayload: BuildSamplePayload());

    // A real, formatted example of the whole payload. "Health only" is a claim the
    // operator can check for themselves rather than take on trust.
    private static string BuildSamplePayload() => JsonSerializer.Serialize(new
    {
        status = "Healthy",
        checks = new[]
        {
            new { name = "postgresql", status = "Healthy" },
            new { name = "hangfire", status = "Healthy" },
            new { name = "minio", status = "Healthy" },
            new { name = "signalr", status = "Healthy" },
        },
        version = "1.0.0",
        uptimeSeconds = 86_400,
    }, new JsonSerializerOptions { WriteIndented = true });
}
