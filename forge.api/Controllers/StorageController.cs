using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Core.Interfaces;
using Forge.Integrations;

namespace Forge.Api.Controllers;

/// <summary>
/// Serves files from LocalFileStorageService via time-limited presigned tokens.
/// Only active when StorageProvider=local — MinIO serves its own presigned URLs directly.
/// This endpoint is intentionally unauthenticated (token = bearer).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/storage")]
public class StorageController(IStorageService storageService) : ControllerBase
{
    [HttpGet("{bucket}/{**key}")]
    public async Task<IActionResult> GetFile(string bucket, string key, [FromQuery] string token, CancellationToken ct)
    {
        if (storageService is not LocalFileStorageService localService)
            return NotFound(); // endpoint only meaningful for local storage

        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var validated = localService.ValidateToken(token);
        if (validated is null)
            return Unauthorized();

        if (!string.Equals(validated.Value.Bucket, bucket, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(validated.Value.Key, key, StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        try
        {
            var stream = await storageService.DownloadAsync(bucket, key, ct);
            var contentType = GetContentType(key);
            return File(stream, contentType, Path.GetFileName(key));
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
    }

    private static string GetContentType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".step" or ".stp" => "application/step",
            ".stl" => "model/stl",
            _ => "application/octet-stream",
        };
    }
}
