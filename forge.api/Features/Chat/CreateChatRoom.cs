using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record CreateChatRoomCommand(int CreatedById, string Name, List<int> MemberIds) : IRequest<ChatRoomResponseModel>;

public class CreateChatRoomValidator : AbstractValidator<CreateChatRoomCommand>
{
    public CreateChatRoomValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MemberIds).NotEmpty().WithMessage("At least one member is required.");
    }
}

public class CreateChatRoomHandler(AppDbContext db) : IRequestHandler<CreateChatRoomCommand, ChatRoomResponseModel>
{
    public async Task<ChatRoomResponseModel> Handle(CreateChatRoomCommand request, CancellationToken ct)
    {
        var room = new ChatRoom
        {
            Name = request.Name,
            IsGroup = true,
            CreatedById = request.CreatedById,
            ChannelType = ChannelType.Group,
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
                u.Id == request.CreatedById ? ChannelMemberRole.Owner : ChannelMemberRole.Member,
                false)).ToList(),
            room.ChannelType);
    }
}
