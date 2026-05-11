using QBEngineer.Api.Services;

namespace QBEngineer.Tests.Services;

/// <summary>
/// Pro Services rollout — unit tests for the folder-path token resolver.
/// Verifies substitution, standard-date defaults, sanitization, and
/// graceful handling of unmatched tokens.
/// </summary>
public class FolderPathResolverTests
{
    private readonly FolderPathResolver _resolver = new();

    [Fact]
    public void Substitutes_Customer_Token()
    {
        var result = _resolver.Resolve(
            "/Clients/{Customer}/",
            new Dictionary<string, string> { ["Customer"] = "ACME" });
        Assert.Equal("/Clients/ACME/", result);
    }

    [Fact]
    public void Substitutes_Multiple_Tokens()
    {
        var result = _resolver.Resolve(
            "/Clients/{Customer}/Tasks/{Job}/",
            new Dictionary<string, string>
            {
                ["Customer"] = "ACME",
                ["Job"] = "TASK-1042",
            });
        Assert.Equal("/Clients/ACME/Tasks/TASK-1042/", result);
    }

    [Fact]
    public void Sanitizes_Slashes_In_Token_Values()
    {
        var result = _resolver.Resolve(
            "/Clients/{Customer}/",
            new Dictionary<string, string> { ["Customer"] = "ACME / Inc" });
        Assert.Equal("/Clients/ACME - Inc/", result);
        Assert.DoesNotContain("ACME/Inc", result);
    }

    [Fact]
    public void Sanitizes_Backslashes_In_Token_Values()
    {
        var result = _resolver.Resolve(
            "/Clients/{Customer}/",
            new Dictionary<string, string> { ["Customer"] = "ACME\\Industries" });
        Assert.Equal("/Clients/ACME-Industries/", result);
    }

    [Fact]
    public void Collapses_Internal_Whitespace_Runs()
    {
        var result = _resolver.Resolve(
            "/Clients/{Customer}/",
            new Dictionary<string, string> { ["Customer"] = "ACME    Industries" });
        Assert.Equal("/Clients/ACME Industries/", result);
    }

    [Fact]
    public void Fills_Year_Token_From_Utcnow_When_Not_Provided()
    {
        var result = _resolver.Resolve("/Year-{Year}/", null);
        var expected = $"/Year-{DateTimeOffset.UtcNow.Year}/";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Fills_Month_Token_As_Two_Digit()
    {
        var result = _resolver.Resolve("/Month-{Month}/", null);
        Assert.Matches(@"/Month-\d{2}/", result);
    }

    [Fact]
    public void Fills_Quarter_Token_As_Q1_Q4()
    {
        var result = _resolver.Resolve("/Q-{Quarter}/", null);
        Assert.Matches(@"/Q-Q[1-4]/", result);
    }

    [Fact]
    public void Caller_Can_Override_Default_Date_Tokens()
    {
        var result = _resolver.Resolve(
            "/Year-{Year}/",
            new Dictionary<string, string> { ["Year"] = "2099" });
        Assert.Equal("/Year-2099/", result);
    }

    [Fact]
    public void Token_Matching_Is_Case_Insensitive()
    {
        var result = _resolver.Resolve(
            "/Clients/{customer}/",  // lowercase token
            new Dictionary<string, string> { ["Customer"] = "ACME" });  // PascalCase key
        Assert.Equal("/Clients/ACME/", result);
    }

    [Fact]
    public void Unmatched_Tokens_Stay_Literal()
    {
        var result = _resolver.Resolve(
            "/Clients/{Customer}/Bogus/{Unknown}/",
            new Dictionary<string, string> { ["Customer"] = "ACME" });
        Assert.Equal("/Clients/ACME/Bogus/{Unknown}/", result);
    }

    [Fact]
    public void Empty_Template_Returns_Empty_String()
    {
        Assert.Equal(string.Empty, _resolver.Resolve(string.Empty));
    }

    [Fact]
    public void Empty_Context_Still_Fills_Date_Tokens()
    {
        var result = _resolver.Resolve("/{Year}/{Month}/{Quarter}/", new Dictionary<string, string>());
        Assert.Matches(@"/\d{4}/\d{2}/Q[1-4]/", result);
    }
}
