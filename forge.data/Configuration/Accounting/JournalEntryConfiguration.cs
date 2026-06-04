using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("acct_journal_entries");

        builder.HasKey(e => e.Id);
        // long PK — explicit value generation (cheap pre-rows; painful to widen later).
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.SourceType).HasMaxLength(100);
        builder.Property(e => e.IdempotencyKey).HasMaxLength(200);
        builder.Property(e => e.Memo).HasMaxLength(2000);

        // Idempotency: a duplicate key returns the existing entry (engine), but
        // the DB still guards it — UNIQUE(BookId, IdempotencyKey) (§5.2).
        builder.HasIndex(e => new { e.BookId, e.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_acct_journal_entries_book_idemp");

        // EntryNumber monotonic per book/year — gaps allowed but unique (§5.1).
        builder.HasIndex(e => new { e.BookId, e.FiscalYearId, e.EntryNumber })
            .IsUnique()
            .HasDatabaseName("ux_acct_journal_entries_book_year_num");

        builder.HasIndex(e => new { e.SourceType, e.SourceId })
            .HasDatabaseName("ix_acct_journal_entries_source");

        builder.HasIndex(e => e.FiscalPeriodId).HasDatabaseName("ix_acct_journal_entries_period");

        builder.HasOne(e => e.Book)
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_acct_journal_entries_book")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FiscalPeriod)
            .WithMany(p => p.JournalEntries)
            .HasForeignKey(e => e.FiscalPeriodId)
            .HasConstraintName("fk_acct_journal_entries_period")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FiscalYear)
            .WithMany()
            .HasForeignKey(e => e.FiscalYearId)
            .HasConstraintName("fk_acct_journal_entries_year")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Currency)
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .HasConstraintName("fk_acct_journal_entries_currency")
            .OnDelete(DeleteBehavior.Restrict);

        // Reversal links — both self-referential, never cascade (reverse, don't delete).
        builder.HasOne(e => e.ReversalOfEntry)
            .WithMany()
            .HasForeignKey(e => e.ReversalOfEntryId)
            .HasConstraintName("fk_acct_journal_entries_reversal_of")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReversedByEntry)
            .WithMany()
            .HasForeignKey(e => e.ReversedByEntryId)
            .HasConstraintName("fk_acct_journal_entries_reversed_by")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.JournalEntry)
            .HasForeignKey(l => l.JournalEntryId)
            .HasConstraintName("fk_acct_journal_lines_entry")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
