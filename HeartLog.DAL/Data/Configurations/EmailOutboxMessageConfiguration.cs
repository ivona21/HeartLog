using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeartLog.DAL.Data.Configurations;

public class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("EmailOutbox");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Recipient).IsRequired().HasMaxLength(320);
        builder.Property(m => m.Template).IsRequired().HasMaxLength(100);
        builder.HasIndex(m => m.NextAttemptAt);
        builder.HasIndex(m => m.ExpiresAt);
    }
}
