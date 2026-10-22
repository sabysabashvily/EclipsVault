using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EclipsVault.Infrastructure.Persistence.Configurations;

public sealed class SecretGrantConfiguration : IEntityTypeConfiguration<SecretGrant>
{
    public void Configure(EntityTypeBuilder<SecretGrant> builder)
    {
        builder.ToTable("SecretGrants");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.GranteeUsername).HasMaxLength(64).IsRequired();
        builder.Property(g => g.GrantedBy).HasMaxLength(64).IsRequired();

        // One active grant per (secret, grantee).
        builder.HasIndex(g => new { g.SecretId, g.GranteeUserId }).IsUnique();
        builder.HasIndex(g => g.GranteeUserId);
        builder.HasIndex(g => g.IssuerUserId);

        builder.HasOne<Secret>()
            .WithMany()
            .HasForeignKey(g => g.SecretId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(g => g.GranteeUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
