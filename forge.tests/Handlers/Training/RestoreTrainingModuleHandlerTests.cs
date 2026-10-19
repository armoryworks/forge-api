using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Training;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Training;

public class RestoreTrainingModuleHandlerTests
{
    private readonly Data.Context.AppDbContext _db = TestDbContextFactory.Create();

    private async Task<TrainingModule> SeedModule(string slug)
    {
        var module = new TrainingModule
        {
            Title = "Safety Basics",
            Slug = slug,
            Summary = "Overview",
            ContentType = TrainingContentType.Article,
            ContentJson = "{}",
            EstimatedMinutes = 10,
            IsPublished = true,
        };
        _db.TrainingModules.Add(module);
        await _db.SaveChangesAsync();
        return module;
    }

    [Fact]
    public async Task DeleteThenRestore_BringsTheModuleBack()
    {
        var module = await SeedModule("safety-basics");

        await new DeleteTrainingModuleHandler(_db).Handle(new DeleteTrainingModuleCommand(module.Id), CancellationToken.None);
        (await _db.TrainingModules.AnyAsync(m => m.Id == module.Id)).Should().BeFalse();

        await new RestoreTrainingModuleHandler(_db).Handle(new RestoreTrainingModuleCommand(module.Id), CancellationToken.None);

        (await _db.TrainingModules.AnyAsync(m => m.Id == module.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Restore_WhenSlugReused_Throws()
    {
        var module = await SeedModule("dup-slug");
        await new DeleteTrainingModuleHandler(_db).Handle(new DeleteTrainingModuleCommand(module.Id), CancellationToken.None);
        await SeedModule("dup-slug");

        var act = () => new RestoreTrainingModuleHandler(_db).Handle(new RestoreTrainingModuleCommand(module.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Restore_UnknownModule_ThrowsNotFound()
    {
        var act = () => new RestoreTrainingModuleHandler(_db).Handle(new RestoreTrainingModuleCommand(999_999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
