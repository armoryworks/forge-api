using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IReportBuilderRepository
{
    Task<List<SavedReport>> GetUserReports(string userId);
    Task<List<SavedReport>> GetSharedReports();
    Task<SavedReport?> GetById(int id);
    Task<SavedReport> Create(SavedReport report);
    Task<SavedReport> Update(SavedReport report);
    Task Delete(int id, string userId);
}
