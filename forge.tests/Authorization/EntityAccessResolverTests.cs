using System.Security.Claims;

using FluentAssertions;

using Forge.Api.Authorization;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Authorization;

/// <summary>
/// The role and owner rules behind the files, activity and preview endpoints: a role
/// outside the owning controller's set is refused, the person a private record belongs
/// to keeps access, and an entity type with no rule is refused.
/// </summary>
public sealed class EntityAccessResolverTests
{
    private const int CallerId = 5;
    private const int OtherUserId = 9;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly EntityAccessResolver _resolver;

    public EntityAccessResolverTests() => _resolver = new EntityAccessResolver(_db);

    private static ClaimsPrincipal As(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Prepend(new Claim(ClaimTypes.NameIdentifier, CallerId.ToString())),
        "Test"));

    [Theory]
    [InlineData("invoices", "ProductionWorker", false)]
    [InlineData("Invoice", "OfficeManager", true)]
    [InlineData("sales-order", "Engineer", false)]
    [InlineData("SalesOrder", "PM", true)]
    [InlineData("jobs", "ProductionWorker", true)]
    [InlineData("parts", "ProductionWorker", true)]
    [InlineData("assets", "Engineer", true)]
    [InlineData("assets", "OfficeManager", false)]
    [InlineData("VendorBill", "Controller", true)]
    [InlineData("VendorBill", "Engineer", false)]
    [InlineData("QcInspection", "ProductionWorker", true)]
    public async Task Roles_follow_the_owning_controller(string entityType, string role, bool admitted)
    {
        (await _resolver.CanAccessAsync(entityType, 1, As(role), forWrite: false, CancellationToken.None))
            .Should().Be(admitted);
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("")]
    public async Task An_entity_type_without_a_rule_is_refused(string entityType)
    {
        (await _resolver.CanAccessAsync(entityType, 1, As("Admin"), forWrite: false, CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        (await _resolver.CanAccessAsync("jobs", 1, anonymous, forWrite: false, CancellationToken.None)).Should().BeFalse();
    }

    [Theory]
    [InlineData("pay-stubs")]
    [InlineData("tax-documents")]
    [InlineData("identity-docs")]
    [InlineData("employee-docs")]
    [InlineData("direct-deposit")]
    public async Task User_keyed_records_are_open_to_their_user_and_to_hr(string entityType)
    {
        (await _resolver.CanAccessAsync(entityType, CallerId, As("ProductionWorker"), false, CancellationToken.None))
            .Should().BeTrue("it is the caller's own record");
        (await _resolver.CanAccessAsync(entityType, OtherUserId, As("ProductionWorker"), false, CancellationToken.None))
            .Should().BeFalse("it is another user's record");
        (await _resolver.CanAccessAsync(entityType, OtherUserId, As("OfficeManager"), false, CancellationToken.None))
            .Should().BeTrue("payroll administration reads every user's records");
    }

    [Fact]
    public async Task An_expense_is_open_to_its_submitter_and_approvers_only()
    {
        var own = new Expense { UserId = CallerId, Description = "Mileage" };
        var other = new Expense { UserId = OtherUserId, Description = "Lunch" };
        _db.Expenses.AddRange(own, other);
        await _db.SaveChangesAsync();

        (await _resolver.CanAccessAsync("Expense", own.Id, As("Engineer"), false, CancellationToken.None)).Should().BeTrue();
        (await _resolver.CanAccessAsync("expenses", other.Id, As("Engineer"), false, CancellationToken.None)).Should().BeFalse();
        (await _resolver.CanAccessAsync("expenses", other.Id, As("Manager"), false, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task A_compliance_submission_is_open_to_its_employee()
    {
        var submission = new ComplianceFormSubmission { UserId = CallerId, TemplateId = 1 };
        _db.ComplianceFormSubmissions.Add(submission);
        await _db.SaveChangesAsync();

        (await _resolver.CanAccessAsync("compliance_submissions", submission.Id, As("ProductionWorker"), false, CancellationToken.None))
            .Should().BeTrue();
        (await _resolver.CanAccessAsync("compliance_submissions", submission.Id + 1, As("ProductionWorker"), false, CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Chat_files_follow_room_membership()
    {
        var room = new ChatRoom { Name = "floor", CreatedById = OtherUserId };
        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync();
        _db.ChatRoomMembers.Add(new ChatRoomMember { ChatRoomId = room.Id, UserId = CallerId });
        await _db.SaveChangesAsync();

        (await _resolver.CanAccessAsync("chat", room.Id, As("ProductionWorker"), false, CancellationToken.None)).Should().BeTrue();
        (await _resolver.CanAccessAsync("chat", room.Id + 1, As("Admin"), false, CancellationToken.None))
            .Should().BeFalse("a room's files are for its members, whatever the caller's role");
    }

    [Fact]
    public async Task Compliance_templates_are_read_by_everyone_and_changed_by_admin()
    {
        (await _resolver.CanAccessAsync("compliance-templates", 1, As("ProductionWorker"), false, CancellationToken.None)).Should().BeTrue();
        (await _resolver.CanAccessAsync("compliance-templates", 1, As("Manager"), true, CancellationToken.None)).Should().BeFalse();
        (await _resolver.CanAccessAsync("compliance-templates", 1, As("Admin"), true, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task A_pay_stub_pdf_is_open_to_the_user_it_belongs_to()
    {
        var file = new FileAttachment { EntityType = "pay-stubs", EntityId = OtherUserId, UploadedById = OtherUserId };
        _db.FileAttachments.Add(file);
        await _db.SaveChangesAsync();
        _db.PayStubs.Add(new PayStub { UserId = CallerId, FileAttachmentId = file.Id });
        await _db.SaveChangesAsync();

        (await _resolver.CanAccessFileAsync(file, As("ProductionWorker"), false, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task An_identity_document_upload_is_open_to_its_uploader_before_it_is_linked()
    {
        var file = new FileAttachment { EntityType = "identity-documents", EntityId = 0, UploadedById = CallerId };

        (await _resolver.CanAccessFileAsync(file, As("ProductionWorker"), false, CancellationToken.None)).Should().BeTrue();
        file.UploadedById = OtherUserId;
        (await _resolver.CanAccessFileAsync(file, As("ProductionWorker"), false, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Uploading_a_file_does_not_open_a_record_the_caller_cannot_see()
    {
        var file = new FileAttachment { EntityType = "invoices", EntityId = 3, UploadedById = CallerId };

        (await _resolver.CanAccessFileAsync(file, As("ProductionWorker"), false, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task A_files_required_role_still_applies()
    {
        var file = new FileAttachment { EntityType = "jobs", EntityId = 3, RequiredRole = "Manager" };

        (await _resolver.CanAccessFileAsync(file, As("Engineer"), false, CancellationToken.None)).Should().BeFalse();
        (await _resolver.CanAccessFileAsync(file, As("Manager"), false, CancellationToken.None)).Should().BeTrue();
    }
}
