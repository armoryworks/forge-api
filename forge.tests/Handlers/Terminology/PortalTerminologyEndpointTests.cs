using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Controllers;
using Forge.Api.Features.Terminology;
using Forge.Core.Entities;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Terminology;

public class PortalTerminologyEndpointTests
{
    [Fact]
    public void PortalTerminology_IsAnonymousGetUnderThePortalRoute()
    {
        var method = typeof(CustomerPortalController).GetMethod(nameof(CustomerPortalController.Terminology))!;

        method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("terminology");
        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        typeof(CustomerPortalController).GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v1/portal");
    }

    [Fact]
    public async Task PortalTerminology_ReturnsOnlyCatalogTermKeys()
    {
        var db = TestDbContextFactory.Create();
        db.TerminologyEntries.AddRange(
            new TerminologyEntry { Key = "entity_job", Label = "Work Order", IsAdminEdited = true },
            new TerminologyEntry { Key = "label_jobs:es", Label = "Órdenes de trabajo", IsAdminEdited = true, SourcePresetId = TerminologyEntry.MachineTranslationSource },
            new TerminologyEntry { Key = "gender_job:es", Label = "f", IsAdminEdited = true },
            new TerminologyEntry { Key = "entity_quote", Label = "", IsAdminEdited = true },
            new TerminologyEntry { Key = "status_shipped", Label = "Done", SourcePresetId = "PRESET-08" },
            new TerminologyEntry { Key = "entity_track_type", Label = "Task Type", SourcePresetId = "PRESET-08" },
            new TerminologyEntry { Key = "internal_note", Label = "secret", IsAdminEdited = true });
        await db.SaveChangesAsync();

        var result = await new GetPortalTerminologyHandler(new TerminologyRepository(db))
            .Handle(new GetPortalTerminologyQuery(), CancellationToken.None);

        result.Select(e => e.Key).Should().BeEquivalentTo("entity_job", "label_jobs:es", "gender_job:es");
        result.Should().OnlyContain(e => !e.IsMachineTranslated);
    }
}
