using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class ReportBuilderRepository(AppDbContext context) : IReportBuilderRepository
{
    public async Task<List<SavedReport>> GetUserReports(string userId)
    {
        var userIdInt = int.Parse(userId);
        return await context.SavedReports
            .Where(r => r.UserId == userIdInt)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync();
    }

    public async Task<List<SavedReport>> GetSharedReports()
    {
        return await context.SavedReports
            .Where(r => r.IsShared)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync();
    }

    public async Task<SavedReport?> GetById(int id)
    {
        return await context.SavedReports
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<SavedReport> Create(SavedReport report)
    {
        context.SavedReports.Add(report);
        await context.SaveChangesAsync();
        return report;
    }

    public async Task<SavedReport> Update(SavedReport report)
    {
        context.SavedReports.Update(report);
        await context.SaveChangesAsync();
        return report;
    }

    public async Task Delete(int id, string userId)
    {
        var userIdInt = int.Parse(userId);
        var report = await context.SavedReports
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userIdInt)
            ?? throw new KeyNotFoundException($"Saved report {id} not found or access denied.");

        report.DeletedAt = DateTimeOffset.UtcNow;
        report.DeletedBy = userId;
        await context.SaveChangesAsync();
    }
}
