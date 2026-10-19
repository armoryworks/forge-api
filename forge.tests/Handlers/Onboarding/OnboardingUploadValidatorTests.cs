using FluentAssertions;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Onboarding;

namespace Forge.Tests.Handlers.Onboarding;

public class OnboardingUploadValidatorTests
{
    private static IFormFile NamedFile(string fileName)
    {
        var bytes = new byte[] { 1, 2, 3 };
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
    }

    [Theory]
    [InlineData("passport.pdf", true)]
    [InlineData("license.heic", true)]
    [InlineData("card.jpg", true)]
    [InlineData("setup.exe", false)]
    [InlineData("scan.pdf.exe", false)]
    [InlineData("page.html", false)]
    public void I9Document_RefusesExecutablesAndScripts(string fileName, bool valid)
    {
        new UploadI9DocumentValidator()
            .Validate(new UploadI9DocumentCommand(5, NamedFile(fileName), "A"))
            .IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("check.png", true)]
    [InlineData("check.pdf", true)]
    [InlineData("check.bat", false)]
    [InlineData("check.ps1 ", false)]
    public void VoidedCheck_RefusesExecutablesAndScripts(string fileName, bool valid)
    {
        new UploadVoidedCheckValidator()
            .Validate(new UploadVoidedCheckCommand(5, NamedFile(fileName)))
            .IsValid.Should().Be(valid);
    }
}
