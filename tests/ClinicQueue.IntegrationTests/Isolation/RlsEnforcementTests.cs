using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Medical;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Platform;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Domain.Reporting;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ClinicQueue.IntegrationTests.Isolation;

/// <summary>
/// Release gate for the FINAL isolation layer: PostgreSQL Row-Level Security itself
/// (constitution Principle I / Golden Rule #1). Unlike <see cref="TenantIsolationTests"/>, this
/// deliberately BYPASSES the repository/DbContext filters and hits the database with raw SQL as
/// the real application role (<c>clinicqueue_app</c>, NOBYPASSRLS). It proves that even if the
/// app layer had a bug, the database blocks cross-tenant reads AND writes, and fails closed when
/// no tenant is set. These tests fail if someone removes FORCE ROW LEVEL SECURITY, drops the
/// policy, or connects the app as an owner/BYPASSRLS role.
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class RlsEnforcementTests(Infrastructure.PostgresFixture fixture)
{
    // Every clinic-scoped table (snake_case), each with a "ClinicId" column under RLS.
    private static readonly string[] TenantTables =
    {
        "clinic_memberships",
        "subscriptions",
        "dependents",
        "audit_logs",
        "outbox_notifications",
        "push_subscriptions",
        "queue_sessions",
        "appointments",
        "patient_profiles",
        "visits",
        "prescriptions",
        "clinic_export_jobs",
        "ratings",
    };

    private AppDbContext OwnerContext()
    {
        // Owner/superuser context — bypasses RLS. Used only to seed both clinics.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString, Guid? scopedClinic)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Mirror RlsConnectionInterceptor: scope the RLS session variable per connection.
        // When scopedClinic is null we set NOTHING → current_setting(...) is unset → fail closed.
        if (scopedClinic is { } clinic)
        {
            await using var set = connection.CreateCommand();
            set.CommandText = "SELECT set_config('app.current_clinic', @c, false)";
            set.Parameters.AddWithValue("c", clinic.ToString());
            await set.ExecuteNonQueryAsync();
        }

        return connection;
    }

    private static async Task<long> ScalarCountAsync(NpgsqlConnection connection, string sql, Guid? clinic = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (clinic is { } c)
        {
            command.Parameters.AddWithValue("c", c);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, Guid clinic)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("c", clinic);
        return await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Rls_blocks_cross_tenant_reads_and_is_fail_closed_and_forced()
    {
        await using var setup = OwnerContext();
        await setup.Database.MigrateAsync();

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await SeedClinicAsync(setup, clinicA);
        await SeedClinicAsync(setup, clinicB);
        await setup.SaveChangesAsync();

        // 1) FORCE + ENABLE on every tenant table (fails if a future migration drops FORCE).
        await using (var owner = await OpenAsync(fixture.ConnectionString, scopedClinic: null))
        {
            foreach (var table in TenantTables)
            {
                await using var command = owner.CreateCommand();
                command.CommandText =
                    "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname = @t";
                command.Parameters.AddWithValue("t", table);
                await using var reader = await command.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue($"{table} must exist");
                reader.GetBoolean(0).Should().BeTrue($"{table} must have ROW LEVEL SECURITY enabled");
                reader.GetBoolean(1).Should().BeTrue($"{table} must have ROW LEVEL SECURITY forced");
            }
        }

        // 2) As the app role scoped to A: only A's rows are visible, never B's.
        await using (var appAsA = await OpenAsync(fixture.AppConnectionString, clinicA))
        {
            foreach (var table in TenantTables)
            {
                var visibleForB = await ScalarCountAsync(
                    appAsA, $"SELECT count(*) FROM {table} WHERE \"ClinicId\" = @c", clinicB);
                visibleForB.Should().Be(0, $"clinic B rows in {table} must be invisible under tenant A");

                var visibleTotal = await ScalarCountAsync(appAsA, $"SELECT count(*) FROM {table}");
                visibleTotal.Should().BeGreaterThan(0, $"clinic A's own {table} rows must be visible");

                var visibleForeign = await ScalarCountAsync(
                    appAsA, $"SELECT count(*) FROM {table} WHERE \"ClinicId\" <> @c", clinicA);
                visibleForeign.Should().Be(0, $"no non-A rows in {table} may be visible under tenant A");
            }
        }

        // 3) Fail-closed: app role with NO app.current_clinic set → zero rows everywhere.
        await using (var appUnset = await OpenAsync(fixture.AppConnectionString, scopedClinic: null))
        {
            foreach (var table in TenantTables)
            {
                var count = await ScalarCountAsync(appUnset, $"SELECT count(*) FROM {table}");
                count.Should().Be(0, $"{table} must return zero rows when the tenant is unset (fail-closed)");
            }
        }

        // 4) The BYPASSRLS role legitimately sees both clinics (cross-tenant path).
        await using (var bypass = await OpenAsync(fixture.BypassConnectionString, scopedClinic: null))
        {
            foreach (var table in TenantTables)
            {
                var both = await ScalarCountAsync(bypass, $"SELECT count(*) FROM {table}");
                both.Should().BeGreaterThanOrEqualTo(2, $"BYPASSRLS role must see both clinics' {table} rows");
            }
        }
    }

    [Fact]
    public async Task Rls_blocks_cross_tenant_writes()
    {
        await using var setup = OwnerContext();
        await setup.Database.MigrateAsync();

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await SeedClinicAsync(setup, clinicA);
        await SeedClinicAsync(setup, clinicB);
        await setup.SaveChangesAsync();

        // Scoped to A, attempt to UPDATE and DELETE clinic B's rows: RLS makes them invisible,
        // so every statement affects ZERO rows.
        await using (var appAsA = await OpenAsync(fixture.AppConnectionString, clinicA))
        {
            foreach (var table in TenantTables)
            {
                var updated = await ExecuteAsync(
                    appAsA, $"UPDATE {table} SET \"CreatedAt\" = \"CreatedAt\" WHERE \"ClinicId\" = @c", clinicB);
                updated.Should().Be(0, $"tenant A must not UPDATE clinic B rows in {table}");

                var deleted = await ExecuteAsync(
                    appAsA, $"DELETE FROM {table} WHERE \"ClinicId\" = @c", clinicB);
                deleted.Should().Be(0, $"tenant A must not DELETE clinic B rows in {table}");
            }
        }

        // Confirm B's rows survived, via the BYPASSRLS role.
        await using (var bypass = await OpenAsync(fixture.BypassConnectionString, scopedClinic: null))
        {
            foreach (var table in TenantTables)
            {
                var remaining = await ScalarCountAsync(
                    bypass, $"SELECT count(*) FROM {table} WHERE \"ClinicId\" = @c", clinicB);
                remaining.Should().BeGreaterThan(0, $"clinic B rows in {table} must be untouched");
            }
        }
    }

    /// <summary>Seeds one row in every clinic-scoped table for the given clinic (owner connection).</summary>
    private static async Task SeedClinicAsync(AppDbContext db, Guid clinicId)
    {
        var user = new User { EmailOrPhone = $"rls-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "RLS Clinic",
            Specialty = "OB",
            SubdomainSlug = $"rls-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo",
        });
        await db.SaveChangesAsync();

        var membership = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active,
        };
        db.ClinicMemberships.Add(membership);
        db.Subscriptions.Add(new Subscription
        {
            ClinicId = clinicId,
            PlanName = "standard",
            Status = SubscriptionStatus.Active,
            RenewalDate = DateTime.UtcNow.AddDays(30),
        });
        db.Dependents.Add(new Dependent
        {
            ClinicId = clinicId,
            GuardianUserId = user.Id,
            Name = "Child",
            DateOfBirth = new DateOnly(2018, 1, 1),
            RelationshipToGuardian = "child",
        });
        db.AuditLogs.Add(new Domain.Common.AuditLog
        {
            ClinicId = clinicId,
            ActorUserId = user.Id,
            Action = "read",
            EntityType = "Visit",
            Timestamp = DateTime.UtcNow,
        });
        db.OutboxNotifications.Add(new OutboxNotification
        {
            ClinicId = clinicId,
            RecipientUserId = user.Id,
            Type = NotificationType.BookingConfirmation,
            PayloadJson = "{}",
        });
        db.PushSubscriptions.Add(new PushSubscription
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Endpoint = $"https://push.test/{Guid.NewGuid():N}",
            P256dh = "p",
            Auth = "a",
        });
        db.ClinicExportJobs.Add(new ClinicExportJob
        {
            ClinicId = clinicId,
            RequestedByUserId = user.Id,
            Status = ClinicExportJobStatus.Pending,
        });

        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = QueueSessionStatus.Open,
        };
        db.QueueSessions.Add(session);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membership.Id,
            BookedByUserId = user.Id,
            TemporaryNumber = 1,
            Status = AppointmentStatus.Booked,
            Method = AppointmentMethod.WalkIn,
        };
        db.Appointments.Add(appointment);
        db.PatientProfiles.Add(new PatientProfile
        {
            ClinicId = clinicId,
            ClinicMembershipId = membership.Id,
            BloodType = "A+",
        });
        await db.SaveChangesAsync();

        var visit = new Visit
        {
            ClinicId = clinicId,
            AppointmentId = appointment.Id,
            TreatingDoctorUserId = user.Id,
            Diagnosis = "dx",
            Notes = "notes",
        };
        db.Visits.Add(visit);
        db.Ratings.Add(new Rating
        {
            ClinicId = clinicId,
            AppointmentId = appointment.Id,
            Score = 5,
            Comment = "ok",
        });
        await db.SaveChangesAsync();

        db.Prescriptions.Add(new Prescription
        {
            ClinicId = clinicId,
            VisitId = visit.Id,
            Drug = "drug",
            Dose = "1mg",
            Frequency = "qd",
            Duration = "7d",
        });
        await db.SaveChangesAsync();
    }
}
