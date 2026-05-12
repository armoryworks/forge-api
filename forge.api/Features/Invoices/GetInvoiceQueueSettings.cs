using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record GetInvoiceQueueSettingsQuery : IRequest<InvoiceQueueSettingsResponse>;

public record InvoiceQueueSettingsResponse(string Mode, int? AssignedUserId, string? AssignedUserName);

public class GetInvoiceQueueSettingsHandler(AppDbContext db) : IRequestHandler<GetInvoiceQueueSettingsQuery, InvoiceQueueSettingsResponse>
{
    public async Task<InvoiceQueueSettingsResponse> Handle(GetInvoiceQueueSettingsQuery request, CancellationToken ct)
    {
        var modeSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "invoice_mode", ct);
        var assigneeSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "invoice_queue_assignee", ct);

        var mode = modeSetting?.Value ?? "direct"; // "direct" or "managed"
        int? assignedUserId = null;
        string? assignedUserName = null;

        if (assigneeSetting is not null && int.TryParse(assigneeSetting.Value, out var userId))
        {
            assignedUserId = userId;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            assignedUserName = user is not null ? $"{user.FirstName} {user.LastName}".Trim() : null;
        }

        return new InvoiceQueueSettingsResponse(mode, assignedUserId, assignedUserName);
    }
}
