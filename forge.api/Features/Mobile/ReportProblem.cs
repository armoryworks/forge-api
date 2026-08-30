using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record ReportProblemRequestModel(string Message, string? Screen, string? AppVersion, string? Platform);

public record ReportProblemCommand(ReportProblemRequestModel Data, string DeviceKey) : IRequest;

public class ReportProblemValidator : AbstractValidator<ReportProblemCommand>
{
    public ReportProblemValidator()
    {
        RuleFor(x => x.Data.Message).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Data.Screen).MaximumLength(64);
        RuleFor(x => x.Data.AppVersion).MaximumLength(32);
        RuleFor(x => x.Data.Platform).MaximumLength(32);
    }
}

/// <summary>
/// "Report a problem" from the phone: a structured log line (so it lands in
/// this instance's own log sink) plus a notification to every admin, tied to
/// the reporting user and device. Nothing leaves the instance.
/// </summary>
public class ReportProblemHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IMediator mediator,
    ILogger<ReportProblemHandler> logger)
    : IRequestHandler<ReportProblemCommand>
{
    public async Task Handle(ReportProblemCommand request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId;
        var reporter = userId is null
            ? "Shared device"
            : await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.LastName + ", " + u.FirstName)
                .FirstOrDefaultAsync(cancellationToken) ?? $"User {userId}";

        logger.LogWarning(
            "[MOBILE-PROBLEM-REPORT] {Reporter} on {Platform} {AppVersion} ({Device}) at {Screen}: {Message}",
            reporter, request.Data.Platform, request.Data.AppVersion, request.DeviceKey, request.Data.Screen, request.Data.Message);

        var where = string.IsNullOrWhiteSpace(request.Data.Screen) ? string.Empty : $" on {request.Data.Screen}";
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        foreach (var admin in admins.Where(a => a.IsActive))
        {
            await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                UserId: admin.Id,
                Type: "mobile_problem_report",
                Severity: "warning",
                Source: "mobile",
                Title: $"Problem report from {reporter}",
                Message: $"{request.Data.Message}{where} · {request.Data.Platform} {request.Data.AppVersion}".Trim(),
                EntityType: null,
                EntityId: null,
                SenderId: userId)), cancellationToken);
        }
    }
}
