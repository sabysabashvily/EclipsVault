using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EclipsVault.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Username).HasMaxLength(64).IsRequired();
        builder.Property(a => a.SourceIp).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Action).HasConversion<int>();
        builder.Property(a => a.ResourceType).HasMaxLength(64).IsRequired();
        builder.Property(a => a.ResourceName).HasMaxLength(AuditLog.ResourceNameMaxLength);
        builder.Property(a => a.Details).HasMaxLength(1024);
        builder.Property(a => a.PreviousHash).HasMaxLength(64);
        builder.Property(a => a.EntryHash).HasMaxLength(64);

        // Existing rows were sealed before this column existed, so the stored default is 1 — the
        // scheme they were written with. New rows are stamped explicitly by the chain writer.
        builder.Property(a => a.HashVersion).HasDefaultValue(1);

        builder.HasIndex(a => a.TimestampUtc);
        builder.HasIndex(a => a.ResourceId);
        builder.HasIndex(a => a.UserId);

        // Chained rows have a unique, gap-free sequence. The index that enforces that is declared in
        // EclipsVaultDbContext.OnModelCreating instead: its filter has to be written in the
        // provider's own identifier quoting, and a configuration class cannot see the provider.
        builder.HasIndex(a => a.Sequence);
    }
}
