using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeartLog.DAL.Data.Configurations;

public class EmotionTranslationConfiguration : IEntityTypeConfiguration<EmotionTranslation>
{
    public void Configure(EntityTypeBuilder<EmotionTranslation> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Locale)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(t => t.Label)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(t => new { t.EmotionId, t.Locale })
            .IsUnique();
    }
}
