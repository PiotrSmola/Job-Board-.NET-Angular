using Backend.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Backend.Data;

// IDataProtectionKeyContext - klucze Data Protection (tokeny potwierdzenia e-maila i resetu hasła) w bazie
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies { get; set; }
    public DbSet<JobOffer> JobOffers { get; set; }
    public DbSet<JobApplication> JobApplications { get; set; }
    public DbSet<ApplicationStatusHistory> ApplicationStatusHistories { get; set; }
    public DbSet<ApplicationAttachment> ApplicationAttachments { get; set; }
    public DbSet<ProfileDocument> ProfileDocuments { get; set; }
    public DbSet<SavedJob> SavedJobs { get; set; }
    public DbSet<EmailMessage> EmailMessages { get; set; }
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

    // stan EmailOutboxInterceptor: trwa zapis nowych wiadomości e-mail / zapisane czekają na COMMIT transakcji
    internal bool SavingEmails { get; set; }
    internal bool UncommittedEmails { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // MySQL nie przechowuje strefy - wszystkie daty zapisujemy jako UTC i odczytujemy z Kind = Utc,
        // dzięki czemu API zwraca je z sufiksem "Z" (frontend nie interpretuje ich jako czasu lokalnego)
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<JobOffer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(5000);
            entity.Property(e => e.Location).IsRequired().HasMaxLength(200);
            entity.Property(e => e.SalaryMin).HasPrecision(18, 2);
            entity.Property(e => e.SalaryMax).HasPrecision(18, 2);
            entity.Property(e => e.WorkType).HasConversion<int>();
            entity.Property(e => e.CreatedAt);
            // wartość domyślna kolumny (Active) jest ustawiona w migracji - w modelu jej nie deklarujemy,
            // bo EF pomijałby wtedy przy INSERT wartość Draft (= domyślna wartość CLR enuma)
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.ExpiresAt);

            // publiczna lista ofert: filtr po statusie i dacie ważności, domyślne sortowanie po dacie utworzenia
            entity.HasIndex(e => new { e.Status, e.ExpiresAt, e.CreatedAt });

            entity.HasMany(e => e.Applications)
                .WithOne(e => e.JobOffer)
                .HasForeignKey(e => e.JobOfferId)
                .OnDelete(DeleteBehavior.Cascade);

            // firmy z ofertami nie można usunąć (najpierw trzeba usunąć oferty)
            entity.HasOne(e => e.Company)
                .WithMany(e => e.JobOffers)
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(5000);
            entity.Property(e => e.Website).HasMaxLength(300);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);

            // unikalność bez rozróżniania wielkości liter zapewnia kolacja kolumny (utf8mb4_0900_ai_ci)
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.Status);

            // usunięcie konta admina nie usuwa firmy ani informacji o decyzji
            entity.HasOne(e => e.ReviewedByUser)
                .WithMany()
                .HasForeignKey(e => e.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<JobApplication>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Note).HasMaxLength(1000);
            entity.Property(e => e.AppliedAt);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.StatusChangedAt).IsConcurrencyToken();

            // jedna aplikacja użytkownika na ofertę (chroni też przed równoległymi żądaniami)
            entity.HasIndex(e => new { e.JobOfferId, e.UserId }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany(e => e.JobApplications)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.StatusHistory)
                .WithOne(e => e.JobApplication)
                .HasForeignKey(e => e.JobApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            // załączniki usuwane razem z aplikacją (pliki w magazynie usuwa kod po zatwierdzeniu zmian)
            entity.HasMany(e => e.Attachments)
                .WithOne(e => e.JobApplication)
                .HasForeignKey(e => e.JobApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApplicationAttachment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasConversion<int>();
            entity.Property(e => e.FileKey).IsRequired().HasMaxLength(300);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);

            // klucz pliku w magazynie wskazuje dokładnie jeden załącznik
            entity.HasIndex(e => e.FileKey).IsUnique();
            entity.HasIndex(e => e.JobApplicationId);
        });

        builder.Entity<ProfileDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasConversion<int>();
            entity.Property(e => e.FileKey).IsRequired().HasMaxLength(300);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);

            // klucz pliku w magazynie wskazuje dokładnie jeden dokument
            entity.HasIndex(e => e.FileKey).IsUnique();
            entity.HasIndex(e => e.UserId);
        });

        builder.Entity<SavedJob>(entity =>
        {
            // jedna pozycja na parę użytkownik-oferta (PUT jest idempotentny)
            entity.HasKey(e => new { e.UserId, e.JobOfferId });
            entity.Property(e => e.SavedAt);

            // lista "Zapisane oferty" - od najnowszych zapisów
            entity.HasIndex(e => new { e.UserId, e.SavedAt });

            // usunięcie oferty usuwa ją z zapisanych
            entity.HasOne(e => e.JobOffer)
                .WithMany()
                .HasForeignKey(e => e.JobOfferId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EmailMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.ToAddress).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ToName).HasMaxLength(200);
            entity.Property(e => e.Subject).IsRequired().HasMaxLength(300);
            entity.Property(e => e.LastError).HasMaxLength(500);

            // wiadomości do wysłania: Pending z NextAttemptAt <= teraz
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt });
            // limit e-maili resetu hasła na adres (Type, UserId, CreatedAt)
            entity.HasIndex(e => new { e.UserId, e.Type, e.CreatedAt });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApplicationStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.Comment).HasMaxLength(1000);

            // usunięcie konta autora zmiany nie usuwa historii
            entity.HasOne(e => e.ChangedByUser)
                .WithMany()
                .HasForeignKey(e => e.ChangedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.CreatedAt);
            entity.Property(e => e.Headline).HasMaxLength(150);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.LinkedInUrl).HasMaxLength(300);
            entity.Property(e => e.About).HasMaxLength(2000);
            // wartość domyślna kolumn (true - istniejące konta mają powiadomienia włączone) jest ustawiona w migracji;
            // w modelu jej nie deklarujemy, bo EF pomijałby przy INSERT wartość false (= domyślna wartość CLR)

            // dokumenty profilu, zapisane oferty i wiadomości e-mail usuwane razem z kontem (pliki w magazynie
            // usuwa kod po zatwierdzeniu zmian)
            entity.HasMany(e => e.ProfileDocuments)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.SavedJobs)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // firmy z członkami (pracodawcami) nie można usunąć - konto pracodawcy bez firmy nie ma sensu
            entity.HasOne(e => e.Company)
                .WithMany(e => e.Members)
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => ToUtc(v),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? ToUtc(v.Value) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
