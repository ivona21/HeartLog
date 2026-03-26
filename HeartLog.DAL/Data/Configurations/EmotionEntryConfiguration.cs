using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeartLog.DAL.Data.Configurations;

public class EmotionEntryConfiguration : IEntityTypeConfiguration<EmotionEntry>
{
    public void Configure(EntityTypeBuilder<EmotionEntry> builder)
    {
        builder.ToTable("EmotionEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.Comment)
            .HasColumnType("text");

        builder.Property(e => e.OccurredAt)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        builder.HasIndex(e => new { e.UserId, e.OccurredAt });

        builder.HasOne(e => e.User)
            .WithMany(u => u.EmotionEntries)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
