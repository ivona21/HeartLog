namespace HeartLog.DAL.Data;
using Configurations;
using Microsoft.EntityFrameworkCore;
using Models;

public class ApplicationDbContext: DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
        
    }
    
    public DbSet<User> Users { get; set; } = null!;
    
    public DbSet<Item> Items { get; set; } = null!;

    public DbSet<Emotion> Emotions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new EmotionConfiguration());
    }
}
