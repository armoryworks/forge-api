using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.DomainEvents;

public class InvoicePastDueFollowUpTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static IClock Clock()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return clock.Object;
    }

    private static async Task<(AppDbContext Db, Invoice Invoice)> SeedAsync(
        string invoiceNumber = "INV-00060", InvoiceStatus status = InvoiceStatus.Overdue, int daysOverdue = 31)
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        var customer = new Customer { Name = "Acme" };
        db.Set<Customer>().Add(customer);

        db.Roles.AddRange(
            new IdentityRole<int> { Id = 1, Name = "Admin" },
            new IdentityRole<int> { Id = 2, Name = "OfficeManager" });
        db.Users.AddRange(
            new ApplicationUser { Id = 5, UserName = "admin" },
            new ApplicationUser { Id = 8, UserName = "former-om", IsActive = false },
            new ApplicationUser { Id = 9, UserName = "om" });
        db.UserRoles.AddRange(
            new IdentityUserRole<int> { UserId = 5, RoleId = 1 },
            new IdentityUserRole<int> { UserId = 8, RoleId = 2 },
            new IdentityUserRole<int> { UserId = 9, RoleId = 2 });
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber, CustomerId = customer.Id, CurrencyId = 1, Status = status,
            InvoiceDate = Now.AddDays(-daysOverdue - 30), DueDate = Now.AddDays(-daysOverdue),
            Lines = [new InvoiceLine { Description = "Widgets", Quantity = 10, UnitPrice = 125m, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (db, invoice);
    }

    private static OnInvoicePastDue_CreateFollowUp Handler(AppDbContext db)
        => new(db, Clock(), NullLogger<OnInvoicePastDue_CreateFollowUp>.Instance);

    [Fact]
    public async Task FollowUp_StatesTheRealBalance_InTheInvoiceCurrency()
    {
        var (db, invoice) = await SeedAsync();

        await Handler(db).Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 31), CancellationToken.None);

        var task = await db.FollowUpTasks.SingleAsync();
        task.Description.Should().Contain("Balance due: $1,250.00.");
        task.Description.Should().NotContain("¤");
    }

    [Fact]
    public async Task FollowUp_GoesToAnActiveOfficeManagerBeforeAnAdmin()
    {
        var (db, invoice) = await SeedAsync();

        await Handler(db).Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 31), CancellationToken.None);

        (await db.FollowUpTasks.SingleAsync()).AssignedToUserId.Should().Be(9);
    }

    [Fact]
    public async Task SixtyDayFollowUp_IsNotSuppressedByAnInvoiceNumberContaining60()
    {
        var (db, invoice) = await SeedAsync("INV-00060");
        var handler = Handler(db);
        await handler.Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 31), CancellationToken.None);

        await handler.Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 61), CancellationToken.None);

        (await db.FollowUpTasks.Select(f => f.Title).ToListAsync()).Should().BeEquivalentTo(
            "Invoice INV-00060 past due — 30 days",
            "Invoice INV-00060 past due — 60 days");
    }

    [Fact]
    public async Task SameBucket_CreatesOneFollowUp()
    {
        var (db, invoice) = await SeedAsync();
        var handler = Handler(db);

        await handler.Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 31), CancellationToken.None);
        await handler.Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 45), CancellationToken.None);

        (await db.FollowUpTasks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Job_PublishesABucketItMissedTheBoundaryOf()
    {
        var (db, invoice) = await SeedAsync(daysOverdue: 45);
        var publisher = new Mock<IPublisher>();
        var job = new InvoicePastDueCheckJob(db, Clock(), publisher.Object, NullLogger<InvoicePastDueCheckJob>.Instance);

        await job.Execute(CancellationToken.None);

        publisher.Verify(p => p.Publish(
            It.Is<InvoicePastDueEvent>(e => e.InvoiceId == invoice.Id && e.DaysOverdue == 45),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Job_IgnoresDraftInvoices()
    {
        var (db, _) = await SeedAsync(status: InvoiceStatus.Draft);
        var publisher = new Mock<IPublisher>();
        var job = new InvoicePastDueCheckJob(db, Clock(), publisher.Object, NullLogger<InvoicePastDueCheckJob>.Instance);

        await job.Execute(CancellationToken.None);

        publisher.VerifyNoOtherCalls();
    }
}
