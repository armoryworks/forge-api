using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

using Serilog;

namespace Forge.Api.Data.TrainingContent;

public abstract class TrainingContentBase
{
    protected readonly AppDbContext Db;
    protected readonly Dictionary<string, int> SlugMap;

    protected TrainingContentBase(AppDbContext db, Dictionary<string, int> slugMap)
    {
        Db = db;
        SlugMap = slugMap;
    }

    protected async Task<int> GetOrCreateModule(TrainingModule m)
    {
        if (SlugMap.TryGetValue(m.Slug, out var existingId))
        {
            // Update existing module content on re-seed
            var existing = await Db.TrainingModules.FindAsync(existingId);
            if (existing != null)
            {
                existing.Title = m.Title;
                existing.Summary = m.Summary;
                existing.ContentType = m.ContentType;
                existing.ContentJson = m.ContentJson;
                existing.AppRoutes = m.AppRoutes;
                existing.EstimatedMinutes = m.EstimatedMinutes;
                existing.Tags = m.Tags;
                existing.IsPublished = m.IsPublished;
                existing.IsOnboardingRequired = m.IsOnboardingRequired;
                existing.SortOrder = m.SortOrder;
                await Db.SaveChangesAsync();
            }
            return existingId;
        }
        Db.TrainingModules.Add(m);
        await Db.SaveChangesAsync();
        SlugMap[m.Slug] = m.Id;
        return m.Id;
    }

    protected int LookupSlug(string slug)
    {
        return SlugMap.TryGetValue(slug, out var id) ? id : 0;
    }

    public abstract Task SeedAsync();

    /// <summary>
    /// Loads the slug → ID map from the database.
    /// </summary>
    public static async Task<Dictionary<string, int>> LoadSlugMapAsync(AppDbContext db)
    {
        var slugs = await db.TrainingModules
            .AsNoTracking()
            .Select(m => new { m.Id, m.Slug })
            .ToListAsync();
        return slugs.ToDictionary(m => m.Slug, m => m.Id);
    }
}
