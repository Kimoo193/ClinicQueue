using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Emergency;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Auditing;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Queue;

/// <summary>
/// Pause notifies all pending/confirmed patients and preserves positions; resume continues
/// from the stop point (FR-050–FR-053).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class PauseResumeTests(Infrastructure.PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static PauseQueueSessionHandler CreatePauseHandler(AppDbContext db) =>
        new(db, new EfAuditWriter(db));

    private static ResumeQueueSessionHandler CreateResumeHandler(AppDbContext db) =>
        new(db, new EfAuditWriter(db));

    [Fact]
    public async Task Pause_notifies_booked_and_present_patients_and_preserves_positions()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var doctor = new User { EmailOrPhone = $"doc-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var patientA = new User { EmailOrPhone = $"pa-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var patientB = new User { EmailOrPhone = $"pb-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var day = ClinicClock.Today("Africa/Cairo");

        db.Users.AddRange(doctor, patientA, patientB);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Pause Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"ps-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        var memA = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = patientA.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        var memB = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = patientB.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.AddRange(memA, memB);

        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = day,
            Status = QueueSessionStatus.Open,
            NormalSinceLastUrgent = 1
        };
        db.QueueSessions.Add(session);
        await db.SaveChangesAsync();

        var booked = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = memA.Id,
            BookedByUserId = patientA.Id,
            TemporaryNumber = 1,
            Status = AppointmentStatus.Booked
        };
        var present = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = memB.Id,
            BookedByUserId = patientB.Id,
            TemporaryNumber = 2,
            RealNumber = 1,
            Status = AppointmentStatus.Present
        };
        db.Appointments.AddRange(booked, present);
        await db.SaveChangesAsync();

        var pause = await CreatePauseHandler(db).HandleAsync(
            new PauseQueueSessionCommand(clinicId, session.Id, doctor.Id, "Equipment failure"));

        pause.Status.Should().Be(nameof(QueueSessionStatus.Paused));
        pause.PausedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        var reloaded = await db.QueueSessions.SingleAsync(s => s.Id == session.Id);
        reloaded.Status.Should().Be(QueueSessionStatus.Paused);
        reloaded.PauseReason.Should().Be("Equipment failure");
        reloaded.PausedByUserId.Should().Be(doctor.Id);
        reloaded.NormalSinceLastUrgent.Should().Be(1);

        var appointments = await db.Appointments
            .Where(a => a.QueueSessionId == session.Id)
            .OrderBy(a => a.TemporaryNumber)
            .ToListAsync();
        appointments[0].TemporaryNumber.Should().Be(1);
        appointments[0].RealNumber.Should().BeNull();
        appointments[0].Status.Should().Be(AppointmentStatus.Booked);
        appointments[1].TemporaryNumber.Should().Be(2);
        appointments[1].RealNumber.Should().Be(1);
        appointments[1].Status.Should().Be(AppointmentStatus.Present);

        var outbox = await db.OutboxNotifications
            .Where(n => n.ClinicId == clinicId && n.Type == NotificationType.PauseResume)
            .ToListAsync();
        outbox.Should().HaveCount(2);
        outbox.Select(n => n.RecipientUserId).Should().BeEquivalentTo([patientA.Id, patientB.Id]);
        outbox.Should().OnlyContain(n => n.PayloadJson.Contains("pause"));

        var audit = await db.AuditLogs
            .Where(a => a.ClinicId == clinicId && a.Action == "QueuePause")
            .ToListAsync();
        audit.Should().ContainSingle();
        audit[0].ActorUserId.Should().Be(doctor.Id);
        audit[0].EntityId.Should().Be(session.Id);
    }

    [Fact]
    public async Task Resume_continues_from_stop_point_and_notifies_waiting_patients()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var doctor = new User { EmailOrPhone = $"rd-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var patient = new User { EmailOrPhone = $"rp-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var day = ClinicClock.Today("Africa/Cairo");

        db.Users.AddRange(doctor, patient);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Resume Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"rs-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        var membership = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = patient.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.Add(membership);

        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = day,
            Status = QueueSessionStatus.Paused,
            PauseReason = "Power cut",
            PausedAt = DateTime.UtcNow.AddMinutes(-10),
            PausedByUserId = doctor.Id,
            NormalSinceLastUrgent = 2
        };
        db.QueueSessions.Add(session);
        await db.SaveChangesAsync();

        db.Appointments.Add(new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membership.Id,
            BookedByUserId = patient.Id,
            TemporaryNumber = 3,
            RealNumber = 2,
            Status = AppointmentStatus.Present
        });
        await db.SaveChangesAsync();

        var resume = await CreateResumeHandler(db).HandleAsync(
            new ResumeQueueSessionCommand(clinicId, session.Id, doctor.Id));

        resume.Status.Should().Be(nameof(QueueSessionStatus.Open));

        var reloaded = await db.QueueSessions.SingleAsync(s => s.Id == session.Id);
        reloaded.Status.Should().Be(QueueSessionStatus.Open);
        reloaded.PauseReason.Should().BeNull();
        reloaded.PausedAt.Should().BeNull();
        reloaded.PausedByUserId.Should().BeNull();
        reloaded.NormalSinceLastUrgent.Should().Be(2);

        var appointment = await db.Appointments.SingleAsync(a => a.QueueSessionId == session.Id);
        appointment.RealNumber.Should().Be(2);
        appointment.TemporaryNumber.Should().Be(3);
        appointment.Status.Should().Be(AppointmentStatus.Present);

        var outbox = await db.OutboxNotifications
            .Where(n => n.ClinicId == clinicId && n.Type == NotificationType.PauseResume)
            .ToListAsync();
        outbox.Should().ContainSingle();
        outbox[0].RecipientUserId.Should().Be(patient.Id);
        outbox[0].PayloadJson.Should().Contain("resume");

        var audit = await db.AuditLogs
            .Where(a => a.ClinicId == clinicId && a.Action == "QueueResume")
            .ToListAsync();
        audit.Should().ContainSingle();
        audit[0].ActorUserId.Should().Be(doctor.Id);
    }
}
