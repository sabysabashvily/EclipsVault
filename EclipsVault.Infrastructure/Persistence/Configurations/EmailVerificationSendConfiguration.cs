using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EclipsVault.Infrastructure.Persistence.Configurations;

public sealed class EmailVerificationSendConfiguration : IEntityTypeConfiguration<EmailVerificationSend>
{
    public void Configure(EntityTypeBuilder<EmailVerificationSend> builder)
    {
        builder.HasKey(send => send.Id);
        builder.Property(send => send.SourceHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(send => new { send.AccountId, send.SentAtUtc });
        builder.HasIndex(send => new { send.SourceHash, send.SentAtUtc });
    }
}
