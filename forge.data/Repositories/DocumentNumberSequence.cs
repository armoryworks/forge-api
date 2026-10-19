using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Forge.Data.Repositories;

public static class DocumentNumberSequence
{
    public static async Task<string> NextAsync(IQueryable<string> numbers, string prefix, CancellationToken ct)
        => (await NextBlockAsync(numbers, prefix, 1, ct))[0];

    public static async Task<IReadOnlyList<string>> NextBlockAsync(
        IQueryable<string> numbers, string prefix, int count, CancellationToken ct)
    {
        var token = $"{prefix}-";
        var pattern = $"^{Regex.Escape(token)}[0-9]{{1,18}}$";

        var highest = await numbers
            .Where(n => n.StartsWith(token))
            .Where(n => Regex.IsMatch(n, pattern))
            .Select(n => (long?)Convert.ToInt64(n.Substring(token.Length)))
            .MaxAsync(ct);

        var start = (highest ?? 0) + 1;
        return Enumerable.Range(0, count).Select(i => $"{token}{start + i:D5}").ToList();
    }
}
