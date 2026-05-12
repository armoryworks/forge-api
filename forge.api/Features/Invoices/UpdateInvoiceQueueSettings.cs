using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record UpdateInvoiceQueueSettingsCommand(string Mode, int? AssignedUserId) : IRequest;

public class UpdateInvoiceQueueSettingsValidator : AbstractValidator<UpdateInvoiceQueueSettingsCommand>
{
    public UpdateInvoiceQueueSettingsValidator()
    {
        RuleFor(x => x.Mode).Must(m => m is "direct" or "managed").WithMessage("Mode must be 'direct' or 'managed'.");
    }
}

public class UpdateInvoiceQueueSettingsHandler(AppDbContext db) : IRequestHandler<UpdateInvoiceQueueSettingsCommand>
{
    public async Task Handle(UpdateInvoiceQueueSettingsCommand request, CancellationToken ct)
    {
        await UpsertSetting("invoice_mode", request.Mode, "Invoice workflow mode: direct or managed", ct);

        if (request.AssignedUserId.HasValue)
            await UpsertSetting("invoice_queue_assignee", request.AssignedUserId.Value.ToString(), "User ID assigned to manage the invoice queue", ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task UpsertSetting(string key, string value, string description, CancellationToken ct)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            await db.SystemSettings.AddAsync(new SystemSetting { Key = key, Value = value, Description = description }, ct);
        }
        else
        {
            setting.Value = value;
        }
    }
}
