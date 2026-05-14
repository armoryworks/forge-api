using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record CreateChannelCommand(
    int CreatedById,
    string Name,
    ChannelType ChannelType,
    string? Description,
    string? IconName,
    List<int> MemberIds) : IRequest<ChatRoomResponseModel>;

public class CreateChannelValidator : AbstractValidator<CreateChannelCommand>
{
    public CreateChannelValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ChannelType)
            .Must(t => t is ChannelType.Group or ChannelType.Custom)
            .WithMessage("Only Group and Custom channels can be created by users.");
        RuleFor(x => x.MemberIds).NotEmpty().WithMessage("At least one member is required.");
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.IconName).MaximumLength(50);
    }
}

public class CreateChannelHandler(AppDbContext db) : IRequestHandler<CreateChannelCommand, ChatRoomResponseModel>
{
    public async Task<ChatRoomResponseModel> Handle(CreateChannelCommand request, CancellationToken ct)
    {
        var room = new ChatRoom
        {
            Name = request.Name,
            IsGroup = true,
            CreatedById = request.CreatedById,
            ChannelType = request.ChannelType,
            Description = request.Description,
            IconName = request.IconName,
        };

        var allMemberIds = request.MemberIds.Distinct().ToList();
        if (!allMemberIds.Contains(request.CreatedById))
            allMemberIds.Add(request.CreatedById);

        foreach (var memberId in allMemberIds)
        {
            room.Members.Add(new ChatRoomMember
            {
                UserId = memberId,
                JoinedAt = DateTimeOffset.UtcNow,
                Role = memberId == request.CreatedById ? ChannelMemberRole.Owner : ChannelMemberRole.Member,
            });
        }

        db.Set<ChatRoom>().Add(room);
        await db.SaveChangesAsync(ct);

        var users = await db.Users
            .Where(u => allMemberIds.Contains(u.Id))
            .ToListAsync(ct);

        var memberMap = room.Members.ToDictionary(m => m.UserId);

        return new ChatRoomResponseModel(
            room.Id,
            room.Name,
            room.IsGroup,
            room.CreatedById,
            room.CreatedAt,
            users.Select(u => new ChatRoomMemberResponseModel(
                u.Id,
                (u.FirstName + " " + u.LastName).Trim(),
                u.Initials ?? "??",
                u.AvatarColor ?? "#94a3b8",
                memberMap.GetValueOrDefault(u.Id)?.Role ?? ChannelMemberRole.Member,
                false)).ToList(),
            room.ChannelType,
            room.Description,
            room.TeamId,
            room.IsReadOnly,
            room.IconName);
    }
}
