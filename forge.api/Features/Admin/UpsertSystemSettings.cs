using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record UpsertSystemSettingsCommand(List<SystemSettingRequestModel> Settings) : IRequest<List<SystemSettingResponseModel>>;

public record SystemSettingRequestModel(string Key, string Value, string? Description);

public class UpsertSystemSettingsHandler(
    ISystemSettingRepository repo,
    AppDbContext db) : IRequestHandler<UpsertSystemSettingsCommand, List<SystemSettingResponseModel>>
{
    public async Task<List<SystemSettingResponseModel>> Handle(UpsertSystemSettingsCommand request, CancellationToken ct)
    {
        string? newCompanyState = null;

        foreach (var item in request.Settings)
        {
            if (item.Key == "company_state")
                newCompanyState = item.Value;

            var existing = await repo.FindByKeyAsync(item.Key, ct);
            if (existing is not null)
            {
                existing.Value = item.Value;
                if (item.Description is not null)
                    existing.Description = item.Description;
            }
            else
            {
                await repo.AddAsync(new SystemSetting
                {
                    Key = item.Key,
                    Value = item.Value,
                    Description = item.Description,
                }, ct);
            }
        }

        await repo.SaveChangesAsync(ct);

        // Auto-link state withholding template when company_state changes
        if (newCompanyState is not null)
            await LinkStateWithholdingTemplateAsync(newCompanyState, ct);

        var all = await repo.GetAllAsync(ct);
        return all.Select(s => new SystemSettingResponseModel(s.Id, s.Key, s.Value, s.Description)).ToList();
    }

    private async Task LinkStateWithholdingTemplateAsync(string stateCode, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .FirstOrDefaultAsync(t => t.FormType == ComplianceFormType.StateWithholding, ct);
        if (template is null) return;

        var stateRef = await db.ReferenceData
            .FirstOrDefaultAsync(r => r.GroupCode == "state_withholding" && r.Code == stateCode, ct);

        if (stateRef?.Metadata is null)
        {
            // Unknown state — clear the template link
            template.DocuSealTemplateId = null;
            template.Name = "State Tax Withholding";
            template.Description = "State-specific income tax withholding form — select a state in system settings.";
            await db.SaveChangesAsync(ct);
            return;
        }

        using var doc = JsonDocument.Parse(stateRef.Metadata);
        var root = doc.RootElement;
        var category = root.TryGetProperty("category", out var catProp) ? catProp.GetString() : null;

        if (category == "no_tax")
        {
            // No income tax — deactivate state withholding requirement
            template.IsActive = false;
            template.DocuSealTemplateId = null;
            template.Name = $"State Tax Withholding ({stateRef.Label})";
            template.Description = $"{stateRef.Label} has no state income tax — this form is not required.";
            template.BlocksJobAssignment = false;
        }
        else if (category == "federal")
        {
            // Uses federal W-4 — link to the federal W-4 DocuSeal template
            var federalTemplate = await db.ComplianceFormTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.FormType == ComplianceFormType.W4, ct);
            template.IsActive = false;
            template.DocuSealTemplateId = federalTemplate?.DocuSealTemplateId;
            template.Name = $"State Tax Withholding ({stateRef.Label})";
            template.Description = $"{stateRef.Label} accepts the federal W-4 — no separate state form required.";
            template.BlocksJobAssignment = false;
        }
        else
        {
            // Has its own form
            var formName = root.TryGetProperty("formName", out var fnProp) ? fnProp.GetString() : "State Form";
            int? docuSealId = root.TryGetProperty("docuSealTemplateId", out var dtProp) ? dtProp.GetInt32() : null;

            template.IsActive = true;
            template.DocuSealTemplateId = docuSealId;
            template.Name = $"State Tax Withholding — {stateRef.Label} {formName}";
            template.Description = docuSealId.HasValue
                ? $"{stateRef.Label} {formName} — ready for e-signatures."
                : $"{stateRef.Label} {formName} — upload the form PDF via DocuSeal to enable e-signatures.";
            template.BlocksJobAssignment = true;
        }

        await db.SaveChangesAsync(ct);
    }
}
