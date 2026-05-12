using Bogus;
using FluentAssertions;
using Forge.Api.Features.Admin;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

public class CreateReferenceDataHandlerTests
{
    private readonly CreateReferenceDataHandler _handler;
    private readonly Data.Context.AppDbContext _db;
    private readonly Faker _faker = new();

    public CreateReferenceDataHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new CreateReferenceDataHandler(_db);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesEntryAndReturnsModel()
    {
        var code = _faker.Random.AlphaNumeric(8);
        var label = _faker.Commerce.ProductName();
        var command = new CreateReferenceDataCommand("test_group", code, label, 1, null, null, null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Code.Should().Be(code);
        result.Label.Should().Be(label);
        result.SortOrder.Should().Be(1);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DuplicateGroupAndCode_ThrowsInvalidOperationException()
    {
        var groupCode = "dup_group";
        var code = "dup_code";
        _db.ReferenceData.Add(new ReferenceData { GroupCode = groupCode, Code = code, Label = "Existing", SortOrder = 1 });
        await _db.SaveChangesAsync();

        var command = new CreateReferenceDataCommand(groupCode, code, "New Label", 2, null, null, null);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{code}'*already exists*'{groupCode}'*");
    }
}
