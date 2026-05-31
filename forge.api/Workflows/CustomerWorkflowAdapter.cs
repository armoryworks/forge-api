using System.Text.Json;

using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern — Customer-specific creator + field-applier wired into
/// the workflow runtime. Mirrors <see cref="VendorWorkflowAdapter"/> for
/// the Customer entity. The applier covers identity (name / companyName /
/// email / phone) + credit-and-tax (creditLimit / defaultCurrency /
/// isTaxExempt / taxExemptionId).
///
/// <para><b>Scope cuts from the legacy guided-customer-dialog</b>:</para>
/// <list type="bullet">
///   <item><c>EngagementShape</c> picker (QuickQuote / Repeat / Strategic /
///   Prototype) is dropped — the legacy dialog folded the extras into a
///   note string that was never sent to the server (no Notes column on
///   Customer); the picker was dead-letter UI. If the team adds a real
///   engagement-shape column later this is the place to apply it.</item>
///   <item><c>Addresses</c> (billing + shipping) are deferred to the
///   customer detail page post-creation. <see cref="CustomerAddress"/> is
///   a collection entity — the workflow's patchStep model doesn't fit
///   collection mutations cleanly, same constraint that drove vendor's
///   supply-items step into a "do this later" stub.</item>
///   <item><c>ContactName</c> form field is dropped — Customer carries
///   primary contact details on the row (Email/Phone); rich contacts live
///   on the separate <see cref="Contact"/> collection accessible from the
///   customer detail page.</item>
/// </list>
/// </summary>
public class CustomerWorkflowAdapter(AppDbContext db)
    : IWorkflowEntityCreator, IWorkflowFieldApplier, IWorkflowEntityPromoter
{
    public string EntityType => "Customer";

    /// <summary>
    /// completeRun requires a promoter for every entity type. Customer has
    /// no Draft → Active lifecycle (it's IsActive=true from creation), so
    /// promotion is a no-op — same shape as VendorWorkflowAdapter.PromoteAsync.
    /// </summary>
    public Task<bool> PromoteAsync(int entityId, string targetStatus, CancellationToken ct)
        => Task.FromResult(false);

    public async Task<int> CreateDraftAsync(JsonElement? initialData, CancellationToken ct)
    {
        var name = ReadStringOrDefault(initialData, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(
                "Customer requires a name to materialize.",
                new[] { new ValidationFailure("name", "Name is required.") });
        }

        var customer = new Customer
        {
            Name = name,
            CompanyName = ReadStringOrDefault(initialData, "companyName")?.Trim().NullIfEmpty(),
            Email = ReadStringOrDefault(initialData, "email")?.Trim().NullIfEmpty(),
            Phone = ReadStringOrDefault(initialData, "phone")?.Trim().NullIfEmpty(),
            CreditLimit = ReadDecimalOrDefault(initialData, "creditLimit"),
            DefaultCurrency = ReadStringOrDefault(initialData, "defaultCurrency")?.Trim().NullIfEmpty(),
            IsTaxExempt = ReadBoolOrDefault(initialData, "isTaxExempt") ?? false,
            TaxExemptionId = ReadStringOrDefault(initialData, "taxExemptionId")?.Trim().NullIfEmpty(),
            IsActive = true,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return customer.Id;
    }

    public async Task ApplyAsync(int entityId, JsonElement fields, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == entityId, ct)
            ?? throw new KeyNotFoundException($"Customer id {entityId} not found.");

        // Identity step
        if (TryReadString(fields, "name", out var name) && name is not null)
            customer.Name = name.Trim();
        if (TryReadString(fields, "companyName", out var companyName))
            customer.CompanyName = companyName?.Trim().NullIfEmpty();
        if (TryReadString(fields, "email", out var email))
            customer.Email = email?.Trim().NullIfEmpty();
        if (TryReadString(fields, "phone", out var phone))
            customer.Phone = phone?.Trim().NullIfEmpty();

        // Credit + Tax step
        if (TryReadDecimal(fields, "creditLimit", out var creditLimit))
            customer.CreditLimit = creditLimit;
        if (TryReadString(fields, "defaultCurrency", out var currency))
            customer.DefaultCurrency = currency?.Trim().NullIfEmpty();
        if (TryReadBool(fields, "isTaxExempt", out var isTaxExempt) && isTaxExempt.HasValue)
            customer.IsTaxExempt = isTaxExempt.Value;
        if (TryReadString(fields, "taxExemptionId", out var taxExemptionId))
            customer.TaxExemptionId = taxExemptionId?.Trim().NullIfEmpty();

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Workflow abandonment cleanup. Customer has no Draft status, so the
    /// safe-to-cleanup heuristic is "no real transactions yet" — if any
    /// Jobs, Quotes, SalesOrders, Invoices, or Payments exist against the
    /// row, the customer is real data and stays.
    /// </summary>
    public async Task<bool> SoftDeleteIfDraftAsync(int entityId, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == entityId, ct);
        if (customer is null) return false;
        var hasTransactions = await db.Jobs.AnyAsync(j => j.CustomerId == entityId, ct)
                           || await db.Quotes.AnyAsync(q => q.CustomerId == entityId, ct)
                           || await db.SalesOrders.AnyAsync(so => so.CustomerId == entityId, ct)
                           || await db.Invoices.AnyAsync(i => i.CustomerId == entityId, ct)
                           || await db.Payments.AnyAsync(p => p.CustomerId == entityId, ct);
        if (hasTransactions) return false;
        customer.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ─── JSON helpers (kept in sync with PartWorkflowAdapter / VendorWorkflowAdapter) ───

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

    private static bool? ReadBoolOrDefault(JsonElement? root, string name)
    {
        if (root is null || root.Value.ValueKind != JsonValueKind.Object) return null;
        if (!root.Value.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
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

    private static bool TryReadBool(JsonElement root, string name, out bool? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty(name, out var prop)) return false;
        switch (prop.ValueKind)
        {
            case JsonValueKind.Null: value = null; return true;
            case JsonValueKind.True: value = true; return true;
            case JsonValueKind.False: value = false; return true;
            default: return false;
        }
    }
}
