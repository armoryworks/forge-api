using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record UpdateChannelCommand(int UserId, int ChannelId, string? Name, string? Description, string? IconName) : IRequest;

public class UpdateChannelValidator : AbstractValidator<UpdateChannelCommand>
{
    public UpdateChannelValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name != null);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description != null);
        RuleFor(x => x.IconName).MaximumLength(50).When(x => x.IconName != null);
    }
}

public class UpdateChannelHandler(AppDbContext db) : IRequestHandler<UpdateChannelCommand>
{
    public async Task Handle(UpdateChannelCommand request, CancellationToken ct)
    {
        var room = await db.Set<ChatRoom>()
            .Include(r => r.Members)
            .FirstOrDefaultAsync(r => r.Id == request.ChannelId, ct)
            ?? throw new KeyNotFoundException($"Channel {request.ChannelId} not found");

        // Verify user is admin or owner
        var member = room.Members.FirstOrDefault(m => m.UserId == request.UserId);
        if (member == null)
            throw new UnauthorizedAccessException("You are not a member of this channel.");
        if (member.Role == ChannelMemberRole.Member)
            throw new UnauthorizedAccessException("Only channel admins and owners can update channel settings.");

        if (request.Name != null) room.Name = request.Name;
        if (request.Description != null) room.Description = request.Description;
        if (request.IconName != null) room.IconName = request.IconName;

        await db.SaveChangesAsync(ct);
    }
}
