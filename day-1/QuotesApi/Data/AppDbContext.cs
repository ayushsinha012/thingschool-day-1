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

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==========================================
        // Quote
        // ==========================================

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(quote => quote.Id);

            entity.Property(quote => quote.Author)
                .IsRequired();

            entity.Property(quote => quote.Text)
                .IsRequired();
        });

        // ==========================================
        // Collection
        // ==========================================

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(collection => collection.Id);

            entity.Property(collection => collection.Name)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(collection => collection.OwnerId)
                .IsRequired();

            // Collection owns CollectionItem.
            // CollectionItem cannot exist independently.
            entity.OwnsMany(
                collection => collection.Items,
                item =>
                {
                    item.WithOwner()
                        .HasForeignKey("CollectionId");

                    // Shadow key used by EF Core.
                    item.Property<int>("Id");

                    item.HasKey("Id");

                    item.Property(collectionItem => collectionItem.QuoteId)
                        .IsRequired();

                    item.Property(collectionItem => collectionItem.AddedAt)
                        .IsRequired();
                });

            // Tell EF to use the private _items
            // backing field instead of trying to modify
            // the read-only Items property.
            entity.Navigation(collection => collection.Items)
                .UsePropertyAccessMode(
                    PropertyAccessMode.Field);
        });
    }
}