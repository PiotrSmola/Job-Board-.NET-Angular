using Backend.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<JobOffer> JobOffers { get; set; }
    public DbSet<JobApplication> JobApplications { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<JobOffer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Company).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(5000);
            entity.Property(e => e.Location).IsRequired().HasMaxLength(200);
            entity.Property(e => e.SalaryMin).HasPrecision(18, 2);
            entity.Property(e => e.SalaryMax).HasPrecision(18, 2);
            entity.Property(e => e.WorkType).HasConversion<int>();
            entity.Property(e => e.CreatedAt);

            entity.HasMany(e => e.Applications)
                .WithOne(e => e.JobOffer)
                .HasForeignKey(e => e.JobOfferId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<JobApplication>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Note).HasMaxLength(1000);
            entity.Property(e => e.AppliedAt);

            entity.HasOne(e => e.User)
                .WithMany(e => e.JobApplications)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.CreatedAt);
        });
    }
}
