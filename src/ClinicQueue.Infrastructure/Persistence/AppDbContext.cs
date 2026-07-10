using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Common;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Medical;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Platform;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClinicQueue.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<ClinicMembership> ClinicMemberships => Set<ClinicMembership>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Dependent> Dependents => Set<Dependent>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxNotification> OutboxNotifications => Set<OutboxNotification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<QueueSession> QueueSessions => Set<QueueSession>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<ClinicExportJob> ClinicExportJobs => Set<ClinicExportJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // UTC DateTime persistence (Npgsql timestamptz) — constitution: UTC in the database
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var utcNullableConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(utcNullableConverter);
                }
            }
        }

        ConfigureUser(modelBuilder);
        ConfigureClinic(modelBuilder);
        ConfigureClinicMembership(modelBuilder);
        ConfigureSubscription(modelBuilder);
        ConfigureDependent(modelBuilder);
        ConfigureRefreshToken(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ConfigureOutbox(modelBuilder);
        ConfigurePushSubscription(modelBuilder);
        ConfigureQueueSession(modelBuilder);
        ConfigureAppointment(modelBuilder);
        ConfigurePatientProfile(modelBuilder);
        ConfigureVisit(modelBuilder);
        ConfigurePrescription(modelBuilder);
        ConfigureClinicExportJob(modelBuilder);

        // Intentionally NO HasQueryFilter for tenant isolation — applied in BaseRepository at execution time.
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = utcNow;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ClinicScopedEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<User>();
        e.ToTable("users");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.EmailOrPhone).IsUnique();
        e.Property(x => x.EmailOrPhone).HasMaxLength(320).IsRequired();
        e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
    }

    private static void ConfigureClinic(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Clinic>();
        e.ToTable("clinics");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.SubdomainSlug).IsUnique();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Specialty).HasMaxLength(200).IsRequired();
        e.Property(x => x.SubdomainSlug).HasMaxLength(100).IsRequired();
        e.Property(x => x.Timezone).HasMaxLength(100).IsRequired();
    }

    private static void ConfigureClinicMembership(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ClinicMembership>();
        e.ToTable("clinic_memberships");
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.ClinicId, x.UserId, x.Role }).IsUnique();
        e.HasOne(x => x.User).WithMany(u => u.Memberships).HasForeignKey(x => x.UserId);
        e.HasOne(x => x.Clinic).WithMany(c => c.Memberships).HasForeignKey(x => x.ClinicId);
        ConfigureRowVersion(e);
    }

    private static void ConfigureSubscription(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Subscription>();
        e.ToTable("subscriptions");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.ClinicId);
        e.Property(x => x.PlanName).HasMaxLength(100).IsRequired();
        e.HasOne(x => x.Clinic).WithMany(c => c.Subscriptions).HasForeignKey(x => x.ClinicId);
        ConfigureRowVersion(e);
    }

    private static void ConfigureDependent(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Dependent>();
        e.ToTable("dependents");
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.ClinicId, x.GuardianUserId });
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.RelationshipToGuardian).HasMaxLength(100).IsRequired();
        e.HasOne(x => x.Guardian).WithMany().HasForeignKey(x => x.GuardianUserId);
        ConfigureRowVersion(e);
    }

    private static void ConfigureRefreshToken(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<RefreshToken>();
        e.ToTable("refresh_tokens");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        e.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId);
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AuditLog>();
        e.ToTable("audit_logs");
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.ClinicId, x.Timestamp });
        e.Property(x => x.Action).HasMaxLength(200).IsRequired();
        e.Property(x => x.EntityType).HasMaxLength(200).IsRequired();
        ConfigureRowVersion(e);
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<OutboxNotification>();
        e.ToTable("outbox_notifications");
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.Status, x.CreatedAt });
        e.Property(x => x.PayloadJson).IsRequired();
        ConfigureRowVersion(e);
    }

    private static void ConfigurePushSubscription(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<PushSubscription>();
        e.ToTable("push_subscriptions");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.Endpoint).IsUnique();
        e.HasIndex(x => new { x.ClinicId, x.UserId });
        e.Property(x => x.Endpoint).HasMaxLength(2048).IsRequired();
        e.Property(x => x.P256dh).HasMaxLength(512).IsRequired();
        e.Property(x => x.Auth).HasMaxLength(512).IsRequired();
        e.Property(x => x.UserAgent).HasMaxLength(512);
        ConfigureRowVersion(e);
    }

    private static void ConfigureQueueSession(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<QueueSession>();
        e.ToTable("queue_sessions");
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.ClinicId, x.OperatingDate }).IsUnique();
        e.Property(x => x.PauseReason).HasMaxLength(500);
        e.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        ConfigureRowVersion(e);
    }

    private static void ConfigureAppointment(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Appointment>();
        e.ToTable("appointments");
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.QueueSessionId, x.TemporaryNumber }).IsUnique();
        e.HasIndex(x => new { x.ClinicId, x.QueueSessionId, x.Status });
        e.HasIndex(x => new { x.QueueSessionId, x.RealNumber })
            .IsUnique()
            .HasFilter("\"RealNumber\" IS NOT NULL");

        // FR-041a: at most one active (Booked=0, Present=1) booking per patient-of-record per session.
        e.HasIndex(x => new { x.ClinicId, x.QueueSessionId, x.PatientMembershipId })
            .IsUnique()
            .HasDatabaseName("IX_appointments_active_membership")
            .HasFilter("\"PatientMembershipId\" IS NOT NULL AND \"Status\" IN (0, 1)");
        e.HasIndex(x => new { x.ClinicId, x.QueueSessionId, x.DependentId })
            .IsUnique()
            .HasDatabaseName("IX_appointments_active_dependent")
            .HasFilter("\"DependentId\" IS NOT NULL AND \"Status\" IN (0, 1)");

        e.Property(x => x.InstantEntryReason).HasMaxLength(500);
        e.Property(x => x.DepositProofUrl).HasMaxLength(2048);

        // Deposit-paid no-shows carried at close (FR-065): confirmed deposits still Booked.
        e.HasIndex(x => new { x.QueueSessionId, x.Method, x.Status });

        e.HasOne(x => x.QueueSession)
            .WithMany(s => s.Appointments)
            .HasForeignKey(x => x.QueueSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        ConfigureRowVersion(e);
    }

    private static void ConfigurePatientProfile(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<PatientProfile>();
        e.ToTable("patient_profiles");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.ClinicMembershipId).IsUnique();
        e.HasIndex(x => x.ClinicId);
        e.Property(x => x.BloodType).HasMaxLength(20);
        e.Property(x => x.ChronicConditions).HasMaxLength(2000);
        e.Property(x => x.Allergies).HasMaxLength(2000);
        e.Property(x => x.CurrentMedications).HasMaxLength(2000);
        e.HasOne(x => x.ClinicMembership)
            .WithMany()
            .HasForeignKey(x => x.ClinicMembershipId)
            .OnDelete(DeleteBehavior.Restrict);
        ConfigureRowVersion(e);
    }

    private static void ConfigureVisit(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Visit>();
        e.ToTable("visits");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.AppointmentId).IsUnique();
        e.HasIndex(x => new { x.ClinicId, x.TreatingDoctorUserId });
        e.Property(x => x.Diagnosis).HasMaxLength(2000).IsRequired();
        e.Property(x => x.Notes).HasMaxLength(8000).IsRequired();
        e.HasOne(x => x.Appointment)
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
        ConfigureRowVersion(e);
    }

    private static void ConfigurePrescription(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Prescription>();
        e.ToTable("prescriptions");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.VisitId).IsUnique();
        e.HasIndex(x => x.ClinicId);
        e.Property(x => x.Drug).HasMaxLength(200).IsRequired();
        e.Property(x => x.Dose).HasMaxLength(100).IsRequired();
        e.Property(x => x.Frequency).HasMaxLength(100).IsRequired();
        e.Property(x => x.Duration).HasMaxLength(100).IsRequired();
        e.HasOne(x => x.Visit)
            .WithOne(v => v.Prescription)
            .HasForeignKey<Prescription>(x => x.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
        ConfigureRowVersion(e);
    }

    private static void ConfigureClinicExportJob(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ClinicExportJob>();
        e.ToTable("clinic_export_jobs");
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.ClinicId, x.CreatedAt });
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.PayloadJson).HasColumnType("text");
        e.Property(x => x.Error).HasMaxLength(2000);
        ConfigureRowVersion(e);
    }

    private static void ConfigureRowVersion<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : ClinicScopedEntity
    {
        e.Property(x => x.RowVersion)
            .HasColumnName("row_version")
            .IsRequired()
            .IsConcurrencyToken();
    }
}
