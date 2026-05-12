namespace QBEngineer.Api.Services;

/// <summary>
/// Encrypts / decrypts regulatory PII (SSN, bank routing/account numbers,
/// I-9 document numbers) using ASP.NET Data Protection. Ciphertext is stored
/// in plain `text` columns on EmployeeProfile + IdentityDocument; the active
/// DP key chain is persisted to Postgres (configured in Program.cs).
///
/// Never log ciphertext OR plaintext; never round-trip plaintext through
/// client-facing response models — only decrypt at the seams that need it
/// (PDF fill, DocuSeal submission, admin audit).
/// </summary>
public interface IPiiProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/>. Returns null when input is null/blank.</summary>
    string? Protect(string? plaintext);

    /// <summary>Decrypts <paramref name="ciphertext"/>. Returns null when input is null/blank.</summary>
    string? Unprotect(string? ciphertext);
}
