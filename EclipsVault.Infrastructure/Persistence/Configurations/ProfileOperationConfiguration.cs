using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EclipsVault.Infrastructure.Persistence.Configurations;

public sealed class ProfileOperationConfiguration : IEntityTypeConfiguration<ProfileOperation>
{
    public void Configure(EntityTypeBuilder<ProfileOperation> builder)
    {
        builder.ToTable("ProfileOperations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Kind).HasConversion<int>();
        builder.Property(o => o.ProofMethod).HasConversion<int?>();
        builder.Property(o => o.ChallengePurpose).HasConversion<int?>();
        builder.Property(o => o.RevocationVersion).HasMaxLength(64).IsRequired();
        builder.Property(o => o.CandidatePasswordHash).HasMaxLength(64);
        builder.Property(o => o.CandidatePasswordSalt).HasMaxLength(32);
        builder.Property(o => o.PasskeyNickname).HasMaxLength(64);
        builder.Property(o => o.Challenge).HasMaxLength(128);
        builder.Property(o => o.TargetEmail).HasMaxLength(254);
        builder.Property(o => o.MailboxCodeHash).HasMaxLength(32);

        // A session holds one operation, so it has one passkey prompt in flight; preparing replaces it.
        builder.HasIndex(o => new { o.UserId, o.SessionId }).IsUnique();

        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
