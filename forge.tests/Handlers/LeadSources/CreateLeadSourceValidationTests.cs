using FluentAssertions;
using FluentValidation;

using Forge.Api.Features.LeadSources;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.LeadSources;

public class CreateLeadSourceValidationTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    [Theory]
    [InlineData("   ", "WEB", "name")]
    [InlineData("Website", "  ", "code")]
    public async Task Blank_required_fields_are_field_validation_errors(string name, string code, string field)
    {
        var act = () => new CreateLeadSourceHandler(_db).Handle(
            new CreateLeadSourceCommand(new CreateLeadSourceRequest(name, code, null)), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == field);
    }

    [Fact]
    public async Task Duplicate_code_is_a_field_validation_error()
    {
        _db.LeadSources.Add(new LeadSource { Name = "Website", Code = "WEB", IsActive = true });
        await _db.SaveChangesAsync();

        var act = () => new CreateLeadSourceHandler(_db).Handle(
            new CreateLeadSourceCommand(new CreateLeadSourceRequest("Web form", " WEB ", null)), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "code");
    }
}
