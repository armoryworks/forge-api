namespace Forge.Core.Models;

/// <summary>
/// Phase 1r — flat cross-customer contact row for the
/// /customers/contacts admin view. Combines Contact + parent Customer
/// + suppression-prefs summary in a single row.
/// </summary>
public record FlatContactRowModel(
    int ContactId,
    int CustomerId,
    string CustomerName,
    string? CompanyName,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool IsPrimary,
    bool EmailOptOut,
    bool CallOptOut,
    bool InCooldown);
