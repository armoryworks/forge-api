using System.Text.Json.Nodes;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

using Serilog;

namespace Forge.Api.Data.TrainingContent;

/// <summary>
/// Seeds per-locale training translations from JSON under Data/Seeds/training-i18n/&lt;locale&gt;/.
/// Each module file is an array of { slug, title, summary, contentJson }; a file named
/// paths.json is an array of { slug, title, description }. English is the canonical base
/// (no rows here). Idempotent: upserts by (entity id, locale), matching by slug.
/// </summary>
public class TrainingTranslationSeeder(AppDbContext db)
{
    public async Task SeedAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Data", "Seeds", "training-i18n");
        if (!Directory.Exists(root)) return;

        var moduleSlugToId = await db.TrainingModules.AsNoTracking().ToDictionaryAsync(m => m.Slug, m => m.Id);
        var pathSlugToId = await db.TrainingPaths.AsNoTracking().ToDictionaryAsync(p => p.Slug, p => p.Id);
        var moduleByKey = (await db.TrainingModuleTranslations.ToListAsync())
            .ToDictionary(t => (t.TrainingModuleId, t.Locale));
        var pathByKey = (await db.TrainingPathTranslations.ToListAsync())
            .ToDictionary(t => (t.TrainingPathId, t.Locale));

        var now = DateTime.UtcNow;   // BaseEntity rows are not auto-stamped by SetTimestamps
        int mod = 0, pth = 0;
        foreach (var localeDir in Directory.GetDirectories(root))
        {
            var locale = Path.GetFileName(localeDir).ToLowerInvariant();
            foreach (var file in Directory.GetFiles(localeDir, "*.json"))
            {
                var isPaths = Path.GetFileNameWithoutExtension(file).Equals("paths", StringComparison.OrdinalIgnoreCase);
                JsonArray? arr;
                try { arr = JsonNode.Parse(await File.ReadAllTextAsync(file))?.AsArray(); }
                catch { Log.Warning("Training translation file {File} is not valid JSON — skipped", file); continue; }
                if (arr is null) continue;

                foreach (var node in arr)
                {
                    var obj = node?.AsObject();
                    var slug = obj?["slug"]?.GetValue<string>();
                    if (obj is null || string.IsNullOrEmpty(slug)) continue;

                    if (isPaths)
                    {
                        if (!pathSlugToId.TryGetValue(slug, out var pid)) continue;
                        var title = obj["title"]?.GetValue<string>() ?? "";
                        var desc = obj["description"]?.GetValue<string>() ?? "";
                        if (pathByKey.TryGetValue((pid, locale), out var ex)) { ex.Title = title; ex.Description = desc; ex.UpdatedAt = now; }
                        else
                        {
                            var t = new TrainingPathTranslation { TrainingPathId = pid, Locale = locale, Title = title, Description = desc, CreatedAt = now, UpdatedAt = now };
                            db.TrainingPathTranslations.Add(t);
                            pathByKey[(pid, locale)] = t;
                        }
                        pth++;
                    }
                    else
                    {
                        if (!moduleSlugToId.TryGetValue(slug, out var mid)) continue;
                        var title = obj["title"]?.GetValue<string>() ?? "";
                        var summary = obj["summary"]?.GetValue<string>() ?? "";
                        var contentJson = obj["contentJson"]?.ToJsonString() ?? "{}";
                        if (moduleByKey.TryGetValue((mid, locale), out var ex)) { ex.Title = title; ex.Summary = summary; ex.ContentJson = contentJson; ex.UpdatedAt = now; }
                        else
                        {
                            var t = new TrainingModuleTranslation { TrainingModuleId = mid, Locale = locale, Title = title, Summary = summary, ContentJson = contentJson, CreatedAt = now, UpdatedAt = now };
                            db.TrainingModuleTranslations.Add(t);
                            moduleByKey[(mid, locale)] = t;
                        }
                        mod++;
                    }
                }
            }
        }

        await db.SaveChangesAsync();
        Log.Information("Seeded training translations: {Mod} module + {Path} path locale-entries", mod, pth);
    }
}
