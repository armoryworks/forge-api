using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CustomerPoDocumentConfiguration : IEntityTypeConfiguration<CustomerPoDocument>
{
    public void Configure(EntityTypeBuilder<CustomerPoDocument> builder)
    {
        builder.HasIndex(e => e.DocumentNumber).IsUnique();
        builder.HasIndex(e => e.SalesOrderId);
    }
}
