using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(quote => quote.Id);

            entity.Property(quote => quote.Author)
                .IsRequired();

            entity.Property(quote => quote.Text)
                .IsRequired();

            entity.HasIndex(quote => quote.Author);
        });


        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(collection => collection.Id);

            entity.Property(collection => collection.Name)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(collection => collection.OwnerId)
                .IsRequired();

            entity.OwnsMany(
                collection => collection.Items,
                item =>
                {
                    item.WithOwner()
                        .HasForeignKey("CollectionId");

                    item.Property<int>("Id");

                    item.HasKey("Id");

                    item.Property(collectionItem => collectionItem.QuoteId)
                        .IsRequired();

                    item.Property(collectionItem => collectionItem.AddedAt)
                        .IsRequired();
                });

            entity.Navigation(collection => collection.Items)
                .UsePropertyAccessMode(
                    PropertyAccessMode.Field);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);

            entity.Property(user => user.Email)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(user => user.PasswordHash)
                .IsRequired();

            entity.HasIndex(user => user.Email)
                .IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(token => token.Id);

            entity.Property(token => token.TokenHash)
                .IsRequired();

            entity.HasIndex(token => token.TokenHash)
                .IsUnique();

            entity.HasIndex(token => token.FamilyId);

            entity.HasIndex(token => token.UserId);
        });
    }
}