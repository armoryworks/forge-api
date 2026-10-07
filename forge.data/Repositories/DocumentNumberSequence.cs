using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Forge.Data.Repositories;

public static class DocumentNumberSequence
{
    public static async Task<string> NextAsync(IQueryable<string> numbers, string prefix, CancellationToken ct)
    {
        var token = $"{prefix}-";
        var pattern = $"^{Regex.Escape(token)}[0-9]{{1,18}}$";

        var highest = await numbers
            .Where(n => n.StartsWith(token))
            .Where(n => Regex.IsMatch(n, pattern))
            .Select(n => (long?)Convert.ToInt64(n.Substring(token.Length)))
            .MaxAsync(ct);

        return $"{token}{(highest ?? 0) + 1:D5}";
    }
}
