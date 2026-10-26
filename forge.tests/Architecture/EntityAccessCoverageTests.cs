using System.Reflection;
using System.Text.RegularExpressions;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

using Forge.Api.Authorization;
using Forge.Api.Controllers;
using Forge.Api.Features.Files;

namespace Forge.Tests.Architecture;

/// <summary>
/// The files, activity and preview endpoints take the entity type from the URL and ask
/// <see cref="EntityAccessRules"/> who may see it. An entity type with no rule is denied, so
/// these tests make sure every type those endpoints can meet has one, and that each rule's role
/// set still equals the class-level <c>[Authorize]</c> of the controller that owns the entity.
/// A new entity type, or a role change on its controller, fails here instead of shipping open
/// or shutting real users out.
/// </summary>
public sealed partial class EntityAccessCoverageTests
{
    private static readonly Dictionary<string, Type[]> OwningControllers = new()
    {
        ["job"] = [typeof(JobsController), typeof(JobListController)],
        ["part"] = [typeof(PartsController), typeof(PartListController)],
        ["operation"] = [typeof(PartsController), typeof(PartListController)],
        ["asset"] = [typeof(AssetsController)],
        ["lead"] = [typeof(LeadsController)],
        ["customer"] = [typeof(CustomersController)],
        ["salesorder"] = [typeof(SalesOrdersController), typeof(SalesOrdersListController)],
        ["quote"] = [typeof(QuotesController)],
        ["invoice"] = [typeof(InvoicesController)],
        ["shipment"] = [typeof(ShipmentsController)],
        ["payment"] = [typeof(PaymentsController)],
        ["customerreturn"] = [typeof(CustomerReturnsController)],
        ["creditmemo"] = [typeof(CreditMemosController), typeof(CreditMemoReadsController)],
        ["purchaseorder"] = [typeof(PurchaseOrdersController)],
        ["vendor"] = [typeof(VendorsController)],
        ["vendorbill"] = [typeof(VendorBillsController), typeof(VendorBillReadsController)],
        ["vendorpayment"] = [typeof(VendorPaymentsController), typeof(VendorPaymentReadsController)],
        ["vendorcreditmemo"] = [typeof(VendorCreditMemosController), typeof(VendorCreditMemoReadsController)],
        ["vendorreturn"] = [typeof(VendorReturnsController)],
        ["paymentrun"] = [typeof(PaymentRunsController)],
        ["lot"] = [typeof(LotsController)],
        ["qcinspection"] = [typeof(QualityController)],
        ["nonconformance"] = [typeof(NcrCapaController)],
        ["ppapelement"] = [typeof(PpapController)],
        ["receivinginspectiontemplate"] = [typeof(ReceivingInspectionTemplatesController)],
        ["employee"] = [typeof(EmployeesController)],
    };

    private static readonly HashSet<string> HrKeys =
    [
        "expense", "paystub", "taxdocument", "identitydoc", "identitydocument", "employeedoc",
        "directdeposit", "user", "compliancesubmission",
    ];

    [Fact]
    public void Every_uploadable_entity_type_has_an_access_rule()
    {
        FileEntityTypes.Valid.Where(t => EntityAccessRules.Find(t) is null)
            .Should().BeEmpty("an upload type with no rule would 403 every upload and download");
    }

    [Fact]
    public void Every_entity_type_written_onto_a_file_attachment_has_an_access_rule()
    {
        var literals = RepoRoot.SourceFiles("forge.api")
            .SelectMany(f => FileAttachmentEntityType().Matches(File.ReadAllText(f.FullPath))
                .Select(m => (f.RelativePath, Type: m.Groups[1].Value)))
            .ToList();

        literals.Should().NotBeEmpty("the scan must find the chat, onboarding and compliance attachment sites");
        literals.Where(l => EntityAccessRules.Find(l.Type) is null)
            .Select(l => $"{l.RelativePath}: \"{l.Type}\"")
            .Should().BeEmpty("a stored attachment type with no rule can never be downloaded again");
    }

    [Fact]
    public void Every_activity_entity_type_has_an_access_rule()
    {
        var allowed = (HashSet<string>)typeof(EntityActivityController)
            .GetField("AllowedEntityTypes", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        allowed.Should().NotBeEmpty();
        allowed.Where(t => EntityAccessRules.Find(t) is null).Should().BeEmpty();
    }

    [Fact]
    public void Every_preview_type_has_an_access_rule()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Path, "forge.api/Features/Preview/GetEntityPreview.cs"));
        var types = PreviewType().Matches(source).Select(m => m.Groups[1].Value).ToList();

        types.Should().Contain(["customer", "invoice", "part"]);
        types.Where(t => EntityAccessRules.Find(t) is null).Should().BeEmpty();
    }

    [Fact]
    public void Every_role_based_rule_matches_its_owning_controllers()
    {
        foreach (var (key, controllers) in OwningControllers)
        {
            var rule = EntityAccessRules.All.Should().ContainKey(key).WhoseValue;
            var classRoles = controllers.Select(c => c.GetCustomAttribute<AuthorizeAttribute>()?.Roles).ToList();

            if (classRoles.Any(string.IsNullOrEmpty))
            {
                rule.Roles.Should().BeNull($"{key}: an owning controller admits any signed-in user");
                continue;
            }

            var expected = classRoles.SelectMany(r => r!.Split(',', StringSplitOptions.TrimEntries)).ToHashSet();
            rule.Roles.Should().BeEquivalentTo(expected, $"{key} must mirror {string.Join(", ", controllers.Select(c => c.Name))}");
        }
    }

    [Fact]
    public void Every_rule_is_either_controller_backed_or_an_owned_record()
    {
        EntityAccessRules.All.Keys
            .Where(k => !OwningControllers.ContainsKey(k) && !HrKeys.Contains(k) && k is not ("chat" or "compliancetemplate"))
            .Should().BeEmpty("add the owning controller to this test when a rule is added");
    }

    [Fact]
    public void Hr_records_follow_the_payroll_and_expense_approver_roles()
    {
        var payroll = typeof(PayrollController).GetMethod(nameof(PayrollController.GetUserPayStubs))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles;
        var approver = typeof(ExpensesController).GetMethod(nameof(ExpensesController.UpdateExpenseStatus))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles;

        EntityAccessRules.HrRoles.Should().Be(payroll).And.Be(approver);
        foreach (var key in HrKeys)
            EntityAccessRules.All[key].Roles.Should().BeEquivalentTo(
                EntityAccessRules.HrRoles.Split(','), key);
    }

    [Theory]
    [InlineData("sales-orders", "salesorder")]
    [InlineData("SalesOrder", "salesorder")]
    [InlineData("sales-order", "salesorder")]
    [InlineData("compliance_submissions", "compliancesubmission")]
    [InlineData("identity-documents", "identitydocument")]
    [InlineData("chat", "chat")]
    public void Spellings_fold_to_one_key(string entityType, string key)
    {
        EntityAccessRules.Normalize(entityType).Should().Be(key);
    }

    [GeneratedRegex(@"new FileAttachment\b[^}]*?EntityType\s*=\s*""([^""]+)""", RegexOptions.Singleline)]
    private static partial Regex FileAttachmentEntityType();

    [GeneratedRegex(@"""([a-z-]+)""\s*=>\s*await")]
    private static partial Regex PreviewType();
}
