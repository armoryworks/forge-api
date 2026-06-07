using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class JournalTemplateConfiguration : IEntityTypeConfiguration<JournalTemplate>
{
    public void Configure(EntityTypeBuilder<JournalTemplate> builder)
    {
        builder.ToTable("acct_journal_templates");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Memo).HasMaxLength(500);

        builder.HasIndex(e => new { e.BookId, e.Name })
            .IsUnique()
            .HasDatabaseName("ux_acct_journal_templates_book_name");

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.JournalTemplate)
            .HasForeignKey(l => l.JournalTemplateId)
            .HasConstraintName("fk_acct_journal_template_lines_template")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class JournalTemplateLineConfiguration : IEntityTypeConfiguration<JournalTemplateLine>
{
    public void Configure(EntityTypeBuilder<JournalTemplateLine> builder)
    {
        builder.ToTable("acct_journal_template_lines");

        builder.Property(e => e.AccountDeterminationKey).HasMaxLength(60);
        builder.Property(e => e.Debit).HasPrecision(18, 2);
        builder.Property(e => e.Credit).HasPrecision(18, 2);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.PartyType).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.JournalTemplateId).HasDatabaseName("ix_acct_journal_template_lines_template");
    }
}
