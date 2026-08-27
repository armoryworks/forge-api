using System.Text.Json;

using FluentAssertions;

using Forge.Api.Data;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Architecture;

/// <summary>
/// Seeds every training module into an in-memory context and checks the
/// content the renderers and the quiz scorer actually read. A quiz written
/// in the wrong shape (no <c>isCorrect</c> option) would score every
/// attempt as zero without any error — this is what caught that.
/// </summary>
public sealed class TrainingContentShapeTests
{
    [Fact]
    public async Task Every_module_has_valid_json_routes_and_a_scorable_quiz()
    {
        using var db = TestDbContextFactory.Create();
        var slugMap = new Dictionary<string, int>();
        foreach (var seeder in SeedData.CreateSeeders(db, slugMap))
            await seeder.SeedAsync();

        var failures = new List<string>();
        foreach (var m in db.TrainingModules)
        {
            JsonDocument content;
            try { content = JsonDocument.Parse(m.ContentJson); }
            catch (JsonException e) { failures.Add($"{m.Slug}: ContentJson is not JSON ({e.Message})"); continue; }

            try
            {
                var routes = JsonSerializer.Deserialize<string[]>(m.AppRoutes ?? "[]") ?? [];
                if (routes.Length == 0) failures.Add($"{m.Slug}: no AppRoutes");
                foreach (var r in routes.Where(r => !r.StartsWith('/'))) failures.Add($"{m.Slug}: AppRoute '{r}' must start with /");
            }
            catch (JsonException) { failures.Add($"{m.Slug}: AppRoutes is not a JSON array"); }

            // The DB caps these; the in-memory provider does not, so a long
            // string would only surface as a crash seeding a real install.
            if (m.Title.Length > 300) failures.Add($"{m.Slug}: title is {m.Title.Length} chars (max 300)");
            if (m.Slug.Length > 200) failures.Add($"{m.Slug}: slug is {m.Slug.Length} chars (max 200)");
            if (m.Summary.Length > 1000) failures.Add($"{m.Slug}: summary is {m.Summary.Length} chars (max 1000)");

            var root = content.RootElement;
            switch (m.ContentType)
            {
                case TrainingContentType.Quiz:
                    if (!root.TryGetProperty("questions", out var questions) || questions.GetArrayLength() == 0)
                    { failures.Add($"{m.Slug}: quiz has no questions"); break; }
                    foreach (var q in questions.EnumerateArray())
                    {
                        var qid = q.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        if (string.IsNullOrEmpty(qid)) failures.Add($"{m.Slug}: a question has no id");
                        if (!q.TryGetProperty("text", out _)) failures.Add($"{m.Slug}: question {qid} has no text");
                        if (!q.TryGetProperty("options", out var opts)) { failures.Add($"{m.Slug}: question {qid} has no options"); continue; }
                        var correct = opts.EnumerateArray().Count(o => o.TryGetProperty("isCorrect", out var c) && c.GetBoolean());
                        if (correct != 1) failures.Add($"{m.Slug}: question {qid} has {correct} correct options (needs exactly one isCorrect)");
                        if (opts.EnumerateArray().Any(o => !o.TryGetProperty("id", out _))) failures.Add($"{m.Slug}: question {qid} has an option without id");
                    }
                    break;
                case TrainingContentType.Walkthrough:
                    if (!root.TryGetProperty("appRoute", out _)) failures.Add($"{m.Slug}: walkthrough has no appRoute");
                    if (!root.TryGetProperty("steps", out var steps) || steps.GetArrayLength() == 0) failures.Add($"{m.Slug}: walkthrough has no steps");
                    else foreach (var step in steps.EnumerateArray())
                        if (!step.TryGetProperty("element", out _) || !step.TryGetProperty("popover", out _))
                            failures.Add($"{m.Slug}: a walkthrough step lacks element/popover");
                    break;
                case TrainingContentType.Article:
                    if (!root.TryGetProperty("body", out var body) || string.IsNullOrWhiteSpace(body.GetString()))
                        failures.Add($"{m.Slug}: article has no body");
                    break;
                case TrainingContentType.QuickRef:
                    if (!root.TryGetProperty("groups", out var groups) || groups.GetArrayLength() == 0)
                        failures.Add($"{m.Slug}: quick reference has no groups");
                    break;
            }
        }

        failures.Should().BeEmpty(string.Join("\n  ", failures));
    }
}
