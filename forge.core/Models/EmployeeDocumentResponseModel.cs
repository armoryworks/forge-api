namespace Forge.Core.Models;

public record EmployeeDocumentResponseModel(
    int Id,
    string FileName,
    string ContentType,
    long Size,
    string? DocumentType,
    DateTimeOffset? ExpirationDate,
    DateTimeOffset CreatedAt);
