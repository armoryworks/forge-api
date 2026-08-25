namespace Forge.Core.Settings;

/// <summary>
/// Descriptors for the per-entity <c>{entity}.allow_manual_numbers</c> flags. The flags themselves
/// have always lived in <c>system_settings</c> and are enforced by each entity's create/update
/// handlers; these descriptors surface them in the admin settings UI so an operator can actually
/// turn them on without touching the database.
/// </summary>
public static class NumberingSettings
{
    public const string Group = "Numbering";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        Flag("parts.allow_manual_numbers", "Parts", 100),
        Flag("customers.allow_manual_numbers", "Customers", 101),
        Flag("vendors.allow_manual_numbers", "Vendors", 102),
        Flag("leads.allow_manual_numbers", "Leads", 103),
        Flag("quotes.allow_manual_numbers", "Quotes", 104),
        Flag("sales_orders.allow_manual_numbers", "Sales Orders", 105),
        Flag("purchase_orders.allow_manual_numbers", "Purchase Orders", 106),
        Flag("jobs.allow_manual_numbers", "Jobs", 107),
        Flag("shipments.allow_manual_numbers", "Shipments", 108),
        Flag("invoices.allow_manual_numbers", "Invoices", 109),
        Flag("payments.allow_manual_numbers", "Payments", 110),
    ];

    private static SettingDescriptor Flag(string key, string entity, int sortOrder)
        => new(key, Group, $"Manual {entity} Numbers", SettingDataType.Boolean,
            Description: $"Allow users to type or change {entity.ToLowerInvariant()} numbers instead of "
                + "always using the generated ones. Renames keep the old number resolvable via the "
                + "identifier registry. Off = numbers are system-assigned and read-only.",
            DefaultValue: "false",
            SortOrder: sortOrder);
}
