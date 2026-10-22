using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EclipsVault.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username).HasMaxLength(64).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.DisplayName).HasMaxLength(64).IsRequired();

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();

        // Email is a sign-in identity, so its normalized form is what one account must hold alone.
        builder.Property(u => u.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();

        builder.Property(u => u.PasswordHash).HasMaxLength(64).IsRequired();
        builder.Property(u => u.PasswordSalt).HasMaxLength(32).IsRequired();

        builder.Property(u => u.TotpSecret).HasMaxLength(128);

        builder.Property(u => u.ProjectKey).HasMaxLength(64).IsRequired();
        builder.Property(u => u.Clearance).HasConversion<int>();

        builder.HasMany(u => u.Passkeys)
            .WithOne(p => p.User)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
