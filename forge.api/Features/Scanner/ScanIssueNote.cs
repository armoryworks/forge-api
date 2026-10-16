namespace Forge.Api.Features.Scanner;

/// <summary>
/// The note a scanner issue writes on the material issues it records, which is how a scanner reversal finds the
/// issues to return.
/// </summary>
public static class ScanIssueNote
{
    public static string For(int scanActionLogId) => $"Scanner issue (scan #{scanActionLogId})";
}
