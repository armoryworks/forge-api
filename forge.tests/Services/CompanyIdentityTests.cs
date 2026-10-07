using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class CompanyIdentityTests
{
    private static async Task<AppDbContext> SeedAsync(params (string Key, string Value)[] rows)
    {
        var db = TestDbContextFactory.Create();
        foreach (var (key, value) in rows)
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<(string? FromDb, string? FromRepo)> ResolveBothAsync(AppDbContext db)
        => (await CompanyIdentity.GetCompanyNameAsync(db, CancellationToken.None),
            await CompanyIdentity.GetCompanyNameAsync(new SystemSettingRepository(db), CancellationToken.None));

    [Fact]
    public async Task Prefers_company_name_over_legacy_row()
    {
        using var db = await SeedAsync(("company.name", "Harbor Gear Works"), ("company_name", "Old Name"));

        var (fromDb, fromRepo) = await ResolveBothAsync(db);

        fromDb.Should().Be("Harbor Gear Works");
        fromRepo.Should().Be("Harbor Gear Works");
    }

    [Fact]
    public async Task Falls_back_to_legacy_row_when_company_name_is_blank()
    {
        using var db = await SeedAsync(("company.name", "   "), ("company_name", " Legacy Mold Co "));

        var (fromDb, fromRepo) = await ResolveBothAsync(db);

        fromDb.Should().Be("Legacy Mold Co");
        fromRepo.Should().Be("Legacy Mold Co");
    }

    [Fact]
    public async Task Returns_null_when_neither_row_holds_a_name()
    {
        using var db = await SeedAsync(("company.name", ""));

        var (fromDb, fromRepo) = await ResolveBothAsync(db);

        fromDb.Should().BeNull();
        fromRepo.Should().BeNull();
    }
}
