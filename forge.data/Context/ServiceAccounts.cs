namespace Forge.Data.Context;

public static class ServiceAccounts
{
    public const string LeadIntakeRole = "LeadIntake";

    private static readonly string[] Roles = [LeadIntakeRole];

    public static bool IsServiceAccount(IEnumerable<string> roles)
        => roles.Any(r => Roles.Contains(r, StringComparer.Ordinal));

    public static IQueryable<ApplicationUser> WithoutServiceAccounts(this IQueryable<ApplicationUser> users, AppDbContext db)
        => users.Where(u => !db.UserRoles.Any(ur => ur.UserId == u.Id
            && db.Roles.Any(r => r.Id == ur.RoleId && Roles.Contains(r.Name!))));

    public static IQueryable<ApplicationUser> OnlyServiceAccounts(this IQueryable<ApplicationUser> users, AppDbContext db)
        => users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id
            && db.Roles.Any(r => r.Id == ur.RoleId && Roles.Contains(r.Name!))));
}
