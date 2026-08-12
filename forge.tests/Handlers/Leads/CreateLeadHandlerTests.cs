using Bogus;
using FluentAssertions;
using Moq;

using Forge.Api.Features.Leads;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leads;

public class CreateLeadHandlerTests
{
    private readonly Mock<ILeadRepository> _leadRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CreateLeadHandler _handler;

    private readonly Faker _faker = new();
    private readonly int _userId;

    public CreateLeadHandlerTests()
    {
        _userId = _faker.Random.Int(1, 100);
        _db.CurrentUserId = _userId;
        _handler = new CreateLeadHandler(_leadRepo.Object, _db);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesLeadAndReturnsResponse()
    {
        // Arrange
        var companyName = _faker.Company.CompanyName();
        var contactName = _faker.Name.FullName();
        var email = _faker.Internet.Email();
        var phone = _faker.Phone.PhoneNumber();
        var source = "Website";
        var notes = _faker.Lorem.Sentence();
        var followUpDate = DateTime.UtcNow.AddDays(7);

        var requestModel = new CreateLeadRequestModel(
            companyName, contactName, email, phone, source, notes, followUpDate);

        var expectedResponse = new LeadResponseModel(
            1, companyName, contactName, email, phone, source,
            LeadStatus.New, notes, followUpDate, null, null,
            DateTime.UtcNow, DateTime.UtcNow);

        _leadRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateLeadCommand(requestModel);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Created.Should().BeTrue();
        result.Lead.CompanyName.Should().Be(companyName);
        result.Lead.ContactName.Should().Be(contactName);
        result.Lead.Email.Should().Be(email);

        _leadRepo.Verify(r => r.AddAsync(It.Is<Lead>(l =>
            l.CompanyName == companyName.Trim() &&
            l.ContactName == contactName.Trim() &&
            l.Email == email.Trim() &&
            l.Phone == phone.Trim() &&
            l.Source == source.Trim() &&
            l.Notes == notes.Trim() &&
            l.FollowUpDate == followUpDate &&
            l.CreatedBy == _userId
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IndividualLead_BlankCompany_CreatesWithContactOnly()
    {
        // Individual (no company) — company blank, contact present.
        var requestModel = new CreateLeadRequestModel(
            "", "Dana Rivers", "dana@example.com", null, "Referral", null, null);

        _leadRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeadResponseModel(
                1, "", "Dana Rivers", "dana@example.com", null, "Referral",
                LeadStatus.New, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow));

        await _handler.Handle(new CreateLeadCommand(requestModel), CancellationToken.None);

        _leadRepo.Verify(r => r.AddAsync(It.Is<Lead>(l =>
            l.CompanyName == "" &&
            l.ContactName == "Dana Rivers" &&
            l.DisplayName == "Dana Rivers"   // fallback for the individual
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TrimsWhitespace()
    {
        // Arrange
        var requestModel = new CreateLeadRequestModel(
            "  Acme Corp  ", "  John Doe  ", " john@acme.com ",
            " 555-1234 ", " Referral ", " Great lead ", null);

        var expectedResponse = new LeadResponseModel(
            1, "Acme Corp", "John Doe", "john@acme.com", "555-1234",
            "Referral", LeadStatus.New, "Great lead", null, null, null,
            DateTime.UtcNow, DateTime.UtcNow);

        _leadRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateLeadCommand(requestModel);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _leadRepo.Verify(r => r.AddAsync(It.Is<Lead>(l =>
            l.CompanyName == "Acme Corp" &&
            l.ContactName == "John Doe" &&
            l.Email == "john@acme.com" &&
            l.Phone == "555-1234" &&
            l.Source == "Referral" &&
            l.Notes == "Great lead"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NullOptionalFields_SetsNullValues()
    {
        // Arrange
        var companyName = _faker.Company.CompanyName();
        var requestModel = new CreateLeadRequestModel(
            companyName, null, null, null, null, null, null);

        var expectedResponse = new LeadResponseModel(
            1, companyName, null, null, null, null,
            LeadStatus.New, null, null, null, null,
            DateTime.UtcNow, DateTime.UtcNow);

        _leadRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateLeadCommand(requestModel);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Lead.ContactName.Should().BeNull();
        result.Lead.Email.Should().BeNull();
        result.Lead.Phone.Should().BeNull();
        result.Lead.Source.Should().BeNull();
        result.Lead.Notes.Should().BeNull();
        result.Lead.FollowUpDate.Should().BeNull();

        _leadRepo.Verify(r => r.AddAsync(It.Is<Lead>(l =>
            l.ContactName == null &&
            l.Email == null &&
            l.Phone == null &&
            l.Source == null &&
            l.Notes == null &&
            l.FollowUpDate == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SetsCreatedByFromCurrentUser()
    {
        // Arrange
        var requestModel = new CreateLeadRequestModel(
            "Test Company", null, null, null, null, null, null);

        var expectedResponse = new LeadResponseModel(
            1, "Test Company", null, null, null, null,
            LeadStatus.New, null, null, null, null,
            DateTime.UtcNow, DateTime.UtcNow);

        _leadRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateLeadCommand(requestModel);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _leadRepo.Verify(r => r.AddAsync(It.Is<Lead>(l =>
            l.CreatedBy == _userId
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsResultFromRepository()
    {
        // Arrange
        var requestModel = new CreateLeadRequestModel(
            "Test Corp", "Jane Smith", "jane@test.com", null, null, null, null);

        var expectedResponse = new LeadResponseModel(
            42, "Test Corp", "Jane Smith", "jane@test.com", null, null,
            LeadStatus.New, null, null, null, null,
            DateTime.UtcNow, DateTime.UtcNow);

        _leadRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateLeadCommand(requestModel);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Lead.Should().Be(expectedResponse);
        result.Lead.Id.Should().Be(42);
        result.Created.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExternalId_TrimsAndPersists()
    {
        // Intake relays stamp their submission id as the idempotency key.
        var requestModel = new CreateLeadRequestModel(
            "Acme Corp", null, null, null, null, null, null,
            ExternalId: "  tuyere:sub-123  ");

        _leadRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeadResponseModel(
                1, "Acme Corp", null, null, null, null,
                LeadStatus.New, null, null, null, null,
                DateTime.UtcNow, DateTime.UtcNow, ExternalId: "tuyere:sub-123"));

        var result = await _handler.Handle(new CreateLeadCommand(requestModel), CancellationToken.None);

        result.Created.Should().BeTrue();
        _leadRepo.Verify(r => r.AddAsync(It.Is<Lead>(l =>
            l.ExternalId == "tuyere:sub-123"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateExternalId_ReturnsExistingLeadWithoutCreating()
    {
        // A retried intake POST (timeout / 5xx on the first attempt) must not
        // duplicate the lead — the ExternalId finds the original.
        _db.Leads.Add(new Lead
        {
            Id = 7,
            CompanyName = "Acme Corp",
            ExternalId = "tuyere:sub-123",
            CreatedBy = _userId,
        });
        await _db.SaveChangesAsync();

        var existingResponse = new LeadResponseModel(
            7, "Acme Corp", null, null, null, null,
            LeadStatus.New, null, null, null, null,
            DateTime.UtcNow, DateTime.UtcNow, ExternalId: "tuyere:sub-123");
        _leadRepo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingResponse);

        var requestModel = new CreateLeadRequestModel(
            "Acme Corp", null, null, null, null, null, null,
            ExternalId: "tuyere:sub-123");

        var result = await _handler.Handle(new CreateLeadCommand(requestModel), CancellationToken.None);

        result.Created.Should().BeFalse("the externalId replay must be idempotent, not a duplicate insert");
        result.Lead.Id.Should().Be(7);
        _leadRepo.Verify(r => r.AddAsync(It.IsAny<Lead>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LeadSourceCode_ResolvesToLeadSourceId()
    {
        _db.LeadSources.Add(new LeadSource
        {
            Id = 5,
            Name = "ArmoryWorks Website",
            Code = "armoryworks.com",
        });
        await _db.SaveChangesAsync();

        var requestModel = new CreateLeadRequestModel(
            "Acme Corp", null, null, null, null, null, null,
            LeadSourceCode: "armoryworks.com");

        _leadRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeadResponseModel(
                1, "Acme Corp", null, null, null, null,
                LeadStatus.New, null, null, null, null,
                DateTime.UtcNow, DateTime.UtcNow, LeadSourceId: 5));

        await _handler.Handle(new CreateLeadCommand(requestModel), CancellationToken.None);

        _leadRepo.Verify(r => r.AddAsync(It.Is<Lead>(l =>
            l.LeadSourceId == 5
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownLeadSourceCode_LeavesLeadSourceIdNull()
    {
        // Unknown code degrades to null attribution — never fails the intake.
        var requestModel = new CreateLeadRequestModel(
            "Acme Corp", null, null, null, null, null, null,
            LeadSourceCode: "no-such-source");

        _leadRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeadResponseModel(
                1, "Acme Corp", null, null, null, null,
                LeadStatus.New, null, null, null, null,
                DateTime.UtcNow, DateTime.UtcNow));

        var result = await _handler.Handle(new CreateLeadCommand(requestModel), CancellationToken.None);

        result.Created.Should().BeTrue();
        _leadRepo.Verify(r => r.AddAsync(It.Is<Lead>(l =>
            l.LeadSourceId == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
