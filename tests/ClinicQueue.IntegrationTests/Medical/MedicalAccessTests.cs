using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Medical;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Auditing;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Medical;

/// <summary>
/// Medical least-privilege: Doctor writes, ClinicOwner read-only, Receptionist forbidden;
/// every read writes an AuditLog entry (FR-085, FR-087).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class MedicalAccessTests(Infrastructure.PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(Guid ClinicId, Guid AppointmentId, Guid DoctorUserId, Guid OwnerUserId, Guid ReceptionistUserId)>
        SeedAppointmentAsync(AppDbContext db)
    {
        var clinicId = Guid.NewGuid();
        var doctor = new User { EmailOrPhone = $"md-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var owner = new User { EmailOrPhone = $"ow-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var receptionist = new User { EmailOrPhone = $"rc-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var patient = new User { EmailOrPhone = $"pt-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var day = ClinicClock.Today("Africa/Cairo");

        db.Users.AddRange(doctor, owner, receptionist, patient);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Medical Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"md-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        var patientMem = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = patient.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.AddRange(
            patientMem,
            new ClinicMembership
            {
                ClinicId = clinicId,
                UserId = doctor.Id,
                Role = ClinicRole.Doctor,
                Status = MembershipStatus.Active
            },
            new ClinicMembership
            {
                ClinicId = clinicId,
                UserId = owner.Id,
                Role = ClinicRole.ClinicOwner,
                Status = MembershipStatus.Active
            },
            new ClinicMembership
            {
                ClinicId = clinicId,
                UserId = receptionist.Id,
                Role = ClinicRole.Receptionist,
                Status = MembershipStatus.Active
            });

        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = day,
            Status = QueueSessionStatus.Open
        };
        db.QueueSessions.Add(session);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = patientMem.Id,
            BookedByUserId = patient.Id,
            TemporaryNumber = 1,
            RealNumber = 1,
            Status = AppointmentStatus.Inside
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (clinicId, appointment.Id, doctor.Id, owner.Id, receptionist.Id);
    }

    [Fact]
    public async Task Doctor_can_write_visit_and_prescription_owner_read_only_receptionist_forbidden()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var (clinicId, appointmentId, doctorId, ownerId, receptionistId) =
            await SeedAppointmentAsync(db);

        var create = new CreateVisitHandler(db, new EfAuditWriter(db));
        var get = new GetVisitHandler(db, new EfAuditWriter(db));
        var update = new UpdateVisitHandler(db, new EfAuditWriter(db));
        var prescribe = new IssuePrescriptionHandler(db, new EfAuditWriter(db));

        var created = await create.HandleAsync(new CreateVisitCommand(
            clinicId,
            appointmentId,
            doctorId,
            ClinicRole.Doctor,
            "Gestational hypertension",
            "Monitor BP"));

        created.Diagnosis.Should().Be("Gestational hypertension");
        created.TreatingDoctorUserId.Should().Be(doctorId);

        // ClinicOwner can read.
        var ownerRead = await get.HandleAsync(new GetVisitByAppointmentCommand(
            clinicId, appointmentId, ownerId, ClinicRole.ClinicOwner));
        ownerRead.Diagnosis.Should().Be("Gestational hypertension");

        var readAudits = await db.AuditLogs
            .Where(a => a.ClinicId == clinicId && a.Action == "VisitRead")
            .ToListAsync();
        readAudits.Should().ContainSingle();
        readAudits[0].ActorUserId.Should().Be(ownerId);
        readAudits[0].EntityId.Should().Be(created.VisitId);

        // ClinicOwner cannot write.
        var ownerWrite = async () => await update.HandleAsync(new UpdateVisitCommand(
            clinicId,
            created.VisitId,
            ownerId,
            ClinicRole.ClinicOwner,
            "Tampered",
            "Nope"));
        await ownerWrite.Should().ThrowAsync<MedicalAccessException>();

        var ownerPrescribe = async () => await prescribe.HandleAsync(new IssuePrescriptionCommand(
            clinicId,
            created.VisitId,
            ownerId,
            ClinicRole.ClinicOwner,
            "Drug",
            "10mg",
            "1x",
            "7d"));
        await ownerPrescribe.Should().ThrowAsync<MedicalAccessException>();

        // Receptionist cannot read or write.
        var receptionistRead = async () => await get.HandleAsync(new GetVisitByAppointmentCommand(
            clinicId, appointmentId, receptionistId, ClinicRole.Receptionist));
        await receptionistRead.Should().ThrowAsync<MedicalAccessException>();

        var receptionistCreate = async () => await create.HandleAsync(new CreateVisitCommand(
            clinicId,
            appointmentId,
            receptionistId,
            ClinicRole.Receptionist,
            "x",
            "y"));
        await receptionistCreate.Should().ThrowAsync<MedicalAccessException>();

        // Treating doctor can issue prescription.
        var rx = await prescribe.HandleAsync(new IssuePrescriptionCommand(
            clinicId,
            created.VisitId,
            doctorId,
            ClinicRole.Doctor,
            "Labetalol",
            "100mg",
            "BID",
            "14 days"));
        rx.Drug.Should().Be("Labetalol");

        // Doctor read also audits.
        await get.HandleAsync(new GetVisitByAppointmentCommand(
            clinicId, appointmentId, doctorId, ClinicRole.Doctor));

        var allReads = await db.AuditLogs.CountAsync(a =>
            a.ClinicId == clinicId && a.Action == "VisitRead");
        allReads.Should().Be(2);
    }

    [Fact]
    public async Task MedicalAccessPolicy_matrix_matches_clarification_q4()
    {
        var doctorId = Guid.NewGuid();
        var otherDoctorId = Guid.NewGuid();

        MedicalAccessPolicy.CanReadClinical(ClinicRole.Doctor).Should().BeTrue();
        MedicalAccessPolicy.CanReadClinical(ClinicRole.ClinicOwner).Should().BeTrue();
        MedicalAccessPolicy.CanReadClinical(ClinicRole.Receptionist).Should().BeFalse();
        MedicalAccessPolicy.CanReadClinical(ClinicRole.Patient).Should().BeFalse();

        MedicalAccessPolicy.CanCreateVisit(ClinicRole.Doctor).Should().BeTrue();
        MedicalAccessPolicy.CanCreateVisit(ClinicRole.ClinicOwner).Should().BeFalse();

        MedicalAccessPolicy.CanWriteClinical(ClinicRole.Doctor, doctorId, doctorId).Should().BeTrue();
        MedicalAccessPolicy.CanWriteClinical(ClinicRole.Doctor, otherDoctorId, doctorId).Should().BeFalse();
        MedicalAccessPolicy.CanWriteClinical(ClinicRole.ClinicOwner, doctorId, doctorId).Should().BeFalse();
    }
}
