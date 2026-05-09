using AzureQuotes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureQuotes.Api.Data;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteLike> QuoteLikes => Set<QuoteLike>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Content).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.PhotoUrl).HasMaxLength(2048);
            entity.Property(x => x.PhotoStorageKey).HasMaxLength(1024);
            entity.HasOne(x => x.User)
                .WithMany(x => x.Quotes)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuoteLike>(entity =>
        {
            entity.HasKey(x => new { x.QuoteId, x.UserId });
            entity.HasOne(x => x.Quote)
                .WithMany(x => x.Likes)
                .HasForeignKey(x => x.QuoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany(x => x.Likes)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
