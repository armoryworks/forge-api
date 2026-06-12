using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;

namespace Forge.Integrations;

/// <summary>
/// QB-001 real implementation — posts ONE balanced JournalEntry to the QuickBooks
/// Online API. Reuses the EXACT OAuth/token plumbing of
/// <see cref="QuickBooksAccountingService"/>: the same
/// <see cref="IExternalIdentityResolver"/> resolve ("quickbooks", InstallOnly —
/// QB is one-company-per-install) hands back a fresh access token + RealmId
/// from the same stored install connection, and <see cref="QuickBooksOptions"/>
/// supplies the environment-dependent <c>BaseApiUrl</c>. The ~20-line
/// authenticated-client setup is intentionally repeated here rather than
/// extracted: it is private inside <c>QuickBooksAccountingService</c>, and
/// hoisting it into a shared base/helper purely for this focused write-only
/// service would be a more invasive refactor than the duplication it removes
/// (flagged per the QB-001 build note).
/// </summary>
public class QboJournalPushService(
    IExternalIdentityResolver identityResolver,
    IHttpClientFactory httpClientFactory,
    IOptions<QuickBooksOptions> options,
    ILogger<QboJournalPushService> logger) : IQboJournalPushService
{
    public async Task<string> PushJournalEntryAsync(QboJournalEntryPush entry, CancellationToken ct)
    {
        // Same chokepoint shape as QuickBooksAccountingService.GetAuthenticatedClientAsync.
        var identity = await identityResolver.ResolveAsync(
            "quickbooks", userId: null, TokenResolutionPolicy.InstallOnly, ct)
            ?? throw new InvalidOperationException(
                "QuickBooks is not connected — connect the QuickBooks integration before pushing.");

        var realmId = identity.RealmOrTenantId
            ?? throw new InvalidOperationException(
                "QuickBooks identity returned without a RealmId — this should be impossible " +
                "for a successfully connected QB install.");

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            TxnDate = entry.TxnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PrivateNote = entry.Memo,
            Line = entry.Lines.Select((line, i) => new
            {
                Id = (i + 1).ToString(CultureInfo.InvariantCulture),
                DetailType = "JournalEntryLineDetail",
                Amount = line.Amount,
                Description = line.Description,
                JournalEntryLineDetail = new
                {
                    PostingType = line.IsDebit ? "Debit" : "Credit",
                    AccountRef = new { value = line.QboAccountId },
                },
            }).ToArray(),
        };

        var url = $"{options.Value.BaseApiUrl}/v3/company/{realmId}/journalentry";
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync(url, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[QuickBooks] POST journalentry failed: {StatusCode} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"QuickBooks rejected the journal entry: {response.StatusCode}");
        }

        using var json = JsonDocument.Parse(body);
        var id = json.RootElement.GetProperty("JournalEntry").GetProperty("Id").GetString()
            ?? throw new InvalidOperationException("QuickBooks returned a journal entry without an Id.");

        logger.LogInformation(
            "[QuickBooks] Pushed journal summary {Memo} ({LineCount} lines) — assigned {Id}",
            entry.Memo, entry.Lines.Count, id);
        return id;
    }
}
