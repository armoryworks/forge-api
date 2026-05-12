using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

/// <summary>
/// Exports seeded demo data from the AppDbContext to per-entity JSON files for
/// consumption by the Angular demo bundle.
/// Credential-bearing entities are skipped entirely; credential-bearing properties
/// on exported entities are nulled out before serialization.
/// </summary>
public static class DemoDataExporter
{
    // Only export entities that live in the business-domain namespace. This is an
    // allowlist — anything outside this namespace (Identity join tables, OpenIddict,
    // DataProtection, etc.) is skipped by default.
    private const string AllowedNamespace = "Forge.Core.Entities";

    // Entity CLR type names outside AllowedNamespace that we explicitly WANT to
    // export. ApplicationUser lives in Forge.Data.Context but the demo needs
    // users so that jobs/activity/chat have real names to render.
    private static readonly HashSet<string> AdditionalAllowedTypes = new(StringComparer.Ordinal)
    {
        "ApplicationUser",
    };

    // Entity CLR type names that live inside AllowedNamespace but still must NOT be
    // exported (token stores, audit logs, embeddings, high-volume internal logs).
    private static readonly HashSet<string> SkippedEntityNames = new(StringComparer.Ordinal)
    {
        // Credential / auth surfaces
        "UserMfaDevice",
        "MfaRecoveryCode",
        "UserScanIdentifier",
        "UserIntegration",
        "KioskTerminal",
        "BiApiKey",
        "OidcClientMetadata",
        "OidcRegistrationTicket",
        "OidcCustomScope",
        "OidcAuditEvent",

        // Internal queues / embeddings / high-volume logs
        "SyncQueueEntry",
        "IntegrationOutboxEntry",
        "DocumentEmbedding",
        "AuditLogEntry",
        "WebhookDelivery",
        "DomainEventFailure",
        "ScanActionLog",
        "TrainingScanLog",
        "MachineDataPoint",
        "SpcMeasurement",
    };

    // Property names whose values must be nulled out on ANY exported entity.
    // Matches case-insensitively against the property's suffix or whole name.
    private static readonly string[] ScrubSuffixes = new[]
    {
        "hash", "secret", "token", "apikey", "privatekey", "encryptedkey",
    };

    private static readonly HashSet<string> ScrubExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
        "NormalizedEmail", "NormalizedUserName",
        "Pin", "PinCode", "TotpSecret", "RecoveryCode",
        // SSO / scan identifiers on ApplicationUser — never ship these
        "EmployeeBarcode", "GoogleId", "MicrosoftId", "OidcSubjectId", "OidcProvider",
        "SetupToken", "AccountingEmployeeId",
    };

    public static async Task ExportAsync(AppDbContext db, string outputDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            Converters = { new JsonStringEnumConverter() },
            MaxDepth = 32,
            // Skip computed get-only props (e.g. Invoice.Subtotal => Lines.Sum(...)).
            // These throw when nav collections are nulled during scrubbing, and the
            // demo client can recompute them from raw line items anyway.
            IgnoreReadOnlyProperties = true,
        };

        var manifest = new List<ManifestEntry>();
        var method = typeof(DemoDataExporter).GetMethod(
            nameof(ExportOneAsync),
            BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;

            if (clr.Namespace != AllowedNamespace && !AdditionalAllowedTypes.Contains(clr.Name))
            {
                Log.Debug("[EXPORT] Skip {Type} (namespace {Namespace})", clr.Name, clr.Namespace);
                continue;
            }

            if (SkippedEntityNames.Contains(clr.Name))
            {
                Log.Debug("[EXPORT] Skip {Type} (credential/internal)", clr.Name);
                continue;
            }

            // Skip owned types and keyless entities — db.Set<T>() throws for these.
            if (entityType.IsOwned() || entityType.FindPrimaryKey() == null)
            {
                Log.Debug("[EXPORT] Skip {Type} (owned or keyless)", clr.Name);
                continue;
            }

            var generic = method.MakeGenericMethod(clr);
            int count;
            try
            {
                var task = (Task<int>)generic.Invoke(null, new object[] { db, outputDir, jsonOpts, ct })!;
                count = await task;
            }
            catch (Exception ex)
            {
                // A single bad entity type shouldn't kill the whole export. Log and continue
                // so the remaining entities still get written.
                Log.Error(ex, "[EXPORT] FAILED {Type} — continuing with remaining entities", clr.Name);
                continue;
            }

            var fileName = ToKebabCase(clr.Name) + ".json";
            manifest.Add(new ManifestEntry(clr.Name, fileName, count));
        }

        // Inject subtle demo tells — data fingerprints that survive screenshot
        // cropping/photoshopping and let us identify "bug reports" that were
        // actually taken against the demo site. Only runs on demo exports; has
        // no effect on the production seed or dev DB.
        await InjectDemoTellsAsync(outputDir, jsonOpts, ct);

        // Write manifest
        var manifestPath = Path.Combine(outputDir, "_manifest.json");
        await using (var fs = File.Create(manifestPath))
        {
            await JsonSerializer.SerializeAsync(fs, new
            {
                generatedAt = DateTimeOffset.UtcNow,
                entities = manifest.OrderBy(m => m.Entity).ToList(),
            }, jsonOpts, ct);
        }

        Log.Information("[EXPORT] Wrote {Count} entity files + manifest to {Dir}",
            manifest.Count, outputDir);
    }

    /// <summary>
    /// Append demo-only fingerprint rows to specific exported entity files. The
    /// entries look plausible in the UI but are unusual enough that seeing one
    /// in a "bug report" screenshot reveals the screenshot came from the demo
    /// site, not a production install.
    /// </summary>
    private static async Task InjectDemoTellsAsync(string outputDir, JsonSerializerOptions jsonOpts, CancellationToken ct)
    {
        var customerPath = Path.Combine(outputDir, "customer.json");
        if (!File.Exists(customerPath)) return;

        try
        {
            var existingJson = await File.ReadAllTextAsync(customerPath, ct);
            var rows = JsonSerializer.Deserialize<List<JsonElement>>(existingJson) ?? new();

            // Compute a safe synthetic id that won't collide with real seeded rows.
            var maxId = 0;
            foreach (var row in rows)
            {
                if (row.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var idVal) && idVal > maxId)
                    maxId = idVal;
            }
            var tellId = maxId + 100_000;

            var tell = new Dictionary<string, object?>
            {
                ["id"] = tellId,
                ["name"] = "Acme Demo Industries",
                ["companyName"] = "Acme Demo Industries",
                ["email"] = "contact@acme-demo.example",
                ["phone"] = "(555) 555-0100",
                ["isActive"] = true,
                ["creditLimit"] = 0m,
                ["isOnCreditHold"] = false,
                ["createdAt"] = DateTimeOffset.UtcNow,
                ["updatedAt"] = DateTimeOffset.UtcNow,
            };

            // Append as a raw JsonElement so it rejoins the same shape as siblings.
            var tellJson = JsonSerializer.SerializeToElement(tell, jsonOpts);
            rows.Add(tellJson);

            await File.WriteAllTextAsync(customerPath, JsonSerializer.Serialize(rows, jsonOpts), ct);
            Log.Information("[EXPORT] Injected demo tell into customer.json (id={TellId})", tellId);
        }
        catch (Exception ex)
        {
            // Non-fatal — the export is still useful without the tell.
            Log.Warning(ex, "[EXPORT] Could not inject demo tell into customer.json");
        }
    }

    private static async Task<int> ExportOneAsync<T>(
        AppDbContext db, string outputDir, JsonSerializerOptions jsonOpts, CancellationToken ct)
        where T : class
    {
        var list = await db.Set<T>().AsNoTracking().ToListAsync(ct);

        var scrubProps = GetScrubProperties(typeof(T));
        var navProps = GetNavigationProperties(db, typeof(T));

        foreach (var item in list)
        {
            foreach (var prop in scrubProps)
                ClearProperty(item, prop);

            // Defensive — AsNoTracking without Include() already leaves nav props null,
            // but set them again in case any are lazy-populated.
            foreach (var prop in navProps)
                ClearProperty(item, prop);
        }

        var fileName = ToKebabCase(typeof(T).Name) + ".json";
        var filePath = Path.Combine(outputDir, fileName);
        await using var fs = File.Create(filePath);
        await JsonSerializer.SerializeAsync(fs, list, jsonOpts, ct);

        Log.Information("[EXPORT] {Entity}: {Count} rows → {File}", typeof(T).Name, list.Count, fileName);
        return list.Count;
    }

    private static PropertyInfo[] GetScrubProperties(Type clr)
    {
        return clr.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && IsScrubTarget(p.Name))
            .ToArray();
    }

    private static bool IsScrubTarget(string propertyName)
    {
        if (ScrubExactNames.Contains(propertyName)) return true;
        var lower = propertyName.ToLowerInvariant();
        return ScrubSuffixes.Any(s => lower.EndsWith(s));
    }

    private static PropertyInfo[] GetNavigationProperties(AppDbContext db, Type clr)
    {
        var entityType = db.Model.FindEntityType(clr);
        if (entityType == null) return Array.Empty<PropertyInfo>();

        var navNames = entityType.GetNavigations().Select(n => n.Name)
            .Concat(entityType.GetSkipNavigations().Select(n => n.Name))
            .ToHashSet(StringComparer.Ordinal);

        return clr.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && navNames.Contains(p.Name))
            .ToArray();
    }

    private static void ClearProperty(object target, PropertyInfo prop)
    {
        try
        {
            if (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null)
                prop.SetValue(target, Activator.CreateInstance(prop.PropertyType));
            else
                prop.SetValue(target, null);
        }
        catch
        {
            // Some computed/readonly-backed properties will refuse. Safe to skip.
        }
    }

    private static string ToKebabCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal)) return pascal;
        var sb = new StringBuilder(pascal.Length + 8);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(pascal[i - 1]))
                sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private sealed record ManifestEntry(string Entity, string File, int Count);
}
