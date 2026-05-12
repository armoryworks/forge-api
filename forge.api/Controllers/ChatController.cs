using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Chat;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/chat")]
[Authorize]
[RequiresCapability("CAP-EXT-CHAT")]
public class ChatController(IMediator mediator) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── Direct Message Conversations ──

    [HttpGet("conversations")]
    public async Task<ActionResult<List<ChatConversationResponseModel>>> GetConversations()
    {
        var result = await mediator.Send(new GetConversationsQuery(GetUserId()));
        return Ok(result);
    }

    [HttpGet("messages/{otherUserId:int}")]
    public async Task<ActionResult<List<ChatMessageResponseModel>>> GetMessages(int otherUserId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await mediator.Send(new GetMessagesQuery(GetUserId(), otherUserId, page, pageSize));
        return Ok(result);
    }

    [HttpPost("messages")]
    public async Task<ActionResult<ChatMessageResponseModel>> SendMessage([FromBody] SendChatMessageRequestModel model)
    {
        var result = await mediator.Send(new SendMessageCommand(
            GetUserId(), model.RecipientId, model.Content, model.FileAttachmentId, model.LinkedEntityType, model.LinkedEntityId));
        return CreatedAtAction(nameof(GetMessages), new { otherUserId = model.RecipientId }, result);
    }

    [HttpPost("messages/{otherUserId:int}/read")]
    public async Task<IActionResult> MarkRead(int otherUserId)
    {
        await mediator.Send(new MarkMessagesReadCommand(GetUserId(), otherUserId));
        return NoContent();
    }

    // ── Channels (Group Chat, Teams, Custom, System) ──

    [HttpGet("channels")]
    public async Task<ActionResult<List<ChatRoomResponseModel>>> GetChannels()
    {
        var result = await mediator.Send(new GetChannelsQuery(GetUserId()));
        return Ok(result);
    }

    [HttpPost("channels")]
    public async Task<ActionResult<ChatRoomResponseModel>> CreateChannel([FromBody] CreateChannelRequestModel model)
    {
        var result = await mediator.Send(new CreateChannelCommand(
            GetUserId(), model.Name, model.ChannelType, model.Description, model.IconName, model.MemberIds));
        return CreatedAtAction(nameof(GetChannels), result);
    }

    [HttpPatch("channels/{channelId:int}")]
    public async Task<IActionResult> UpdateChannel(int channelId, [FromBody] UpdateChannelRequestModel model)
    {
        await mediator.Send(new UpdateChannelCommand(GetUserId(), channelId, model.Name, model.Description, model.IconName));
        return NoContent();
    }

    [HttpPost("channels/{channelId:int}/join")]
    public async Task<IActionResult> JoinChannel(int channelId)
    {
        await mediator.Send(new JoinChannelCommand(GetUserId(), channelId));
        return NoContent();
    }

    [HttpPost("channels/{channelId:int}/leave")]
    public async Task<IActionResult> LeaveChannel(int channelId)
    {
        await mediator.Send(new LeaveChannelCommand(GetUserId(), channelId));
        return NoContent();
    }

    [HttpPost("channels/{channelId:int}/mute")]
    public async Task<IActionResult> MuteChannel(int channelId, [FromQuery] bool mute = true)
    {
        await mediator.Send(new MuteChannelCommand(GetUserId(), channelId, mute));
        return NoContent();
    }

    [HttpPost("channels/{channelId:int}/read")]
    public async Task<IActionResult> MarkChannelRead(int channelId)
    {
        await mediator.Send(new MarkChannelReadCommand(GetUserId(), channelId));
        return NoContent();
    }

    [HttpPost("channels/{channelId:int}/upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<FileAttachmentResponseModel>> UploadChatFile(
        int channelId, IFormFile file, CancellationToken ct)
    {
        var result = await mediator.Send(new UploadChatFileCommand(channelId, file), ct);
        return Created($"/api/v1/files/{result.Id}", result);
    }

    [HttpGet("channels/discover")]
    public async Task<ActionResult<List<ChatRoomResponseModel>>> DiscoverChannels([FromQuery] string? search)
    {
        var result = await mediator.Send(new DiscoverChannelsQuery(GetUserId(), search));
        return Ok(result);
    }

    // ── Threads ──

    [HttpGet("messages/{messageId:int}/thread")]
    public async Task<ActionResult<List<ChatMessageResponseModel>>> GetThread(int messageId)
    {
        var result = await mediator.Send(new GetThreadQuery(GetUserId(), messageId));
        return Ok(result);
    }

    [HttpPost("messages/{messageId:int}/reply")]
    public async Task<ActionResult<ChatMessageResponseModel>> ReplyInThread(
        int messageId, [FromBody] ThreadReplyRequestModel model)
    {
        var result = await mediator.Send(new ReplyInThreadCommand(GetUserId(), messageId, model.Content));
        return CreatedAtAction(nameof(GetThread), new { messageId }, result);
    }

    // ── Legacy Chat Rooms (kept for backward compatibility) ──

    [HttpGet("rooms")]
    public async Task<ActionResult<List<ChatRoomResponseModel>>> GetRooms()
    {
        var result = await mediator.Send(new GetChatRoomsQuery(GetUserId()));
        return Ok(result);
    }

    [HttpPost("rooms")]
    public async Task<ActionResult<ChatRoomResponseModel>> CreateRoom([FromBody] CreateChatRoomRequestModel model)
    {
        var result = await mediator.Send(new CreateChatRoomCommand(GetUserId(), model.Name, model.MemberIds));
        return CreatedAtAction(nameof(GetRooms), result);
    }

    [HttpGet("rooms/{roomId:int}/messages")]
    public async Task<ActionResult<List<ChatMessageResponseModel>>> GetRoomMessages(
        int roomId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await mediator.Send(new GetChatRoomMessagesQuery(GetUserId(), roomId, page, pageSize));
        return Ok(result);
    }

    [HttpPost("rooms/{roomId:int}/messages")]
    public async Task<ActionResult<ChatMessageResponseModel>> SendRoomMessage(
        int roomId, [FromBody] SendChatMessageRequestModel model)
    {
        var result = await mediator.Send(new SendChatRoomMessageCommand(
            GetUserId(), roomId, model.Content, model.FileAttachmentId, model.LinkedEntityType, model.LinkedEntityId));
        return CreatedAtAction(nameof(GetRoomMessages), new { roomId }, result);
    }

    [HttpPost("rooms/{roomId:int}/members/{userId:int}")]
    public async Task<IActionResult> AddRoomMember(int roomId, int userId)
    {
        await mediator.Send(new AddChatRoomMemberCommand(roomId, userId, GetUserId()));
        return NoContent();
    }

    [HttpDelete("rooms/{roomId:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveRoomMember(int roomId, int userId)
    {
        await mediator.Send(new RemoveChatRoomMemberCommand(roomId, userId, GetUserId()));
        return NoContent();
    }
}
