namespace Forge.Core.Settings;

/// <summary>
/// Phase 1m — schema descriptor for one admin-managed setting. Mirrors
/// the Capability Catalog pattern: descriptors live in code (source-
/// controlled), the runtime value lives in the <c>system_settings</c>
/// table.
///
/// Setting keys are dotted-namespace ("oauth-imap.google.client-id",
/// "twilio.auth-token"). The descriptor's Group + DisplayName drive
/// the admin UI; DataType drives the editor widget.
///
/// For secrets (DataType=Secret): the runtime value in the DB is a
/// sealed envelope (Data Protection API). The settings service auto-
/// unseals on read; the admin UI masks the value on display + edit.
/// </summary>
public sealed record SettingDescriptor(
    string Key,
    string Group,
    string DisplayName,
    SettingDataType DataType,
    string? Description = null,
    string? DefaultValue = null,
    bool IsSecret = false,
    bool IsRequired = false,
    /// <summary>
    /// Optional regex the value must match before save. Null = no
    /// pattern check beyond the data-type's own parsing.
    /// </summary>
    string? ValidationPattern = null,
    /// <summary>
    /// Optional sort key inside a group (lower = earlier). When unset,
    /// admin UI sorts by Key alphabetically.
    /// </summary>
    int SortOrder = 0,
    /// <summary>
    /// Predefined value choices when DataType=Enum. Format: list of
    /// (canonical-value, display-label) pairs.
    /// </summary>
    IReadOnlyList<EnumChoice>? Choices = null);

public sealed record EnumChoice(string Value, string Label);

public enum SettingDataType
{
    String,
    Secret,
    Boolean,
    Integer,
    Url,
    Json,
    Enum,
}
