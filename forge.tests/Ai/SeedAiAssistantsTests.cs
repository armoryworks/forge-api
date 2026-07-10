using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.AiAssistants;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Ai;

/// <summary>
/// The built-in assistant seeder is idempotent BY NAME: a fresh DB gets the full built-in set, and an
/// install that already has the older built-ins gets ONLY the newly-added ones on the next boot — no
/// duplicates, no clobbering. This is what lets new built-ins (e.g. the Barcode & AIDC Advisory) reach
/// existing installs; the previous seed-only-when-empty guard never delivered later additions.
/// </summary>
public class SeedAiAssistantsTests
{
    private const string BarcodeName = "Barcode & AIDC Advisory";

    [Fact]
    public async Task Fresh_db_seeds_the_full_built_in_set_including_the_barcode_advisory()
    {
        await using var db = TestDbContextFactory.Create();

        await SeedAiAssistants.EnsureSeededAsync(db);

        var builtIns = await db.AiAssistants.Where(a => a.IsBuiltIn).ToListAsync();
        builtIns.Should().HaveCount(5);
        builtIns.Select(a => a.Name).Should().Contain(BarcodeName);

        var barcode = builtIns.Single(a => a.Name == BarcodeName);
        barcode.Category.Should().Be("Advisory");
        barcode.Temperature.Should().Be(0.2, "the carrier choice follows the deterministic decision table, not free judgment");
        barcode.SystemPrompt.Should().Contain("DETERMINISTIC DECISION TABLE");
        barcode.SystemPrompt.Should().Contain("not legal, regulatory, or compliance advice", "the required footer must be present");
    }

    [Fact]
    public async Task Existing_install_gets_only_the_missing_built_in_no_duplicates()
    {
        await using var db = TestDbContextFactory.Create();
        // Simulate an install seeded before the Barcode assistant existed: the 4 prior built-ins present.
        db.AiAssistants.AddRange(
            new AiAssistant { Name = "General Assistant", IsBuiltIn = true },
            new AiAssistant { Name = "HR Assistant", IsBuiltIn = true },
            new AiAssistant { Name = "Procurement Assistant", IsBuiltIn = true },
            new AiAssistant { Name = "Sales & Marketing Assistant", IsBuiltIn = true });
        await db.SaveChangesAsync();

        await SeedAiAssistants.EnsureSeededAsync(db);

        var builtIns = await db.AiAssistants.Where(a => a.IsBuiltIn).ToListAsync();
        builtIns.Should().HaveCount(5, "only the one missing built-in is added");
        builtIns.Count(a => a.Name == "General Assistant").Should().Be(1, "existing built-ins are not duplicated");
        builtIns.Should().ContainSingle(a => a.Name == BarcodeName);
    }

    [Fact]
    public async Task Running_twice_is_a_no_op_the_second_time()
    {
        await using var db = TestDbContextFactory.Create();
        await SeedAiAssistants.EnsureSeededAsync(db);
        await SeedAiAssistants.EnsureSeededAsync(db);

        (await db.AiAssistants.CountAsync(a => a.IsBuiltIn)).Should().Be(5);
    }
}
