using System.Security.Claims;

namespace Forge.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string DisplayName(this ClaimsPrincipal? user)
    {
        if (user is null)
            return "Unknown";

        var name = user.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        var fullName = $"{user.FindFirstValue(ClaimTypes.GivenName)} {user.FindFirstValue(ClaimTypes.Surname)}".Trim();
        if (fullName.Length > 0)
            return fullName;

        var email = user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email) ? "Unknown" : email.Trim();
    }
}
