using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EclipsVault.Infrastructure.Persistence.Configurations;

public sealed class AccessRequestConfiguration : IEntityTypeConfiguration<AccessRequest>
{
    public void Configure(EntityTypeBuilder<AccessRequest> builder)
    {
        builder.ToTable("AccessRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.SecretName).HasMaxLength(128).IsRequired();
        builder.Property(r => r.ProjectKey).HasMaxLength(64).IsRequired();
        builder.Property(r => r.RequesterUsername).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.DeniedReasons).HasMaxLength(2000);
        builder.Property(r => r.DecidedBy).HasMaxLength(64);
        builder.Property(r => r.DecisionNote).HasMaxLength(500);
        builder.Property(r => r.Status).HasConversion<int>().IsConcurrencyToken();

        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.RequesterUserId);

        // Snapshots aside, a request is tied to its secret and requester; both cascade-delete
        // (mirrors SecretGrant, which SQL Server already accepts with two cascade paths here).
        builder.HasOne<Secret>()
            .WithMany()
            .HasForeignKey(r => r.SecretId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.RequesterUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
