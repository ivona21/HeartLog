using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeartLog.DAL.Data.Configurations;

public class AgreementEventConfiguration : IEntityTypeConfiguration<AgreementEvent>
{
    public void Configure(EntityTypeBuilder<AgreementEvent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgreementKey).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(24).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(32).IsRequired();
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.UserId, x.AgreementKey, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.UserId, x.SubmissionId, x.AgreementKey }).IsUnique();
    }
}

public class RegistrationIntentConfiguration : IEntityTypeConfiguration<RegistrationIntent>
{
    public void Configure(EntityTypeBuilder<RegistrationIntent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TermsVersion).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PrivacyPolicyVersion).HasMaxLength(64).IsRequired();
        builder.Property(x => x.JournalProcessingVersion).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
