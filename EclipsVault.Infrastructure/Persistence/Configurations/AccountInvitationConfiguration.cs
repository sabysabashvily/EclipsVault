using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EclipsVault.Infrastructure.Persistence.Configurations;

public sealed class AccountInvitationConfiguration : IEntityTypeConfiguration<AccountInvitation>
{
    public void Configure(EntityTypeBuilder<AccountInvitation> builder)
    {
        builder.ToTable("AccountInvitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.TokenHash).HasMaxLength(32).IsRequired();
        builder.HasIndex(i => i.TokenHash).IsUnique();

        // One invitation row per account; resending replaces its token hash and expiry in place.
        builder.HasIndex(i => i.UserId).IsUnique();

        builder.Property(i => i.PendingTotpSecret).HasMaxLength(128);

        builder.HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
