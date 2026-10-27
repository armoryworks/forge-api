namespace Forge.Api.Authorization;

/// <summary>
/// The access rule for every entity type that the cross-cutting files, activity and
/// preview endpoints serve. Those endpoints take the type from the URL, so without
/// this map any signed-in user could read any record's files, notes and totals. The
/// role sets mirror the owning controller's class-level <c>[Authorize]</c>; the
/// Architecture tests pin each one to that controller. An unlisted type is denied.
/// </summary>
public static class EntityAccessRules
{
    public const string HrRoles = "Admin,Manager,OfficeManager";

    private const string JobRoles = "Admin,Manager,PM,Engineer,ProductionWorker,OfficeManager,Procurement";
    private const string PartRoles = "Admin,Manager,Engineer,ProductionWorker,PM,OfficeManager,Procurement";
    private const string SalesRoles = "Admin,Manager,OfficeManager,PM";
    private const string FinanceRoles = "Admin,Manager,OfficeManager";
    private const string PurchasingRoles = "Admin,Manager,OfficeManager,Procurement";

    private static readonly Dictionary<string, EntityAccessRule> Rules = new()
    {
        ["job"] = Allow(JobRoles),
        ["part"] = Allow(PartRoles),
        ["operation"] = Allow(PartRoles),
        ["asset"] = Allow("Admin,Manager,Engineer"),
        ["lead"] = Allow("Admin,Manager,PM,LeadIntake"),
        ["customer"] = Allow("Admin,Manager,OfficeManager,PM,Engineer"),
        ["salesorder"] = Allow(SalesRoles),
        ["quote"] = Allow(SalesRoles),
        ["invoice"] = Allow(FinanceRoles),
        ["shipment"] = Allow(FinanceRoles),
        ["payment"] = Allow(FinanceRoles),
        ["customerreturn"] = Allow(FinanceRoles),
        ["creditmemo"] = Allow("Admin,Manager,OfficeManager,Controller"),
        ["purchaseorder"] = Allow(PurchasingRoles),
        ["vendor"] = Allow(PurchasingRoles),
        ["vendorbill"] = Allow("Admin,Manager,OfficeManager,Procurement,Controller"),
        ["vendorcreditmemo"] = Allow("Admin,Manager,OfficeManager,Procurement,Controller"),
        ["vendorreturn"] = Allow("Admin,Manager,OfficeManager,Procurement"),
        ["paymentrun"] = Allow("Admin,Manager,OfficeManager,Controller"),
        ["vendorpayment"] = Allow("Admin,Manager,OfficeManager,Controller"),
        ["lot"] = Allow("Admin,Manager,Engineer,ProductionWorker"),
        ["qcinspection"] = AnySignedIn(),
        ["nonconformance"] = AnySignedIn(),
        ["ppapelement"] = AnySignedIn(),
        ["receivinginspectiontemplate"] = AnySignedIn(),
        ["compliancetemplate"] = new(null, Set("Admin"), EntityOwnership.None),
        ["controlleddocument"] = new(null, Set("Admin,Manager,Engineer,ComplianceOfficer"), EntityOwnership.None),
        ["expense"] = Owned(HrRoles, EntityOwnership.Expense),
        ["employee"] = Owned("Admin,Manager", EntityOwnership.Employee),
        ["paystub"] = Owned(HrRoles, EntityOwnership.User),
        ["taxdocument"] = Owned(HrRoles, EntityOwnership.User),
        ["identitydoc"] = Owned(HrRoles, EntityOwnership.User),
        ["identitydocument"] = Owned(HrRoles, EntityOwnership.User),
        ["employeedoc"] = Owned(HrRoles, EntityOwnership.User),
        ["directdeposit"] = Owned(HrRoles, EntityOwnership.User),
        ["user"] = Owned(HrRoles, EntityOwnership.User),
        ["compliancesubmission"] = Owned(HrRoles, EntityOwnership.ComplianceSubmission),
        ["chat"] = Owned(string.Empty, EntityOwnership.ChatRoom),
    };

    /// <summary>Every normalised key with its rule.</summary>
    public static IReadOnlyDictionary<string, EntityAccessRule> All => Rules;

    /// <summary>
    /// Folds the three spellings the endpoints use (files: <c>sales-orders</c>,
    /// activity: <c>SalesOrder</c>, preview: <c>sales-order</c>) to one key.
    /// </summary>
    public static string Normalize(string entityType)
    {
        var key = new string(entityType.Trim().ToLowerInvariant()
            .Where(c => c is not ('-' or '_' or ' ')).ToArray());
        return key.Length > 1 && key.EndsWith('s') ? key[..^1] : key;
    }

    /// <summary>The rule for an entity type in any spelling, or null when the type is not served.</summary>
    public static EntityAccessRule? Find(string? entityType) =>
        string.IsNullOrWhiteSpace(entityType) ? null : Rules.GetValueOrDefault(Normalize(entityType));

    private static EntityAccessRule Allow(string roles) => new(Set(roles), null, EntityOwnership.None);

    private static EntityAccessRule AnySignedIn() => new(null, null, EntityOwnership.None);

    private static EntityAccessRule Owned(string roles, EntityOwnership ownership) => new(Set(roles), null, ownership);

    private static HashSet<string> Set(string roles) =>
        roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
}
