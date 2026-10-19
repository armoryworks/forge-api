namespace Forge.Core.Models;

public record CreateDocumentRevisionRequestModel(
    int FileAttachmentId,
    string ChangeDescription);
