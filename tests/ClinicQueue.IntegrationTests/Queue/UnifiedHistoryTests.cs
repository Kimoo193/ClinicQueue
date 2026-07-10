using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Platform;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Application.Queue.Sessions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Queue;

/// <summary>
/// Unified appointment list spans two clinics for one global patient identity (FR-006).
/// Also covers cancel categorization (FR-044/FR-045) and dependent booking (FR-046).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class UnifiedHistoryTests(Infrastructure.PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static CreateAppointmentHandler CreateBookingHandler(AppDbContext db)
    {
        var countAhead = new CountAheadService(db);
        var gate = new ClinicAccessGate(db);
        return new CreateAppointmentHandler(
            db,
            new QueueSessionService(db, gate),
            new QueueNotificationRules(db, countAhead),
            gate);
    }

    private static ListPatientAppointmentsHandler CreateListHandler(AppDbContext db) =>
        new(db, new CountAheadService(db));

    private static DateOnly ClinicToday(string timezone = "Africa/Cairo") =>
        ClinicClock.Today(timezone);

    [Fact]
    public async Task Unified_appointment_list_spans_two_clinics_for_one_global_identity()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"unified-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        db.Clinics.AddRange(
            new Clinic
            {
                Id = clinicA,
                Name = "Clinic Alpha",
                Specialty = "OB/GYN",
                ContactInfo = "Cairo Downtown",
                SubdomainSlug = $"ua-{Guid.NewGuid():N}"[..20],
                Timezone = "Africa/Cairo"
            },
            new Clinic
            {
                Id = clinicB,
                Name = "Clinic Beta",
                Specialty = "Dermatology",
                ContactInfo = "Giza",
                SubdomainSlug = $"ub-{Guid.NewGuid():N}"[..20],
                Timezone = "Africa/Cairo"
            });
        await db.SaveChangesAsync();

        var memA = new ClinicMembership
        {
            ClinicId = clinicA,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        var memB = new ClinicMembership
        {
            ClinicId = clinicB,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.AddRange(memA, memB);
        await db.SaveChangesAsync();

        var day = ClinicToday();
        var book = CreateBookingHandler(db);

        var bookingA = await book.HandleAsync(new CreateAppointmentCommand(
            clinicA, user.Id, day, AppointmentMethod.WalkIn, 0, false, null));
        var bookingB = await book.HandleAsync(new CreateAppointmentCommand(
            clinicB, user.Id, day, AppointmentMethod.WalkIn, 0, false, null));

        var list = await CreateListHandler(db).HandleAsync(
            new ListPatientAppointmentsQuery(user.Id, "upcoming", UnifiedAcrossClinics: true));

        list.Should().HaveCount(2);
        list.Select(a => a.ClinicId).Should().BeEquivalentTo([clinicA, clinicB]);
        list.Select(a => a.AppointmentId).Should().BeEquivalentTo(
            [bookingA.AppointmentId, bookingB.AppointmentId]);
    }

    [Fact]
    public async Task Cancel_moves_appointment_to_cancelled_category_in_unified_history()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"cancel-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Cancel Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"uc-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        });
        await db.SaveChangesAsync();

        var day = ClinicToday();
        var created = await CreateBookingHandler(db).HandleAsync(new CreateAppointmentCommand(
            clinicId, user.Id, day, AppointmentMethod.WalkIn, 0, false, null));

        var cancel = await new CancelAppointmentHandler(db).HandleAsync(
            new CancelAppointmentCommand(created.AppointmentId, user.Id, clinicId, IsStaff: false));

        cancel.Status.Should().Be(nameof(AppointmentStatus.Cancelled));

        var upcoming = await CreateListHandler(db).HandleAsync(
            new ListPatientAppointmentsQuery(user.Id, "upcoming"));
        upcoming.Should().NotContain(a => a.AppointmentId == created.AppointmentId);

        var cancelled = await CreateListHandler(db).HandleAsync(
            new ListPatientAppointmentsQuery(user.Id, "cancelled"));
        cancelled.Should().ContainSingle(a => a.AppointmentId == created.AppointmentId);
    }

    [Fact]
    public async Task Dependent_booking_is_distinct_patient_of_record_and_appears_in_guardian_history()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var guardian = new User
        {
            EmailOrPhone = $"guard-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.Add(guardian);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Dependent Clinic",
            Specialty = "Pediatrics",
            SubdomainSlug = $"ud-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = guardian.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        });
        await db.SaveChangesAsync();

        var registered = await new RegisterDependentHandler(db).HandleAsync(
            new RegisterDependentCommand(
                clinicId,
                guardian.Id,
                "Child One",
                new DateOnly(2018, 5, 1),
                "Child"));

        var day = ClinicToday();
        var book = CreateBookingHandler(db);

        // Guardian self-booking
        var self = await book.HandleAsync(new CreateAppointmentCommand(
            clinicId, guardian.Id, day, AppointmentMethod.WalkIn, 0, false, null));

        // Dependent booking same day — distinct patient-of-record (FR-041a / FR-046)
        var dependentBooking = await new DependentBookingHandler(book, db).HandleAsync(
            new DependentBookingCommand(
                clinicId,
                guardian.Id,
                registered.DependentId,
                day,
                AppointmentMethod.WalkIn,
                CompanionsCount: 0,
                PossibleUrgentFlag: false));

        var appt = await db.Appointments.SingleAsync(a => a.Id == dependentBooking.AppointmentId);
        appt.DependentId.Should().Be(registered.DependentId);
        appt.PatientMembershipId.Should().BeNull();
        appt.BookedByUserId.Should().Be(guardian.Id);

        var history = await CreateListHandler(db).HandleAsync(
            new ListPatientAppointmentsQuery(guardian.Id, "upcoming"));

        history.Should().HaveCount(2);
        history.Should().Contain(a => a.AppointmentId == self.AppointmentId && a.DependentId == null);
        history.Should().Contain(a =>
            a.AppointmentId == dependentBooking.AppointmentId
            && a.DependentId == registered.DependentId);
    }

    [Fact]
    public async Task Clinic_search_filters_by_specialty_name_and_location()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var slug = Guid.NewGuid().ToString("N")[..8];
        db.Clinics.AddRange(
            new Clinic
            {
                Name = $"Searchable Heart {slug}",
                Specialty = "Cardiology",
                ContactInfo = "Alexandria Corniche",
                SubdomainSlug = $"cs1-{slug}",
                Status = ClinicStatus.Active
            },
            new Clinic
            {
                Name = $"Other Skin {slug}",
                Specialty = "Dermatology",
                ContactInfo = "Cairo Nasr City",
                SubdomainSlug = $"cs2-{slug}",
                Status = ClinicStatus.Active
            },
            new Clinic
            {
                Name = $"Suspended Heart {slug}",
                Specialty = "Cardiology",
                ContactInfo = "Alexandria",
                SubdomainSlug = $"cs3-{slug}",
                Status = ClinicStatus.Suspended
            });
        await db.SaveChangesAsync();

        var search = new ClinicSearchHandler(db);

        var bySpecialty = await search.HandleAsync(new ClinicSearchQuery(Specialty: "cardio"));
        bySpecialty.Should().Contain(c => c.Name.Contains($"Searchable Heart {slug}"));
        bySpecialty.Should().NotContain(c => c.Name.Contains($"Suspended Heart {slug}"));

        var byName = await search.HandleAsync(new ClinicSearchQuery(Name: $"Skin {slug}"));
        byName.Should().ContainSingle(c => c.Name.Contains($"Other Skin {slug}"));

        var byLocation = await search.HandleAsync(new ClinicSearchQuery(Location: "corniche"));
        byLocation.Should().Contain(c => c.Name.Contains($"Searchable Heart {slug}"));
    }
}
