namespace Forge.Api.Jobs;

public class DatabaseBackupOptions
{
    public string BackupPath { get; set; } = "/backups";
    public int RetentionDays { get; set; } = 30;
    public string PgDumpPath { get; set; } = "pg_dump";
}
