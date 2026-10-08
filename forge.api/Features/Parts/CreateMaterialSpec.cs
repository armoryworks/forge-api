using System.Text;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

using ReferenceDataEntity = Forge.Core.Entities.ReferenceData;

namespace Forge.Api.Features.Parts;

public record CreateMaterialSpecCommand(CreateMaterialSpecRequestModel Body) : IRequest<ReferenceDataResponseModel>;

public class CreateMaterialSpecValidator : AbstractValidator<CreateMaterialSpecCommand>
{
    public CreateMaterialSpecValidator()
    {
        RuleFor(x => x.Body.Label).NotEmpty().Must(l => !string.IsNullOrWhiteSpace(l)).MaximumLength(200);
        RuleFor(x => x.Body.NewCategoryLabel)
            .Must(l => !string.IsNullOrWhiteSpace(l)).MaximumLength(200)
            .When(x => x.Body.NewCategoryLabel is not null);
        RuleFor(x => x.Body.ParentId).GreaterThan(0).When(x => x.Body.ParentId is not null);
        RuleFor(x => x.Body)
            .Must(b => b.ParentId is null || b.NewCategoryLabel is null)
            .WithMessage("Choose an existing category or name a new one, not both.");
    }
}

public class CreateMaterialSpecHandler(AppDbContext db)
    : IRequestHandler<CreateMaterialSpecCommand, ReferenceDataResponseModel>
{
    public const string GroupCode = "part.material_spec";

    private const int MaxCodeBaseLength = 44;

    public async Task<ReferenceDataResponseModel> Handle(CreateMaterialSpecCommand request, CancellationToken ct)
    {
        var label = request.Body.Label.Trim();
        var categoryLabel = request.Body.NewCategoryLabel?.Trim();

        ReferenceDataEntity? parent = null;
        if (request.Body.ParentId is int parentId)
        {
            parent = await db.ReferenceData.FirstOrDefaultAsync(
                    r => r.Id == parentId && r.GroupCode == GroupCode && r.ParentId == null, ct)
                ?? throw new ValidationException(
                [
                    new ValidationFailure(nameof(CreateMaterialSpecRequestModel.ParentId),
                        "The category must be a top-level material."),
                ]);
        }

        if (categoryLabel is not null)
            await EnsureUniqueAsync(null, categoryLabel, "category", categoryLabel, ct);
        else
            await EnsureUniqueAsync(parent?.Id, label, "material", DisplayName(parent?.Label, label), ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (categoryLabel is not null)
            parent = await AddRowAsync(null, categoryLabel, $"Material category added: {categoryLabel}", ct);

        var entry = await AddRowAsync(parent, label, $"Material added: {DisplayName(parent?.Label, label)}", ct);

        await tx.CommitAsync(ct);

        return new ReferenceDataResponseModel(
            entry.Id, entry.Code, entry.Label, entry.SortOrder, entry.IsActive, entry.IsSeedData,
            entry.EffectiveFrom, entry.EffectiveTo, entry.Metadata, entry.ParentId);
    }

    private async Task EnsureUniqueAsync(
        int? parentId, string label, string noun, string displayName, CancellationToken ct)
    {
        var lowered = label.ToLowerInvariant();
        var existingActive = await db.ReferenceData
            .Where(r => r.GroupCode == GroupCode && r.ParentId == parentId && r.Label.ToLower() == lowered)
            .Select(r => (bool?)r.IsActive)
            .OrderByDescending(a => a)
            .FirstOrDefaultAsync(ct);
        if (existingActive is true)
            throw new InvalidOperationException($"That {noun} already exists: {displayName}.");
        if (existingActive is false)
            throw new InvalidOperationException(
                $"That {noun} already exists but is retired: {displayName}. Ask an admin to reactivate it.");
    }

    private async Task<ReferenceDataEntity> AddRowAsync(
        ReferenceDataEntity? parent, string label, string activityDescription, CancellationToken ct)
    {
        var parentId = parent?.Id;
        var maxSort = await db.ReferenceData
            .Where(r => r.GroupCode == GroupCode && r.ParentId == parentId)
            .MaxAsync(r => (int?)r.SortOrder, ct);

        var row = new ReferenceDataEntity
        {
            GroupCode = GroupCode,
            Code = await UniqueCodeAsync(parent?.Code, label, ct),
            Label = label,
            SortOrder = (maxSort ?? 0) + 10,
            IsActive = true,
            ParentId = parentId,
        };

        db.ReferenceData.Add(row);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("created", activityDescription, ("ReferenceData", row.Id));
        await db.SaveChangesAsync(ct);

        return row;
    }

    private async Task<string> UniqueCodeAsync(string? parentCode, string label, CancellationToken ct)
    {
        var baseCode = Slugify(parentCode is null ? label : $"{parentCode} {label}");
        if (baseCode.Length == 0)
            baseCode = "material";
        if (baseCode.Length > MaxCodeBaseLength)
            baseCode = baseCode[..MaxCodeBaseLength].TrimEnd('-');

        var taken = (await db.ReferenceData
                .Where(r => r.GroupCode == GroupCode && r.Code.StartsWith(baseCode))
                .Select(r => r.Code)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var code = baseCode;
        for (var n = 2; taken.Contains(code); n++)
            code = $"{baseCode}-{n}";
        return code;
    }

    private static string Slugify(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    private static string DisplayName(string? parentLabel, string label)
        => parentLabel is null ? label : $"{parentLabel} / {label}";
}
