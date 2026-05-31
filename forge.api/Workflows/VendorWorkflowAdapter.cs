using System.Text.Json;

using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern — Vendor-specific creator + field-applier wired into the
/// workflow runtime. Mirrors <see cref="PartWorkflowAdapter"/>'s shape for
/// the Vendor entity. The applier handles the small scalar surface the
/// guided wizard collects (identity, address group, payment terms, notes,
/// off-tier variance, auto-PO mode); collection mutations like VendorPart
/// drafts stay in their dedicated controllers — the Supply Items step
/// component calls those endpoints directly instead of routing nested
/// edits through this applier.
///
/// <para><b>Relationship type</b> — the legacy guided dialog folded a
/// Transactional/Strategic/Subcontractor/Distributor picker + per-type
/// extras into the <c>Notes</c> string. The migration to this adapter
/// drops the picker rather than schema-fy six new columns for a UX nicety;
/// admins who want to flag relationship can type it into Notes manually
/// (see CLAUDE.md "Vendor workflow migration" entry, 2026-05-31).</para>
/// </summary>
public class VendorWorkflowAdapter(AppDbContext db)
    : IWorkflowEntityCreator, IWorkflowFieldApplier, IWorkflowEntityPromoter
{
    public string EntityType => "Vendor";

    /// <summary>
    /// completeRun requires a promoter for every entity type. Vendor has no
    /// Draft → Active lifecycle (it's just IsActive=true from creation), so
    /// promotion is a no-op — the entity is already in its terminal state
    /// the moment the materialization step's CreateDraftAsync returns.
    /// Returning false signals "nothing to promote, nothing changed" which
    /// the handler accepts without further work. (Found the hard way: the
    /// first Mark Complete on a vendor wizard 409'd with "No workflow
    /// entity promoter registered for entity type 'Vendor'" because this
    /// interface wasn't implemented.)
    /// </summary>
    public Task<bool> PromoteAsync(int entityId, string targetStatus, CancellationToken ct)
        => Task.FromResult(false);

    public async Task<int> CreateDraftAsync(JsonElement? initialData, CancellationToken ct)
    {
        // Materialize-on-first-patch — by the time we land here the user has
        // submitted the Identity step. CompanyName is the only hard
        // requirement; everything else is optional and can be filled by
        // later steps or left null.
        var companyName = ReadStringOrDefault(initialData, "companyName")?.Trim();
        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new ValidationException(
                "Vendor requires a company name to materialize.",
                new[] { new ValidationFailure("companyName", "Company name is required.") });
        }

        var vendor = new Vendor
        {
            CompanyName = companyName,
            ContactName = ReadStringOrDefault(initialData, "contactName")?.Trim().NullIfEmpty(),
            Email = ReadStringOrDefault(initialData, "email")?.Trim().NullIfEmpty(),
            Phone = ReadStringOrDefault(initialData, "phone")?.Trim().NullIfEmpty(),
            Address = ReadStringOrDefault(initialData, "address")?.Trim().NullIfEmpty(),
            City = ReadStringOrDefault(initialData, "city")?.Trim().NullIfEmpty(),
            State = ReadStringOrDefault(initialData, "state")?.Trim().NullIfEmpty(),
            ZipCode = ReadStringOrDefault(initialData, "zipCode")?.Trim().NullIfEmpty(),
            Country = ReadStringOrDefault(initialData, "country")?.Trim().NullIfEmpty(),
            PaymentTerms = ReadStringOrDefault(initialData, "paymentTerms")?.Trim().NullIfEmpty(),
            Notes = ReadStringOrDefault(initialData, "notes")?.Trim().NullIfEmpty(),
            OffTierVariancePct = ReadDecimalOrDefault(initialData, "offTierVariancePct"),
            MinOrderAmount = ReadDecimalOrDefault(initialData, "minOrderAmount"),
            AutoPoMode = ReadNullableEnum<AutoPoMode>(initialData, "autoPoMode"),
            IsActive = true,
        };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync(ct);
        return vendor.Id;
    }

    public async Task ApplyAsync(int entityId, JsonElement fields, CancellationToken ct)
    {
        var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == entityId, ct)
            ?? throw new KeyNotFoundException($"Vendor id {entityId} not found.");

        // Identity step
        if (TryReadString(fields, "companyName", out var companyName) && companyName is not null)
            vendor.CompanyName = companyName.Trim();
        if (TryReadString(fields, "contactName", out var contactName))
            vendor.ContactName = contactName?.Trim().NullIfEmpty();
        if (TryReadString(fields, "email", out var email))
            vendor.Email = email?.Trim().NullIfEmpty();
        if (TryReadString(fields, "phone", out var phone))
            vendor.Phone = phone?.Trim().NullIfEmpty();

        // Address step
        if (TryReadString(fields, "address", out var address))
            vendor.Address = address?.Trim().NullIfEmpty();
        if (TryReadString(fields, "city", out var city))
            vendor.City = city?.Trim().NullIfEmpty();
        if (TryReadString(fields, "state", out var state))
            vendor.State = state?.Trim().NullIfEmpty();
        if (TryReadString(fields, "zipCode", out var zip))
            vendor.ZipCode = zip?.Trim().NullIfEmpty();
        if (TryReadString(fields, "country", out var country))
            vendor.Country = country?.Trim().NullIfEmpty();

        // Terms step
        if (TryReadString(fields, "paymentTerms", out var paymentTerms))
            vendor.PaymentTerms = paymentTerms?.Trim().NullIfEmpty();
        if (TryReadString(fields, "notes", out var notes))
            vendor.Notes = notes?.Trim().NullIfEmpty();
        if (TryReadDecimal(fields, "offTierVariancePct", out var pct))
            vendor.OffTierVariancePct = pct;
        if (TryReadDecimal(fields, "minOrderAmount", out var minOrder))
            vendor.MinOrderAmount = minOrder;
        if (TryReadNullableEnum<AutoPoMode>(fields, "autoPoMode", out var autoPo))
            vendor.AutoPoMode = autoPo;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Workflow abandonment cleanup. Vendor has no Draft status (just
    /// IsActive bool), so the workflow-abandon equivalent is: soft-delete
    /// the row IFF no transactions have been issued against it yet — once
    /// a PO exists the vendor is real production data and must not be
    /// silently removed.
    /// </summary>
    public async Task<bool> SoftDeleteIfDraftAsync(int entityId, CancellationToken ct)
    {
        var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == entityId, ct);
        if (vendor is null) return false;
        var hasPos = await db.PurchaseOrders.AnyAsync(po => po.VendorId == entityId, ct);
        if (hasPos) return false;
        vendor.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ─── JSON helpers (verbatim from PartWorkflowAdapter — keep in sync) ───

    private static string? ReadStringOrDefault(JsonElement? root, string name)
    {
        if (root is null || root.Value.ValueKind != JsonValueKind.Object) return null;
        if (!root.Value.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static decimal? ReadDecimalOrDefault(JsonElement? root, string name)
    {
        if (root is null || root.Value.ValueKind != JsonValueKind.Object) return null;
        if (!root.Value.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var d) ? d : null;
    }

    private static T? ReadNullableEnum<T>(JsonElement? root, string name) where T : struct, Enum
    {
        if (root is null || root.Value.ValueKind != JsonValueKind.Object) return null;
        if (!root.Value.TryGetProperty(name, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Null) return null;
        if (prop.ValueKind == JsonValueKind.String && Enum.TryParse<T>(prop.GetString(), true, out var parsed))
            return parsed;
        return null;
    }

    private static bool TryReadString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Null) { value = null; return true; }
        if (prop.ValueKind == JsonValueKind.String) { value = prop.GetString(); return true; }
        return false;
    }

    private static bool TryReadDecimal(JsonElement root, string name, out decimal? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Null) { value = null; return true; }
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var d)) { value = d; return true; }
        return false;
    }

    private static bool TryReadNullableEnum<T>(JsonElement root, string name, out T? value) where T : struct, Enum
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Null) { value = null; return true; }
        if (prop.ValueKind == JsonValueKind.String && Enum.TryParse<T>(prop.GetString(), true, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }
}

internal static class StringNullIfEmptyExtension
{
    public static string? NullIfEmpty(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
