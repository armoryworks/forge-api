using FluentAssertions;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class CreateContactInteractionHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly CreateContactInteractionHandler _handler;

    private const int TestUserId = 1;

    public CreateContactInteractionHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _db.CurrentUserId = TestUserId;
        _handler = new CreateContactInteractionHandler(_db);
    }

    // CurrentUserId is set on the AppDbContext directly — middleware does
    // this in production. Tests just assign the field and update it after
    // seeding a user so the handler picks up the right principal.
    private void SetupCurrentUser(int userId) => _db.CurrentUserId = userId;

    private async Task<(ApplicationUser user, Customer customer, Contact contact)> SeedData()
    {
        var user = new ApplicationUser
        {
            UserName = "user@test.com", Email = "user@test.com",
            FirstName = "Test", LastName = "User", Initials = "TU", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        SetupCurrentUser(user.Id);

        var customer = new Customer { Name = "Acme Corp" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var contact = new Contact
        {
            CustomerId = customer.Id,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@acme.com",
            IsPrimary = true,
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();

        return (user, customer, contact);
    }

    [Fact]
    public async Task Handle_WithContactId_CreatesInteraction()
    {
        var (user, customer, contact) = await SeedData();

        var command = new CreateContactInteractionCommand(
            customer.Id, contact.Id, "Call", "Follow-up call",
            "Discussed project timeline", DateTimeOffset.UtcNow, 30);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Subject.Should().Be("Follow-up call");
        result.Type.Should().Be("Call");
        result.DurationMinutes.Should().Be(30);
        result.ContactName.Should().Contain("Doe");
    }

    [Fact]
    public async Task Handle_WithoutContactId_UsesPrimaryContact()
    {
        var (user, customer, contact) = await SeedData();

        var command = new CreateContactInteractionCommand(
            customer.Id, null, "Email", "Quote follow-up",
            null, DateTimeOffset.UtcNow, null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.ContactId.Should().Be(contact.Id);
        result.ContactName.Should().Contain("Doe");
    }

    [Fact]
    public async Task Handle_InvalidContactId_ThrowsKeyNotFoundException()
    {
        var (user, customer, _) = await SeedData();

        var command = new CreateContactInteractionCommand(
            customer.Id, 9999, "Call", "Test", null, DateTimeOffset.UtcNow, null);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_NoContacts_ThrowsKeyNotFoundException()
    {
        var user = new ApplicationUser
        {
            UserName = "u2@test.com", Email = "u2@test.com",
            FirstName = "No", LastName = "Contacts", Initials = "NC", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        SetupCurrentUser(user.Id);

        var customer = new Customer { Name = "Empty Co" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var command = new CreateContactInteractionCommand(
            customer.Id, null, "Note", "Test", null, DateTimeOffset.UtcNow, null);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
