using MediatR;

using Forge.Core.Settings;

namespace Forge.Api.Features.Settings;

/// <summary>
/// Phase 1m — admin UI's discovery endpoint. Returns the descriptor
/// list + each descriptor's current effective value. Secrets are masked
/// (returned as fixed-length placeholder when set, empty when unset)
/// so the UI never receives the plaintext token over the wire after
/// the initial save.
/// </summary>
public record GetSettingsCatalogQuery(string? Group = null) : IRequest<List<SettingsCatalogEntry>>;

public sealed record SettingsCatalogEntry(
    string Key,
    string Group,
    string DisplayName,
    string? Description,
    string DataType,
    string? DefaultValue,
    bool IsSecret,
    bool IsRequired,
    int SortOrder,
    string? Value,
    bool HasValue,
    IReadOnlyList<EnumChoice>? Choices);

public class GetSettingsCatalogHandler(ISettingsService settings)
    : IRequestHandler<GetSettingsCatalogQuery, List<SettingsCatalogEntry>>
{
    private const string SecretMask = "••••••••";

    public async Task<List<SettingsCatalogEntry>> Handle(
        GetSettingsCatalogQuery request, CancellationToken cancellationToken)
    {
        var descriptors = string.IsNullOrEmpty(request.Group)
            ? SettingDescriptorCatalog.All
            : SettingDescriptorCatalog.ForGroup(request.Group);

        var result = new List<SettingsCatalogEntry>(descriptors.Count);
        foreach (var d in descriptors)
        {
            var raw = await settings.GetStringAsync(d.Key, cancellationToken);
            var hasValue = !string.IsNullOrEmpty(raw) && raw != d.DefaultValue;
            // Mask secrets — only the existence is leaked, never the value.
            // Default values for non-secret settings are returned so the UI
            // can show "(default)" inline.
            var display = d.IsSecret && hasValue ? SecretMask : raw;

            result.Add(new SettingsCatalogEntry(
                Key: d.Key,
                Group: d.Group,
                DisplayName: d.DisplayName,
                Description: d.Description,
                DataType: d.DataType.ToString(),
                DefaultValue: d.DefaultValue,
                IsSecret: d.IsSecret,
                IsRequired: d.IsRequired,
                SortOrder: d.SortOrder,
                Value: display,
                HasValue: hasValue,
                Choices: d.Choices));
        }
        return result;
    }
}
