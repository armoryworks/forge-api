namespace Forge.Api.Features.Scanner;

/// <summary>
/// The note a scanner receipt writes on the receiving record it makes, which is how a scanner reversal finds the
/// receipt to take back.
/// </summary>
public static class ScanReceiveNote
{
    public static string For(int scanActionLogId) => $"Scanner receipt (scan #{scanActionLogId})";
}
