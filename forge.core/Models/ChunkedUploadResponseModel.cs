namespace Forge.Core.Models;

public record ChunkedUploadResponseModel(
    string UploadId,
    int ChunkIndex,
    bool IsComplete,
    FileAttachmentResponseModel? FileAttachment);
