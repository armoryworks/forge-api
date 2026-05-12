using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IFileRepository
{
    Task<List<FileAttachmentResponseModel>> GetByEntityAsync(string entityType, int entityId, CancellationToken ct);
    Task<FileAttachment?> FindAsync(int id, CancellationToken ct);
    Task AddAsync(FileAttachment file, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
