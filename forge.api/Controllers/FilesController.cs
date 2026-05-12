using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Files;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
[RequiresCapability("CAP-CROSS-ATTACHMENTS")]
public class FilesController(IMediator mediator) : ControllerBase
{
    [HttpGet("{entityType}/{entityId:int}/files")]
    public async Task<ActionResult<List<FileAttachmentResponseModel>>> GetFiles(string entityType, int entityId)
    {
        var result = await mediator.Send(new GetFilesQuery(entityType, entityId));
        return Ok(result);
    }

    [HttpPost("{entityType}/{entityId:int}/files")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<FileAttachmentResponseModel>> UploadFile(
        string entityType, int entityId, IFormFile file)
    {
        var result = await mediator.Send(new UploadFileCommand(entityType, entityId, file));
        return Created($"/api/v1/files/{result.Id}", result);
    }

    [HttpPost("{entityType}/{entityId:int}/files/chunked")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<ChunkedUploadResponseModel>> UploadFileChunk(
        string entityType,
        int entityId,
        [FromForm] string uploadId,
        [FromForm] string fileName,
        [FromForm] string contentType,
        [FromForm] int chunkIndex,
        [FromForm] int totalChunks,
        IFormFile chunk)
    {
        var result = await mediator.Send(new UploadFileChunkCommand(
            entityType, entityId, uploadId, fileName, contentType, chunkIndex, totalChunks, chunk));

        if (!result.IsComplete)
        {
            return Ok(result);
        }

        return Created($"/api/v1/files/{result.FileAttachment!.Id}", result);
    }

    [HttpGet("files/{id:int}/download")]
    public async Task<IActionResult> DownloadFile(int id)
    {
        var result = await mediator.Send(new DownloadFileQuery(id));
        return File(result.Stream, result.ContentType, result.FileName);
    }

    [HttpGet("parts/{partId:int}/revisions/{revisionId:int}/files")]
    public async Task<ActionResult<List<FileAttachmentResponseModel>>> GetFilesByRevision(int partId, int revisionId)
    {
        var result = await mediator.Send(new GetFilesByRevisionQuery(partId, revisionId));
        return Ok(result);
    }

    [HttpDelete("files/{id:int}")]
    public async Task<IActionResult> DeleteFile(int id)
    {
        await mediator.Send(new DeleteFileCommand(id));
        return NoContent();
    }
}
