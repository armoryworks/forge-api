using FluentAssertions;

using Forge.Api.Features.Mobile;

namespace Forge.Tests.Handlers.Mobile;

public class ReportProblemValidatorTests
{
    private static ReportProblemCommand Command(string message, string? screen = null) =>
        new(new ReportProblemRequestModel(message, screen, "1.0.0", "android"), "device-1");

    [Fact]
    public void Accepts_a_plain_message()
    {
        new ReportProblemValidator().Validate(Command("Scanner freezes after torch toggle", "/app/scan"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_an_empty_message()
    {
        new ReportProblemValidator().Validate(Command(""))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Caps_the_message_at_two_thousand_characters()
    {
        new ReportProblemValidator().Validate(Command(new string('x', 2001)))
            .IsValid.Should().BeFalse();
        new ReportProblemValidator().Validate(Command(new string('x', 2000)))
            .IsValid.Should().BeTrue();
    }
}
