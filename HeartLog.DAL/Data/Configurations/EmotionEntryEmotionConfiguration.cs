using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeartLog.DAL.Data.Configurations;

public class EmotionEntryEmotionConfiguration : IEntityTypeConfiguration<EmotionEntryEmotion>
{
    public void Configure(EntityTypeBuilder<EmotionEntryEmotion> builder)
    {
        builder.ToTable("EmotionEntryEmotions");

        builder.HasKey(ee => ee.Id);

        builder.Property(ee => ee.Id)
            .ValueGeneratedNever();

        builder.Property(ee => ee.CreatedAt)
            .IsRequired();

        builder.HasIndex(ee => new { ee.EmotionEntryId, ee.EmotionId })
            .IsUnique();

        builder.HasIndex(ee => ee.EmotionEntryId)
            .HasFilter("\"IsPrimary\" = true")
            .IsUnique();

        builder.HasOne(ee => ee.EmotionEntry)
            .WithMany(e => e.EmotionEntryEmotions)
            .HasForeignKey(ee => ee.EmotionEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ee => ee.Emotion)
            .WithMany()
            .HasForeignKey(ee => ee.EmotionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
