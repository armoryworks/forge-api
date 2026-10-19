namespace Forge.Core.Models;

/// <summary>A GL account a standalone vendor bill line can debit directly.</summary>
public record BillableGlAccountModel(int Id, string AccountNumber, string Name, string AccountType);
