using System.Text.Json;
using System.Text.RegularExpressions;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Api.Features.Presets.Apply.Layers;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.CloudStorage;

public record UpdateCloudStorageFolderMapCommand(List<FolderMapSuggestion> Entries) : IRequest<List<FolderMapSuggestion>>;

public partial class UpdateCloudStorageFolderMapValidator : AbstractValidator<UpdateCloudStorageFolderMapCommand>
{
    public static readonly IReadOnlyList<string> EntityTypes = ["Customer", "Job", "Deliverable"];
    public static readonly IReadOnlyList<string> Tokens = ["Customer", "Project", "Job", "Year", "Month", "Quarter", "EngagementType"];

    [GeneratedRegex(@"\{(?<name>[A-Za-z][A-Za-z0-9_]*)\}")]
    private static partial Regex TokenRe();

    public UpdateCloudStorageFolderMapValidator()
    {
        RuleFor(x => x.Entries).NotNull();
        RuleFor(x => x.Entries)
            .Must(e => e.Select(s => s.EntityType).Distinct(StringComparer.OrdinalIgnoreCase).Count() == e.Count)
            .WithMessage("Each record type may appear only once in the folder map.")
            .WithErrorCode("folder-map-duplicate-entity")
            .When(x => x.Entries is not null);

        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.EntityType)
                .Must(t => EntityTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Record type must be one of: {string.Join(", ", EntityTypes)}.")
                .WithErrorCode("folder-map-entity-unknown");
            entry.RuleFor(e => e.PathTemplate)
                .NotEmpty().MaximumLength(500)
                .Must(OnlyKnownTokens)
                .WithMessage($"Path templates may use only these tokens: {string.Join(", ", Tokens.Select(t => "{" + t + "}"))}.")
                .WithErrorCode("folder-map-token-unknown");
            entry.RuleFor(e => e.SubfolderNames).NotNull();
            entry.RuleFor(e => e.SubfolderNames.Count).LessThanOrEqualTo(20)
                .When(e => e.SubfolderNames is not null);
            entry.RuleForEach(e => e.SubfolderNames)
                .NotEmpty().MaximumLength(100)
                .Must(n => n.IndexOfAny(['/', '\\']) < 0).WithMessage("Subfolder names cannot contain slashes.");
        });
    }

    private static bool OnlyKnownTokens(string? template) =>
        template is null || TokenRe().Matches(template)
            .All(m => Tokens.Contains(m.Groups["name"].Value, StringComparer.OrdinalIgnoreCase));
}

public class UpdateCloudStorageFolderMapHandler(AppDbContext db) : IRequestHandler<UpdateCloudStorageFolderMapCommand, List<FolderMapSuggestion>>
{
    public async Task<List<FolderMapSuggestion>> Handle(UpdateCloudStorageFolderMapCommand request, CancellationToken ct)
    {
        var entries = request.Entries
            .Select(e => new FolderMapSuggestion(
                UpdateCloudStorageFolderMapValidator.EntityTypes.First(t => string.Equals(t, e.EntityType, StringComparison.OrdinalIgnoreCase)),
                e.PathTemplate.Trim(),
                e.SubfolderNames.Select(n => n.Trim()).ToList(),
                e.AutoCreateOnEntityCreate))
            .ToList();
        var json = JsonSerializer.Serialize(entries);

        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == FolderMapBundleApplier.FolderMapSettingKey, ct);
        if (setting is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = FolderMapBundleApplier.FolderMapSettingKey,
                Value = json,
                Description = "Folder-map catalog (edited in Admin > Cloud storage).",
            });
        }
        else
        {
            setting.Value = json;
        }

        await db.SaveChangesAsync(ct);
        return entries;
    }
}
