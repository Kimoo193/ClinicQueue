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
/// Patient self-service medical surface: profile read/update and per-clinic visit
/// history, each access audit-logged (FR-080, FR-083, FR-086, FR-087).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class PatientMedicalHistoryTests(Infrastructure.PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(Guid ClinicId, Guid PatientMembershipId, Guid PatientUserId, Guid DoctorUserId, Guid OtherPatientUserId)>
        SeedClinicWithPatientAsync(AppDbContext db)
    {
        var clinicId = Guid.NewGuid();
        var patient = new User { EmailOrPhone = $"pt-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var otherPatient = new User { EmailOrPhone = $"pt2-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var doctor = new User { EmailOrPhone = $"md-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };

        db.Users.AddRange(patient, otherPatient, doctor);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "History Clinic",
            Specialty = "GP",
            SubdomainSlug = $"hx-{Guid.NewGuid():N}"[..20],
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
                UserId = otherPatient.Id,
                Role = ClinicRole.Patient,
                Status = MembershipStatus.Active
            },
            new ClinicMembership
            {
                ClinicId = clinicId,
                UserId = doctor.Id,
                Role = ClinicRole.Doctor,
                Status = MembershipStatus.Active
            });
        await db.SaveChangesAsync();

        return (clinicId, patientMem.Id, patient.Id, doctor.Id, otherPatient.Id);
    }

    [Fact]
    public async Task Patient_can_read_and_update_own_profile_doctor_reads_but_other_patient_forbidden()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var (clinicId, membershipId, patientId, doctorId, otherPatientId) =
            await SeedClinicWithPatientAsync(db);

        var get = new GetPatientProfileHandler(db, new EfAuditWriter(db));
        var update = new UpdatePatientProfileHandler(db, new EfAuditWriter(db));

        // No profile row yet: patient GET returns defaults, not 404.
        var initial = await get.HandleAsync(new GetPatientProfileQuery(clinicId, membershipId, patientId));
        initial.BloodType.Should().BeNull();

        var updated = await update.HandleAsync(new UpdatePatientProfileCommand(
            clinicId, membershipId, patientId, "O+", "Asthma", "Penicillin", "Ventolin"));
        updated.BloodType.Should().Be("O+");
        updated.Allergies.Should().Be("Penicillin");

        // Doctor can read (clinical staff), but cannot update.
        var doctorRead = await get.HandleAsync(new GetPatientProfileQuery(clinicId, membershipId, doctorId));
        doctorRead.BloodType.Should().Be("O+");

        var doctorUpdate = async () => await update.HandleAsync(new UpdatePatientProfileCommand(
            clinicId, membershipId, doctorId, "AB-", null, null, null));
        await doctorUpdate.Should().ThrowAsync<MedicalAccessException>();

        // A different patient may not read this profile.
        var strangerRead = async () => await get.HandleAsync(
            new GetPatientProfileQuery(clinicId, membershipId, otherPatientId));
        await strangerRead.Should().ThrowAsync<MedicalAccessException>();

        // Every successful read is audited (FR-087): self GET, doctor GET — 2 entries.
        var profileReads = await db.AuditLogs.CountAsync(a =>
            a.ClinicId == clinicId && a.Action == "ProfileRead");
        profileReads.Should().Be(2);

        var profileUpdates = await db.AuditLogs.CountAsync(a =>
            a.ClinicId == clinicId && a.Action == "ProfileUpdate");
        profileUpdates.Should().Be(1);
    }

    [Fact]
    public async Task Patient_visit_history_lists_own_visits_with_prescriptions_and_audits_each_read()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var (clinicId, membershipId, patientId, doctorId, otherPatientId) =
            await SeedClinicWithPatientAsync(db);

        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = ClinicClock.Today("Africa/Cairo"),
            Status = QueueSessionStatus.Open
        };
        db.QueueSessions.Add(session);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membershipId,
            BookedByUserId = patientId,
            TemporaryNumber = 1,
            RealNumber = 1,
            Status = AppointmentStatus.Completed
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var createVisit = new CreateVisitHandler(db, new EfAuditWriter(db));
        var prescribe = new IssuePrescriptionHandler(db, new EfAuditWriter(db));

        var visit = await createVisit.HandleAsync(new CreateVisitCommand(
            clinicId, appointment.Id, doctorId, ClinicRole.Doctor, "Seasonal flu", "Rest and fluids"));
        await prescribe.HandleAsync(new IssuePrescriptionCommand(
            clinicId, visit.VisitId, doctorId, ClinicRole.Doctor, "Paracetamol", "500mg", "TID", "5 days"));

        var listHandler = new ListPatientVisitsHandler(db, new EfAuditWriter(db));

        var history = await listHandler.HandleAsync(new ListPatientVisitsQuery(clinicId, patientId));
        history.Should().ContainSingle();
        history[0].Diagnosis.Should().Be("Seasonal flu");
        history[0].Prescription.Should().NotBeNull();
        history[0].Prescription!.Drug.Should().Be("Paracetamol");

        // A patient with no visits at this clinic gets an empty list, not an error.
        var otherHistory = await listHandler.HandleAsync(new ListPatientVisitsQuery(clinicId, otherPatientId));
        otherHistory.Should().BeEmpty();

        var visitReadAudits = await db.AuditLogs.CountAsync(a =>
            a.ClinicId == clinicId && a.Action == "VisitRead" && a.EntityId == visit.VisitId);
        visitReadAudits.Should().Be(1);
    }
}
