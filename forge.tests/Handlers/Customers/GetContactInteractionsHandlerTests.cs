using FluentAssertions;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class GetContactInteractionsHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly GetContactInteractionsHandler _handler;

    public GetContactInteractionsHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetContactInteractionsHandler(_db);
    }

    private async Task<(Customer customer, Contact contact1, Contact contact2, ApplicationUser user)> SeedData()
    {
        var user = new ApplicationUser
        {
            UserName = "u@test.com", Email = "u@test.com",
            FirstName = "Test", LastName = "User", Initials = "TU", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var c1 = new Contact { CustomerId = customer.Id, FirstName = "John", LastName = "Doe", IsPrimary = true };
        var c2 = new Contact { CustomerId = customer.Id, FirstName = "Jane", LastName = "Smith" };
        _db.Contacts.AddRange(c1, c2);
        await _db.SaveChangesAsync();

        return (customer, c1, c2, user);
    }

    [Fact]
    public async Task Handle_ReturnsInteractionsForCustomer()
    {
        var (customer, c1, c2, user) = await SeedData();

        _db.ContactInteractions.AddRange(
            new ContactInteraction { ContactId = c1.Id, UserId = user.Id, Type = InteractionType.Call, Subject = "Call 1", InteractionDate = DateTimeOffset.UtcNow.AddDays(-1) },
            new ContactInteraction { ContactId = c2.Id, UserId = user.Id, Type = InteractionType.Email, Subject = "Email 1", InteractionDate = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetContactInteractionsQuery(customer.Id, null), CancellationToken.None);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_FiltersByContactId()
    {
        var (customer, c1, c2, user) = await SeedData();

        _db.ContactInteractions.AddRange(
            new ContactInteraction { ContactId = c1.Id, UserId = user.Id, Type = InteractionType.Call, Subject = "For C1", InteractionDate = DateTimeOffset.UtcNow },
            new ContactInteraction { ContactId = c2.Id, UserId = user.Id, Type = InteractionType.Email, Subject = "For C2", InteractionDate = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetContactInteractionsQuery(customer.Id, c1.Id), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Subject.Should().Be("For C1");
    }

    [Fact]
    public async Task Handle_OrdersByInteractionDateDescending()
    {
        var (customer, c1, _, user) = await SeedData();

        _db.ContactInteractions.AddRange(
            new ContactInteraction { ContactId = c1.Id, UserId = user.Id, Type = InteractionType.Note, Subject = "Older", InteractionDate = DateTimeOffset.UtcNow.AddDays(-5) },
            new ContactInteraction { ContactId = c1.Id, UserId = user.Id, Type = InteractionType.Note, Subject = "Newer", InteractionDate = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetContactInteractionsQuery(customer.Id, null), CancellationToken.None);

        result[0].Subject.Should().Be("Newer");
        result[1].Subject.Should().Be("Older");
    }
}
