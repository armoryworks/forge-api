using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.EmployeeProfile;

public record AcknowledgeFormCommand(int UserId, string FormType) : IRequest;

public class AcknowledgeFormHandler(AppDbContext db) : IRequestHandler<AcknowledgeFormCommand>
{
    // Normalize camelCase keys (from seed data) to snake_case (handler convention)
    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["w4"] = "w4",
        ["i9"] = "i9",
        ["handbook"] = "handbook",
        ["state_withholding"] = "state_withholding",
        ["stateWithholding"] = "state_withholding",
        ["direct_deposit"] = "direct_deposit",
        ["directDeposit"] = "direct_deposit",
        ["workers_comp"] = "workers_comp",
        ["workersComp"] = "workers_comp",
    };

    public async Task Handle(AcknowledgeFormCommand request, CancellationToken ct)
    {
        if (!KeyAliases.TryGetValue(request.FormType, out var normalizedKey))
            throw new ArgumentException($"Invalid form type: {request.FormType}");

        var profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (profile is null)
        {
            profile = new Core.Entities.EmployeeProfile { UserId = request.UserId };
            db.EmployeeProfiles.Add(profile);
        }

        var now = DateTimeOffset.UtcNow;

        switch (normalizedKey)
        {
            case "w4":
                profile.W4CompletedAt = now;
                break;
            case "state_withholding":
                profile.StateWithholdingCompletedAt = now;
                break;
            case "i9":
                profile.I9CompletedAt = now;
                break;
            case "direct_deposit":
                profile.DirectDepositCompletedAt = now;
                break;
            case "workers_comp":
                profile.WorkersCompAcknowledgedAt = now;
                break;
            case "handbook":
                profile.HandbookAcknowledgedAt = now;
                break;
        }

        await db.SaveChangesAsync(ct);
    }
}
