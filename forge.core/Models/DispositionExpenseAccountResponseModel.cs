namespace Forge.Core.Models;

/// <summary>A postable expense account a job's work in process can be written off to when it is disposed as Other.</summary>
public record DispositionExpenseAccountResponseModel(int Id, string AccountNumber, string Name);
