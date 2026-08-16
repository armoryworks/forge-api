using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AttestationConfiguration : IEntityTypeConfiguration<Attestation>
{
    public void Configure(EntityTypeBuilder<Attestation> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.RequireSalesOrderId);

        // Every statement is scoped to something: an order, a party, or both.
        // A row with neither identifies nothing and could never be found again.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_attestations_scope",
            "sales_order_id IS NOT NULL OR party_id IS NOT NULL"));

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Method).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.StatementType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.PartyType).HasConversion<string>().HasMaxLength(20);

        // The production gate's exact query shape. Unchanged by the
        // generalization — party-level rows carry a null SalesOrderId and so
        // never appear in it.
        builder.HasIndex(e => new { e.SalesOrderId, e.Status });
        builder.HasIndex(e => e.AccessToken);
        builder.HasIndex(e => new { e.PartyType, e.PartyId });
        builder.HasIndex(e => e.ArtifactId);
        builder.HasIndex(e => e.CommunicationId);

        builder.HasOne(e => e.SalesOrder)
            .WithMany()
            .HasForeignKey(e => e.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // The FK to the file is optional (verbal acceptance has no document) and nulls out if the
        // file is ever removed. RecordedByUserId / AcceptedByContactId are DB-level FKs (forge-db)
        // without EF navs — intentionally not modelled here.
        builder.HasOne(e => e.FileAttachment)
            .WithMany()
            .HasForeignKey(e => e.FileAttachmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Restrict, not SetNull: the artifact IS the evidence. Losing the link
        // would leave a statement claiming proof that can no longer be produced.
        builder.HasOne(e => e.Artifact)
            .WithMany()
            .HasForeignKey(e => e.ArtifactId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Communication)
            .WithMany()
            .HasForeignKey(e => e.CommunicationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-references: the supersession chain and the supporting agreement.
        builder.HasOne(e => e.SupersededBy)
            .WithMany()
            .HasForeignKey(e => e.SupersededById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SupportedByAttestation)
            .WithMany()
            .HasForeignKey(e => e.SupportedByAttestationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
