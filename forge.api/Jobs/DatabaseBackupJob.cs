using System.Diagnostics;

using Npgsql;

using Forge.Core.Interfaces;

namespace Forge.Api.Jobs;

public class DatabaseBackupJob(
    IConfiguration config,
    IClock clock,
    ILogger<DatabaseBackupJob> logger)
{
    public async Task RunBackupAsync(CancellationToken ct = default)
    {
        var connectionString = config.GetConnectionString("DefaultConnection") ?? string.Empty;
        var backupOptions = config.GetSection("Backup").Get<DatabaseBackupOptions>() ?? new DatabaseBackupOptions();

        var timestamp = clock.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"forge_{timestamp}.sql.gz";
        var filePath = Path.Combine(backupOptions.BackupPath, fileName);

        Directory.CreateDirectory(backupOptions.BackupPath);

        logger.LogInformation("Starting database backup to {FilePath}", filePath);

        var csBuilder = new NpgsqlConnectionStringBuilder(connectionString);

        var startInfo = new ProcessStartInfo
        {
            FileName = backupOptions.PgDumpPath,
            Arguments = $"-h {csBuilder.Host} -p {csBuilder.Port} -U {csBuilder.Username} -d {csBuilder.Database} -Fc",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment = { ["PGPASSWORD"] = csBuilder.Password }
        };

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start pg_dump process");

            await using var fileStream = File.Create(filePath);
            await process.StandardOutput.BaseStream.CopyToAsync(fileStream, ct);

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ct);
                logger.LogError("pg_dump failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                throw new InvalidOperationException($"pg_dump failed: {error}");
            }

            var fileInfo = new FileInfo(filePath);
            logger.LogInformation("Database backup completed: {FilePath} ({Size} bytes)", filePath, fileInfo.Length);

            CleanupOldBackups(backupOptions.BackupPath, backupOptions.RetentionDays);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database backup failed");

            if (File.Exists(filePath))
                File.Delete(filePath);

            throw;
        }
    }

    private void CleanupOldBackups(string backupPath, int retentionDays)
    {
        var cutoff = clock.UtcNow.AddDays(-retentionDays);
        var backupFiles = Directory.GetFiles(backupPath, "forge_*.sql*");

        foreach (var file in backupFiles)
        {
            if (File.GetCreationTimeUtc(file) < cutoff)
            {
                File.Delete(file);
                logger.LogInformation("Deleted old backup: {File}", Path.GetFileName(file));
            }
        }
    }
}
