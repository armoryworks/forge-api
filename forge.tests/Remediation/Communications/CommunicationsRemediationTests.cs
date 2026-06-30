using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Communications;

/// <summary>
/// Region · Communications RED test. Finding G-39-EMAIL-1 (issue #14): the read
/// path <c>GET /api/v1/communications/connections</c> returned sync-config /
/// connection rows for a kind whose capability was OFF. The read query carried no
/// capability attribute (so <c>CapabilityGateBehavior</c> no-ops) while the mutating
/// endpoints gate per-kind via <c>EnsureKindEnabled</c> — leaving the list endpoint
/// as a capability read-leak.
///
/// Now GREEN — <c>GetConnections</c> filters its result per-kind via the same
/// capability snapshot the controller already uses, so a config for a disabled kind
/// is never surfaced. Per-kind filtering (not a blanket [RequiresCapability]) is the
/// fix: an install with email enabled but voice disabled must still list its email
/// rows, which a whole-endpoint 403 would break.
///
/// Both EXT caps are default-OFF; the test enables email only and asserts the voice
/// row stays hidden, then disables email and asserts the list goes empty. State is
/// restored to default-OFF at the end so sibling tests in the shared collection are
/// unaffected.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class CommunicationsRemediationTests
{
    // TestAuthHandler authenticates every request bearing X-Test-User as user id 1.
    private const int TestUserId = 1;

    private readonly CapabilityTestWebApplicationFactory _factory;
    public CommunicationsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", TestUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task SetCapabilityAsync(HttpClient admin, string code, bool enabled)
        => (await admin.PutAsync($"/api/v1/capabilities/{code}/enabled", JsonContent.Create(new { enabled })))
            .EnsureSuccessStatusCode();

    [Fact] // G-39-EMAIL-1 GREEN — the list never returns a config whose kind's capability is OFF.
    public async Task GetConnections_HidesConfigsForDisabledKind()
    {
        var admin = AuthClient();

        int emailId, voiceId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var email = new CommunicationSyncConfig
            {
                UserId = TestUserId,
                Kind = CommunicationKind.Email,
                ProviderId = "imap",
                DisplayLabel = "G39-EMAIL-ROW",
                ExternalAccountId = "g39-email@work.com",
            };
            var voice = new CommunicationSyncConfig
            {
                UserId = TestUserId,
                Kind = CommunicationKind.Voice,
                ProviderId = "twilio-voip",
                DisplayLabel = "G39-VOICE-ROW",
                ExternalAccountId = "+15555550139",
            };
            db.CommunicationSyncConfigs.AddRange(email, voice);
            await db.SaveChangesAsync();
            emailId = email.Id;
            voiceId = voice.Id;
        }

        try
        {
            // Enable email only — voice stays OFF (default).
            await SetCapabilityAsync(admin, "CAP-EXT-EMAIL-SYNC", enabled: true);

            var withEmail = await (await admin.GetAsync("/api/v1/communications/connections")).Content.ReadAsStringAsync();
            withEmail.Should().Contain("G39-EMAIL-ROW", "the email kind's capability is enabled");
            withEmail.Should().NotContain("G39-VOICE-ROW",
                "the voice kind's capability (CAP-EXT-VOIP-SYNC) is disabled — its config must not leak (G-39-EMAIL-1)");

            // Disable email too — now no kind is enabled, so nothing may surface.
            await SetCapabilityAsync(admin, "CAP-EXT-EMAIL-SYNC", enabled: false);

            var withNeither = await (await admin.GetAsync("/api/v1/communications/connections")).Content.ReadAsStringAsync();
            withNeither.Should().NotContain("G39-EMAIL-ROW",
                "with CAP-EXT-EMAIL-SYNC disabled the email config must not leak either");
            withNeither.Should().NotContain("G39-VOICE-ROW",
                "with both EXT caps disabled the connections list must be empty of these rows");
        }
        finally
        {
            // Restore default-OFF state and soft-delete the seeded rows so the
            // shared collection fixture is left clean for sibling tests.
            await SetCapabilityAsync(admin, "CAP-EXT-EMAIL-SYNC", enabled: false);
            using var scope = NewScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var id in new[] { emailId, voiceId })
            {
                var row = await db.CommunicationSyncConfigs.FindAsync(id);
                if (row is not null)
                {
                    row.DeletedAt = DateTimeOffset.UtcNow;
                }
            }
            await db.SaveChangesAsync();
        }
    }
}
