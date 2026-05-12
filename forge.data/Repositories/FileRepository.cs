using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class FileRepository(AppDbContext db) : IFileRepository
{
    public async Task<List<FileAttachmentResponseModel>> GetByEntityAsync(string entityType, int entityId, CancellationToken ct)
    {
        var files = await db.FileAttachments
            .Where(f => f.EntityType == entityType && f.EntityId == entityId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        var userIds = files.Select(f => f.UploadedById).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return files.Select(f => ToResponseModel(f, users)).ToList();
    }

    public Task<FileAttachment?> FindAsync(int id, CancellationToken ct)
        => db.FileAttachments.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task AddAsync(FileAttachment file, CancellationToken ct)
    {
        await db.FileAttachments.AddAsync(file, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);

    private static FileAttachmentResponseModel ToResponseModel(FileAttachment f, Dictionary<int, ApplicationUser> users)
    {
        var uploaderName = users.TryGetValue(f.UploadedById, out var user)
            ? $"{user.FirstName} {user.LastName}" : "Unknown";

        return new FileAttachmentResponseModel(
            f.Id, f.FileName, f.ContentType, f.Size,
            $"/api/v1/files/{f.Id}/download",
            f.EntityType, f.EntityId, f.UploadedById,
            uploaderName, f.CreatedAt, f.PartRevisionId,
            f.RequiredRole);
    }
}
