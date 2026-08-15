using FluentAssertions;

using Forge.Api.Services;

namespace Forge.Tests.Services;

/// <summary>DB-free coverage of the transfer service's pure helpers: exclude-glob semantics and
/// COPY-text row splitting (the dropped-column projection path).</summary>
public sealed class DatabaseTransferGlobTests
{
    [Fact]
    public void ExcludeGlobs_MatchQualifiedAndBareNames()
    {
        DatabaseTransferService.Matches("junk_*", "public", "junk_log").Should().BeTrue();
        DatabaseTransferService.Matches("public.junk_*", "public", "junk_log").Should().BeTrue();
        DatabaseTransferService.Matches("audit.junk_*", "public", "junk_log").Should().BeFalse();
        DatabaseTransferService.Matches("parents", "public", "children").Should().BeFalse();
        DatabaseTransferService.Matches("JUNK_*", "public", "junk_log").Should().BeTrue(); // case-insensitive
        DatabaseTransferService.MatchesAny(["a_*", "*_log"], "public", "junk_log").Should().BeTrue();
        DatabaseTransferService.MatchesAny([], "public", "junk_log").Should().BeFalse();
    }

    [Fact]
    public void SplitCopyLine_PreservesEscapedTabsInsideValues()
    {
        // COPY text escapes an in-value tab as the two characters \t — never a literal tab.
        DatabaseTransferService.SplitCopyLine("1\thas \\t inside\t\\N", 3)
            .Should().Equal("1", @"has \t inside", @"\N");
    }

    [Fact]
    public void SplitCopyLine_LastFieldKeepsTrailingContent()
    {
        DatabaseTransferService.SplitCopyLine("a\tb\tc\td", 3).Should().Equal("a", "b", "c\td");
    }
}
