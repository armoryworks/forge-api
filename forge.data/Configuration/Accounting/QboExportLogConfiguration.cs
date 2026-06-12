using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class QboExportLogConfiguration : IEntityTypeConfiguration<QboExportLog>
{
    public void Configure(EntityTypeBuilder<QboExportLog> builder)
    {
        builder.ToTable("acct_qbo_export_logs");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.QboDocId).HasMaxLength(50).IsRequired();
        builder.Property(e => e.TotalDebit).HasPrecision(18, 2);

        // The overlap check filters on (BookId, FromDate, ToDate).
        builder.HasIndex(e => new { e.BookId, e.FromDate, e.ToDate })
            .HasDatabaseName("ix_acct_qbo_export_logs_book_range");
    }
}
