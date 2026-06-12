namespace Forge.Core.Settings;

/// <summary>
/// Phase 1m — aggregator for all setting descriptors across the install.
/// Each integration declares its own static descriptor list (e.g.
/// <c>OAuthImapSettings.Descriptors</c>); this class flattens them into
/// a single catalog the admin UI + settings service consume.
///
/// Adding a new setting:
///   1. Define its <see cref="SettingDescriptor"/> in the integration's
///      static settings class.
///   2. Reference that list inside <see cref="All"/>.
///   3. Codegen — no migration needed; the system_settings table is
///      already there. Default values render in the admin UI immediately.
/// </summary>
public static class SettingDescriptorCatalog
{
    private static List<SettingDescriptor>? _all;

    public static IReadOnlyList<SettingDescriptor> All => _all ??= BuildAll();

    public static SettingDescriptor? FindByKey(string key)
        => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Groups => All
        .Select(d => d.Group)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static IReadOnlyList<SettingDescriptor> ForGroup(string group) => All
        .Where(d => string.Equals(d.Group, group, StringComparison.OrdinalIgnoreCase))
        .OrderBy(d => d.SortOrder)
        .ThenBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<SettingDescriptor> BuildAll()
    {
        var list = new List<SettingDescriptor>();
        list.AddRange(OAuthImapSettings.Descriptors);
        list.AddRange(TwilioSettings.Descriptors);
        list.AddRange(UspsSettings.Descriptors);
        list.AddRange(SmtpSettings.Descriptors);
        list.AddRange(AiSettings.Descriptors);
        list.AddRange(DocuSealSettings.Descriptors);
        list.AddRange(MinioSettings.Descriptors);
        list.AddRange(GoogleDriveSettings.Descriptors);
        list.AddRange(AccountingSettings.Descriptors);
        list.AddRange(VarianceWatchdogSettings.Descriptors);
        list.AddRange(ShippingSettings.Descriptors);
        list.AddRange(PaymentsSettings.Descriptors);
        list.AddRange(BankingSettings.Descriptors);
        return list;
    }
}
