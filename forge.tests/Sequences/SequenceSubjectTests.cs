using FluentAssertions;
using FluentValidation;
using MediatR;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Sequences;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Tests.Sequences;

public class SequenceSubjectTests
{
    private const int U = SequenceHandlerFixture.UserId;

    private static async Task<SequenceDefinitionResponseModel> Publish(SequenceHandlerFixture f, string? subjectType, bool autoStart = false, int? maxDwell = null)
    {
        var model = new SequenceDefinitionRequestModel("subject-test", "Subject test", null, subjectType,
            [new("a", "A", null, 0, MaxDwellMinutes: maxDwell)], [], [], AutoStartOnSubjectCreate: autoStart);
        var d = await new CreateSequenceDefinitionHandler(f.Db).Handle(new CreateSequenceDefinitionCommand(model), default);
        await new PublishSequenceDefinitionHandler(f.Db, f.Clock).Handle(new PublishSequenceDefinitionCommand(d.Id, U), default);
        return d;
    }

    private static Task<SequenceInstanceResponseModel> Start(SequenceHandlerFixture f, int defId, string? type, int? id) =>
        new StartSequenceInstanceHandler(f.Db, f.Evaluation, f.Clock)
            .Handle(new StartSequenceInstanceCommand(new StartSequenceRequestModel(defId, null, type, id), U), default);

    [Fact]
    public async Task A_known_subject_type_is_stored_in_its_canonical_spelling()
    {
        await using var f = new SequenceHandlerFixture();

        var d = await Publish(f, " job ");

        d.SubjectEntityType.Should().Be("Job");
    }

    [Fact]
    public async Task An_unknown_subject_type_is_kept_as_typed()
    {
        await using var f = new SequenceHandlerFixture();

        var d = await Publish(f, " Lot ");

        d.SubjectEntityType.Should().Be("Lot");
    }

    [Fact]
    public async Task Start_defaults_the_subject_type_from_the_definition()
    {
        await using var f = new SequenceHandlerFixture();
        await f.EnsureJobAsync(3);
        var d = await Publish(f, "Job");

        var i = await Start(f, d.Id, null, 3);

        i.SubjectEntityType.Should().Be("Job");
        i.SubjectEntityId.Should().Be(3);
    }

    [Fact]
    public async Task Start_matches_the_definitions_subject_type_in_any_case()
    {
        await using var f = new SequenceHandlerFixture();
        await f.EnsureJobAsync(3);
        var d = await Publish(f, "Job");

        var i = await Start(f, d.Id, "JOB", 3);

        i.SubjectEntityType.Should().Be("Job");
    }

    [Fact]
    public async Task Start_needs_a_subject_id_when_the_definition_has_a_subject_type()
    {
        await using var f = new SequenceHandlerFixture();
        var d = await Publish(f, "Job");

        var act = () => Start(f, d.Id, "Job", null);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Start_refuses_a_subject_id_without_a_type()
    {
        await using var f = new SequenceHandlerFixture();
        var d = await Publish(f, null);

        var act = () => Start(f, d.Id, null, 5);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Start_without_any_subject_is_allowed_when_the_definition_has_none()
    {
        await using var f = new SequenceHandlerFixture();
        var d = await Publish(f, null);

        var i = await Start(f, d.Id, null, null);

        i.SubjectEntityType.Should().BeNull();
        i.SubjectEntityId.Should().BeNull();
    }

    [Fact]
    public async Task Start_against_a_job_that_does_not_exist_is_not_found()
    {
        await using var f = new SequenceHandlerFixture();
        var d = await Publish(f, "Job");

        var act = () => Start(f, d.Id, "Job", 999);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_max_dwell_of_zero_or_less_is_refused(int minutes)
    {
        await using var f = new SequenceHandlerFixture();

        var act = () => Publish(f, null, maxDwell: minutes);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*max dwell*");
    }

    [Fact]
    public async Task A_definition_typed_in_lower_case_still_auto_starts_on_a_new_job()
    {
        await using var f = new SequenceHandlerFixture();
        await f.EnsureJobAsync(100);
        await Publish(f, "job", autoStart: true);
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<StartSequenceInstanceCommand>(), It.IsAny<CancellationToken>()))
            .Returns<StartSequenceInstanceCommand, CancellationToken>((c, ct) => new StartSequenceInstanceHandler(f.Db, f.Evaluation, f.Clock).Handle(c, ct));

        await new OnJobCreated_StartSequences(f.Db, mediator.Object).Handle(new JobCreatedEvent(100, U), default);

        f.Db.SequenceInstances.Should().ContainSingle(i => i.SubjectEntityType == "Job" && i.SubjectEntityId == 100);
    }
}
