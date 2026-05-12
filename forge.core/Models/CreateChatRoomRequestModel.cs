namespace Forge.Core.Models;

public record CreateChatRoomRequestModel(
    string Name,
    List<int> MemberIds);
