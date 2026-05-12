using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using QBEngineer.Core.Entities;

namespace QBEngineer.Data.Configuration;

public class EmployeeProfileConfiguration : IEntityTypeConfiguration<EmployeeProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeProfile> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        // Phase 3 / WU-19 / F9: Employee can exist with no User account.
        // UserId is nullable; the unique constraint is filtered to non-null
        // values so multiple User-less Employees can coexist while a User
        // can still be linked to at most one Employee.
        builder.HasIndex(e => e.UserId)
            .IsUnique()
            .HasFilter("user_id IS NOT NULL");

        // Identity (denormalized when no User exists)
        builder.Property(e => e.FirstName).HasMaxLength(100);
        builder.Property(e => e.LastName).HasMaxLength(100);
        builder.Property(e => e.WorkEmail).HasMaxLength(256);

        // Personal
        builder.Property(e => e.Gender).HasMaxLength(50);

        // Address
        builder.Property(e => e.Street1).HasMaxLength(200);
        builder.Property(e => e.Street2).HasMaxLength(200);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.State).HasMaxLength(100);
        builder.Property(e => e.ZipCode).HasMaxLength(20);
        builder.Property(e => e.Country).HasMaxLength(100);

        // Contact
        builder.Property(e => e.PhoneNumber).HasMaxLength(50);
        builder.Property(e => e.PersonalEmail).HasMaxLength(200);

        // Emergency
        builder.Property(e => e.EmergencyContactName).HasMaxLength(200);
        builder.Property(e => e.EmergencyContactPhone).HasMaxLength(50);
        builder.Property(e => e.EmergencyContactRelationship).HasMaxLength(100);

        // Employment
        builder.Property(e => e.Department).HasMaxLength(200);
        builder.Property(e => e.JobTitle).HasMaxLength(200);
        builder.Property(e => e.EmployeeNumber).HasMaxLength(50);
        builder.Property(e => e.HourlyRate).HasPrecision(10, 2);
        builder.Property(e => e.SalaryAmount).HasPrecision(12, 2);

        // Sensitive identifiers — ciphertext from IPiiProtector. Stored as
        // text (no fixed length — DP envelope length varies with payload +
        // key rotation). Never indexed.
        builder.Property(e => e.SsnProtected);
        builder.Property(e => e.BankName).HasMaxLength(200);
        builder.Property(e => e.BankRoutingProtected);
        builder.Property(e => e.BankAccountProtected);
        builder.Property(e => e.BankAccountType).HasMaxLength(20);
    }
}
