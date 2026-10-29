using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeartLog.DAL.Data.Configurations;

public class AccountDeletionConfiguration : IEntityTypeConfiguration<AccountDeletion>
{
    public void Configure(EntityTypeBuilder<AccountDeletion> builder)
    {
        builder.HasKey(d => d.SupabaseUserId);
        builder.Property(d => d.SupabaseUserId).ValueGeneratedNever();
        builder.HasIndex(d => d.NextAttemptAt);
    }
}
