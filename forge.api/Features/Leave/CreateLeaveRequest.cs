using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public record CreateLeaveRequestCommand(CreateLeaveRequestModel Request, int UserId) : IRequest<LeaveRequestResponseModel>;

public class CreateLeaveRequestValidator : AbstractValidator<CreateLeaveRequestCommand>
{
    public CreateLeaveRequestValidator()
    {
        RuleFor(x => x.Request.PolicyId).GreaterThan(0);
        RuleFor(x => x.Request.StartDate).NotEmpty();
        RuleFor(x => x.Request.EndDate).GreaterThanOrEqualTo(x => x.Request.StartDate);
        RuleFor(x => x.Request.Hours).GreaterThan(0);
    }
}

public class CreateLeaveRequestHandler(AppDbContext db) : IRequestHandler<CreateLeaveRequestCommand, LeaveRequestResponseModel>
{
    public async Task<LeaveRequestResponseModel> Handle(CreateLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var policy = await db.LeavePolicies.FindAsync([request.Request.PolicyId], cancellationToken)
            ?? throw new KeyNotFoundException($"Leave policy {request.Request.PolicyId} not found");

        var user = await db.Users.FindAsync([request.UserId], cancellationToken)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        var leaveRequest = new LeaveRequest
        {
            UserId = request.UserId,
            PolicyId = request.Request.PolicyId,
            StartDate = request.Request.StartDate,
            EndDate = request.Request.EndDate,
            Hours = request.Request.Hours,
            Status = LeaveRequestStatus.Pending,
            Reason = request.Request.Reason?.Trim(),
        };

        db.LeaveRequests.Add(leaveRequest);
        await db.SaveChangesAsync(cancellationToken);

        var userName = user.LastName + ", " + user.FirstName;

        return new LeaveRequestResponseModel(
            leaveRequest.Id, leaveRequest.UserId, userName,
            leaveRequest.PolicyId, policy.Name,
            leaveRequest.StartDate, leaveRequest.EndDate, leaveRequest.Hours,
            leaveRequest.Status, null, null, null,
            leaveRequest.Reason, null, leaveRequest.CreatedAt);
    }
}
