namespace Forge.Core.Models;

/// <summary>One application table in the transfer summary. Row count is the planner's estimate
/// (<c>pg_class.reltuples</c>) — exact counts over ~300 tables would hammer the DB for a preview.</summary>
public record DumpTableInfoModel(
    string Schema,
    string Name,
    long EstimatedRows,
    long SizeBytes);
