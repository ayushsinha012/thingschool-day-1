using MaintainXpert.Assets.Domain;
using MaintainXpert.Maintenance.Domain;
using MaintainXpert.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace MaintainXpert.Api.Infrastructure;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<Asset> Assets => Set<Asset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkOrder>(builder =>
        {
            builder.HasKey(w => w.Id);

            builder.Property(w => w.Id)
                .HasConversion(id => id.Value, value => new WorkOrderId(value))
                .ValueGeneratedNever();

            builder.Property(w => w.AssetId)
                .HasConversion(id => id.Value, value => new AssetId(value));

            builder.Property(w => w.Description)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(w => w.CreatedAt)
                .IsRequired();

            builder.Property(w => w.Priority)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Property(w => w.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Property(w => w.AssignedTechnicianId)
                .HasConversion(
                    id => id == null ? (Guid?)null : id.Value.Value,
                    value => value == null ? (TechnicianId?)null : new TechnicianId(value.Value));

            builder.Ignore(w => w.DomainEvents);
        });

        modelBuilder.Entity<Asset>(builder =>
        {
            builder.HasKey(a => a.Id);

            builder.Property(a => a.Id)
                .HasConversion(id => id.Value, value => new AssetId(value))
                .ValueGeneratedNever();

            builder.Property(a => a.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(a => a.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
        });
    }
}
