using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeartLog.DAL.Data.Configurations;

public class EmotionConfiguration : IEntityTypeConfiguration<Emotion>
{
    public void Configure(EntityTypeBuilder<Emotion> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Level)
            .HasConversion<int>();

        builder.Property(e => e.Color)
            .HasMaxLength(20);

        builder.HasIndex(e => e.Key)
            .IsUnique();

        builder.HasIndex(e => new { e.ParentId, e.SortOrder });

        builder.HasOne(e => e.Parent)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Translations)
            .WithOne(t => t.Emotion)
            .HasForeignKey(t => t.EmotionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
