using ClinicQueue.Domain.Common;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Medical;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Domain.Reporting;
using ClinicQueue.Infrastructure.Identity;
using ClinicQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ClinicQueue.Seeder;

/// <summary>
/// Builds a realistic two-clinic demo that exercises every screen and every queue state.
///
/// Role discipline (matches production, see docs/architecture.md and Golden Rule #1):
///   • <see cref="PrivilegedDbContext"/> (BYPASSRLS) for the pre-auth / no-tenant writes only —
///     clinics, users, memberships, subscriptions — plus the clean-up sweep (a cross-tenant op).
///   • An <see cref="AppDbContext"/> on the RLS-enforced app role for EVERY tenant-scoped write,
///     with <c>app.current_clinic</c> pinned to the target clinic so the RLS WITH CHECK passes
///     exactly as a real request would. The seed never touches the owner/superuser connection for data.
/// Idempotent: clean-then-seed keyed on the demo slugs and e-mail domains.
/// </summary>
public sealed class DemoSeeder
{
    // Stable clinic ids so QR links and manual testing survive re-seeds.
    private static readonly Guid GhadaId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SmileId = new("22222222-2222-2222-2222-222222222222");

    private const string Password = "Test@1234";
    private const string BaseUrl = "https://app.clinicqueue.app";

    // Demo identities are cleaned by these e-mail domains, so a re-run never accumulates cruft.
    private static readonly string[] DemoEmailLikePatterns =
    [
        "%@clinic.test",
        "%@seed.local",
        "admin@clinicqueue.app",
    ];

    private readonly string _privilegedConn;
    private readonly string _appConn;
    private readonly string _passwordHash;
    private readonly Random _rng = new(20260712);

    public DemoSeeder(string privilegedConn, string appConn)
    {
        _privilegedConn = privilegedConn;
        _appConn = appConn;
        _passwordHash = new PasswordHasher().Hash(Password);
    }

    public async Task<SeedResult> RunAsync()
    {
        await CleanAsync();

        var ids = await SeedIdentityAsync();

        await SeedGhadaTenantDataAsync(ids);
        await SeedSmileTenantDataAsync(ids);

        return BuildResult(ids);
    }

    // ── 1. Clean-up (privileged / BYPASSRLS: a cross-tenant maintenance sweep) ──────────────────
    private async Task CleanAsync()
    {
        Console.Write("• Cleaning previous demo data (privileged role)… ");
        await using var db = PrivilegedContext();

        var clinicIds = new[] { GhadaId, SmileId };

        // Child-to-parent order to respect FKs. All tenant tables filtered by ClinicId.
        await db.Ratings.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.Prescriptions.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.Visits.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.Appointments.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.PatientProfiles.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.QueueSessions.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.Dependents.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.OutboxNotifications.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.PushSubscriptions.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.Subscriptions.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();
        await db.ClinicMemberships.Where(x => clinicIds.Contains(x.ClinicId)).ExecuteDeleteAsync();

        var demoUserIds = new List<Guid>();
        foreach (var pattern in DemoEmailLikePatterns)
        {
            var matches = await db.Users
                .Where(u => EF.Functions.Like(u.EmailOrPhone, pattern))
                .Select(u => u.Id)
                .ToListAsync();
            demoUserIds.AddRange(matches);
        }

        if (demoUserIds.Count > 0)
        {
            await db.RefreshTokens.Where(t => demoUserIds.Contains(t.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(u => demoUserIds.Contains(u.Id)).ExecuteDeleteAsync();
        }

        await db.Clinics.Where(c => clinicIds.Contains(c.Id)).ExecuteDeleteAsync();
        Console.WriteLine("done.");
    }

    // ── 2. Identity: clinics, users, memberships, subscriptions (privileged / pre-auth writes) ──
    private async Task<SeededIds> SeedIdentityAsync()
    {
        Console.Write("• Seeding identity: clinics, users, memberships, subscriptions (privileged role)… ");
        await using var db = PrivilegedContext();
        var ids = new SeededIds();

        // Clinics (non-tenant table).
        db.Clinics.Add(new Clinic
        {
            Id = GhadaId,
            Name = "عيادة د. غادة كامل / Dr. Ghada Kamel Clinic",
            Specialty = "Obstetrics & Gynecology",
            PrimaryColor = "#1B3A6B",
            SecondaryColor = "#5BB8C1",
            UrgentRatio = 2,
            HeadToClinicThreshold = 4,
            AutoAbsenceTimeoutMinutes = 15,
            RepeatedAbsenceThreshold = 2,
            Timezone = "Africa/Cairo",
            SubdomainSlug = "ghada",
            QrCodeUrl = $"{BaseUrl}/r/ghada",
            Status = ClinicStatus.Active,
        });
        db.Clinics.Add(new Clinic
        {
            Id = SmileId,
            Name = "عيادة سمايل للأسنان / Smile Dental Clinic",
            Specialty = "Dentistry",
            PrimaryColor = "#0E7C7B",
            SecondaryColor = "#F4A259",
            UrgentRatio = 2,
            HeadToClinicThreshold = 4,
            AutoAbsenceTimeoutMinutes = 15,
            RepeatedAbsenceThreshold = 2,
            Timezone = "Africa/Cairo",
            SubdomainSlug = "smile",
            QrCodeUrl = $"{BaseUrl}/r/smile",
            Status = ClinicStatus.Active,
        });

        // Pro subscriptions, active, renewing in 30 days.
        db.Subscriptions.Add(NewSubscription(GhadaId, "Pro"));
        db.Subscriptions.Add(NewSubscription(SmileId, "Pro"));

        // Named accounts.
        ids.Admin = AddUser(db, "admin@clinicqueue.app", isSuperAdmin: true);
        ids.GhadaOwner = AddUser(db, "ghada@clinic.test");
        ids.Doctor = AddUser(db, "doctor@clinic.test");
        ids.Reception = AddUser(db, "reception@clinic.test");
        ids.Patient1 = AddUser(db, "patient1@clinic.test");
        ids.Patient2 = AddUser(db, "patient2@clinic.test");
        ids.Patient3 = AddUser(db, "patient3@clinic.test");
        ids.Patient4 = AddUser(db, "patient4@clinic.test");
        ids.Urgent = AddUser(db, "urgent@clinic.test");
        ids.Multi = AddUser(db, "multi@clinic.test");
        ids.Newbie = AddUser(db, "newbie@clinic.test"); // deliberately NO membership anywhere.

        // Pool of anonymous Ghada patients used to give the queue depth and 60 days of history.
        for (var i = 1; i <= PoolSize; i++)
        {
            ids.GhadaPool.Add(AddUser(db, $"hpat{i:D3}@seed.local"));
        }

        // Two Smile patients (besides multi@) so a switch shows a distinct live queue.
        ids.SmilePool.Add(AddUser(db, "spat001@seed.local"));
        ids.SmilePool.Add(AddUser(db, "spat002@seed.local"));

        await db.SaveChangesAsync();

        // Memberships (tenant table, but written pre-auth exactly like onboarding → privileged).
        ids.GhadaOwnerMembership = AddMembership(db, GhadaId, ids.GhadaOwner, ClinicRole.ClinicOwner);
        ids.DoctorMembership = AddMembership(db, GhadaId, ids.Doctor, ClinicRole.Doctor);
        ids.ReceptionMembership = AddMembership(db, GhadaId, ids.Reception, ClinicRole.Receptionist);
        ids.Patient1Membership = AddMembership(db, GhadaId, ids.Patient1, ClinicRole.Patient);
        ids.Patient2Membership = AddMembership(db, GhadaId, ids.Patient2, ClinicRole.Patient);
        ids.Patient3Membership = AddMembership(db, GhadaId, ids.Patient3, ClinicRole.Patient);
        ids.Patient4Membership = AddMembership(db, GhadaId, ids.Patient4, ClinicRole.Patient);
        ids.UrgentMembership = AddMembership(db, GhadaId, ids.Urgent, ClinicRole.Patient);

        // THE switcher account: Receptionist at Ghada + Patient at Smile.
        AddMembership(db, GhadaId, ids.Multi, ClinicRole.Receptionist);
        ids.MultiSmileMembership = AddMembership(db, SmileId, ids.Multi, ClinicRole.Patient);

        foreach (var poolUser in ids.GhadaPool)
        {
            ids.GhadaPoolMemberships.Add(AddMembership(db, GhadaId, poolUser, ClinicRole.Patient));
        }
        foreach (var poolUser in ids.SmilePool)
        {
            ids.SmilePoolMemberships.Add(AddMembership(db, SmileId, poolUser, ClinicRole.Patient));
        }

        await db.SaveChangesAsync();
        Console.WriteLine("done.");
        return ids;
    }

    // ── 3. Ghada tenant data (app role, app.current_clinic = Ghada) ─────────────────────────────
    private async Task SeedGhadaTenantDataAsync(SeededIds ids)
    {
        Console.Write("• Seeding Dr. Ghada tenant data: today's live queue + 60 days history (app role)… ");
        await using var db = await OpenAppContextAsync(GhadaId);

        await SeedGhadaHistoryAsync(db, ids);
        await SeedGhadaTodayAsync(db, ids);
        await SeedGhadaMedicalAsync(db, ids);
        await SeedGhadaAuditAsync(db, ids);

        Console.WriteLine("done.");

        // Aggregate rating is denormalised onto the (non-tenant) clinic row → privileged update.
        await UpdateAggregateRatingAsync(GhadaId);
    }

    /// <summary>60 operating days of closed sessions: 20–35/day, Saturdays busier, ~8% absent,
    /// a slice of confirmed deposits (revenue), and a handful of visits+prescriptions+ratings.</summary>
    private async Task SeedGhadaHistoryAsync(AppDbContext db, SeededIds ids)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var backdate = new List<(Guid Id, DateTime Ts)>();
        var poolCursor = 0;

        for (var dayOffset = 60; dayOffset >= 1; dayOffset--)
        {
            var date = today.AddDays(-dayOffset);
            var isSaturday = date.DayOfWeek == DayOfWeek.Saturday;
            var volume = isSaturday ? _rng.Next(30, 36) : _rng.Next(20, 29);

            var session = new QueueSession
            {
                ClinicId = GhadaId,
                OperatingDate = date,
                Status = QueueSessionStatus.Closed,
                NormalSinceLastUrgent = 0,
            };
            db.QueueSessions.Add(session);

            var realNo = 0;
            for (var n = 1; n <= volume; n++)
            {
                var membershipId = ids.GhadaPoolMemberships[poolCursor % ids.GhadaPoolMemberships.Count];
                var userId = ids.GhadaPool[poolCursor % ids.GhadaPool.Count];
                poolCursor++;

                var absent = _rng.NextDouble() < 0.08;
                var deposit = !absent && _rng.NextDouble() < 0.22;
                var hour = WeightedClinicHour();
                var createdAt = date.ToDateTime(new TimeOnly(hour, _rng.Next(0, 60))).ToUniversalTime();

                var appt = new Appointment
                {
                    ClinicId = GhadaId,
                    QueueSession = session,
                    PatientMembershipId = membershipId,
                    BookedByUserId = userId,
                    Type = AppointmentType.Normal,
                    Method = deposit ? AppointmentMethod.PhoneDeposit : AppointmentMethod.WalkIn,
                    TemporaryNumber = n,
                    RealNumber = absent ? null : ++realNo,
                    Status = absent ? AppointmentStatus.Absent : AppointmentStatus.Completed,
                    DepositProofUrl = deposit ? "https://cdn.clinicqueue.app/deposits/seed.png" : null,
                    DepositConfirmedAt = deposit ? createdAt : null,
                    CalledAt = absent ? null : createdAt.AddMinutes(_rng.Next(5, 90)),
                };
                db.Appointments.Add(appt);
                backdate.Add((appt.Id, createdAt));
            }
        }

        await db.SaveChangesAsync();
        // CreatedAt is stamped to "now" by AppDbContext.SaveChanges; reports group by CreatedAt, so
        // rewrite it to the historical instant. Runs on the same app-role connection (RLS-scoped).
        await BackdateCreatedAtAsync(db, backdate);
    }

    /// <summary>Today's ACTIVE session, arranged so every patient-facing and console state is reachable.</summary>
    private async Task SeedGhadaTodayAsync(AppDbContext db, SeededIds ids)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var session = new QueueSession
        {
            ClinicId = GhadaId,
            OperatingDate = today,
            Status = QueueSessionStatus.Open,
            NormalSinceLastUrgent = 1,
        };
        db.QueueSessions.Add(session);

        var temp = 0;
        var pool = new Queue<(Guid Membership, Guid User)>(
            ids.GhadaPoolMemberships.Zip(ids.GhadaPool, (m, u) => (m, u)));

        Appointment Named(Guid membership, Guid bookedBy, AppointmentStatus status, int? real,
            AppointmentType type = AppointmentType.Normal, AppointmentMethod method = AppointmentMethod.WalkIn)
            => AddTodayAppointment(db, session, membership, bookedBy, status, real, ref temp, type, method);

        Appointment Filler(AppointmentStatus status, int? real)
        {
            var (m, u) = pool.Dequeue();
            return AddTodayAppointment(db, session, m, u, status, real, ref temp);
        }

        // 3 already Done (real 1–3).
        Filler(AppointmentStatus.Completed, 1);
        Filler(AppointmentStatus.Completed, 2);
        Filler(AppointmentStatus.Completed, 3);

        // Live waiting order by real number (see CountAheadService): Present/Inside with a lower
        // real number are "ahead". patient3 Inside(4); patient2(6) → 2 ahead → HEAD TO CLINIC;
        // patient1(11) → 7 ahead → WAIT AT HOME. urgent(7) Present so it shows in reception.
        Named(ids.Patient3Membership, ids.Patient3, AppointmentStatus.Inside, 4).CalledAt = DateTime.UtcNow.AddMinutes(-6);
        Filler(AppointmentStatus.Present, 5);
        Named(ids.Patient2Membership, ids.Patient2, AppointmentStatus.Present, 6);
        Named(ids.UrgentMembership, ids.Urgent, AppointmentStatus.Present, 7, AppointmentType.Urgent);
        Filler(AppointmentStatus.Present, 8);
        Filler(AppointmentStatus.Present, 9);
        Filler(AppointmentStatus.Present, 10);
        Named(ids.Patient1Membership, ids.Patient1, AppointmentStatus.Present, 11);

        // patient4: booked-by-deposit, temp number only, NOT arrived (arrival-confirmation test).
        var p4 = Named(ids.Patient4Membership, ids.Reception, AppointmentStatus.Booked, null,
            method: AppointmentMethod.PhoneDeposit);
        p4.DepositProofUrl = "https://cdn.clinicqueue.app/deposits/patient4.png";
        p4.DepositConfirmedAt = DateTime.UtcNow.AddHours(-2);

        // Two absent today (absence flow).
        Filler(AppointmentStatus.Absent, null);
        Filler(AppointmentStatus.Absent, null);

        // Chronic absentee: booked today, plus TWO prior absences → repeated-absence warning (FR-047).
        var (chronicM, chronicU) = pool.Dequeue();
        Named(chronicM, chronicU, AppointmentStatus.Booked, null);
        await SeedPriorAbsencesAsync(db, chronicM, chronicU, count: 2, today);

        await db.SaveChangesAsync();
    }

    /// <summary>Two earlier stand-alone sessions in which this patient was marked Absent (history for FR-047).</summary>
    private async Task SeedPriorAbsencesAsync(AppDbContext db, Guid membershipId, Guid userId, int count, DateOnly today)
    {
        var backdate = new List<(Guid, DateTime)>();
        for (var i = 1; i <= count; i++)
        {
            var date = today.AddDays(-i * 3);
            var session = new QueueSession
            {
                ClinicId = GhadaId,
                OperatingDate = date.AddDays(-100), // distinct dates, well clear of the 60-day window
                Status = QueueSessionStatus.Closed,
            };
            db.QueueSessions.Add(session);
            var createdAt = session.OperatingDate.ToDateTime(new TimeOnly(11, 0)).ToUniversalTime();
            var appt = new Appointment
            {
                ClinicId = GhadaId,
                QueueSession = session,
                PatientMembershipId = membershipId,
                BookedByUserId = userId,
                TemporaryNumber = 1,
                Status = AppointmentStatus.Absent,
            };
            db.Appointments.Add(appt);
            backdate.Add((appt.Id, createdAt));
        }

        await db.SaveChangesAsync();
        await BackdateCreatedAtAsync(db, backdate);
    }

    /// <summary>Patient profiles + a completed visit with diagnosis, notes, prescription and rating
    /// for patient1/2/3 so the medical screen and doctor console have content.</summary>
    private async Task SeedGhadaMedicalAsync(AppDbContext db, SeededIds ids)
    {
        db.PatientProfiles.Add(NewProfile(ids.Patient1Membership, "O+", "Gestational diabetes (2023)", "Penicillin", "Folic acid"));
        db.PatientProfiles.Add(NewProfile(ids.Patient2Membership, "A-", null, "None known", null));
        db.PatientProfiles.Add(NewProfile(ids.Patient3Membership, "B+", "Hypothyroidism", "None known", "Levothyroxine 50mcg"));
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var backdate = new List<(Guid, DateTime)>();

        (Guid Membership, Guid User, string Dx, string Notes, string Drug, string Dose, int Score)[] cases =
        [
            (ids.Patient1Membership, ids.Patient1, "Routine antenatal check — 24 weeks", "Fundal height appropriate; FHR 148 bpm.", "Ferrous sulfate", "200 mg", 5),
            (ids.Patient2Membership, ids.Patient2, "Follow-up: PCOS management", "Cycle regularised; continue lifestyle plan.", "Metformin", "500 mg", 4),
            (ids.Patient3Membership, ids.Patient3, "Postpartum review — 6 weeks", "Healing well; contraception counselling given.", "Paracetamol", "500 mg", 5),
        ];

        var day = 5;
        foreach (var c in cases)
        {
            var date = today.AddDays(-day);
            var createdAt = date.ToDateTime(new TimeOnly(12, 30)).ToUniversalTime();
            var session = new QueueSession
            {
                ClinicId = GhadaId,
                OperatingDate = date.AddDays(-200), // dedicated past date, clear of other sessions
                Status = QueueSessionStatus.Closed,
            };
            db.QueueSessions.Add(session);

            var appt = new Appointment
            {
                ClinicId = GhadaId,
                QueueSession = session,
                PatientMembershipId = c.Membership,
                BookedByUserId = c.User,
                TemporaryNumber = 1,
                RealNumber = 1,
                Status = AppointmentStatus.Completed,
                CalledAt = createdAt,
            };
            db.Appointments.Add(appt);
            backdate.Add((appt.Id, createdAt));

            var visit = new Visit
            {
                ClinicId = GhadaId,
                Appointment = appt,
                TreatingDoctorUserId = ids.Doctor,
                Diagnosis = c.Dx,
                Notes = c.Notes,
            };
            db.Visits.Add(visit);
            db.Prescriptions.Add(new Prescription
            {
                ClinicId = GhadaId,
                Visit = visit,
                Drug = c.Drug,
                Dose = c.Dose,
                Frequency = "Once daily",
                Duration = "30 days",
            });
            db.Ratings.Add(new Rating
            {
                ClinicId = GhadaId,
                Appointment = appt,
                Score = c.Score,
                Comment = "Attentive and professional care.",
            });
            day++;
        }

        await db.SaveChangesAsync();
        await BackdateCreatedAtAsync(db, backdate);
    }

    private async Task SeedGhadaAuditAsync(AppDbContext db, SeededIds ids)
    {
        AddAudit(db, GhadaId, ids.GhadaOwner, "clinic.settings.updated", nameof(Clinic), GhadaId);
        AddAudit(db, GhadaId, ids.GhadaOwner, "clinic.branding.updated", nameof(Clinic), GhadaId);
        AddAudit(db, GhadaId, ids.Patient1, "PatientJoinedClinic", nameof(ClinicMembership), ids.Patient1Membership);
        AddAudit(db, GhadaId, ids.Reception, "queue.instant_entry", nameof(Appointment), Guid.NewGuid());
        await db.SaveChangesAsync();
    }

    // ── 4. Smile tenant data (app role, app.current_clinic = Smile) — isolation + switcher target ─
    private async Task SeedSmileTenantDataAsync(SeededIds ids)
    {
        Console.Write("• Seeding Smile Dental tenant data: small live queue (app role)… ");
        await using var db = await OpenAppContextAsync(SmileId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var session = new QueueSession
        {
            ClinicId = SmileId,
            OperatingDate = today,
            Status = QueueSessionStatus.Open,
        };
        db.QueueSessions.Add(session);

        var temp = 0;
        // multi@ (Patient at Smile) is waiting — proves the switcher lands on a real live queue.
        AddTodayAppointment(db, session, ids.MultiSmileMembership, ids.Multi, AppointmentStatus.Present, 2, ref temp);
        AddTodayAppointment(db, session, ids.SmilePoolMemberships[0], ids.SmilePool[0], AppointmentStatus.Inside, 1, ref temp);
        AddTodayAppointment(db, session, ids.SmilePoolMemberships[1], ids.SmilePool[1], AppointmentStatus.Present, 3, ref temp);

        await db.SaveChangesAsync();
        Console.WriteLine("done.");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────
    private const int PoolSize = 40;

    private PrivilegedDbContext PrivilegedContext()
    {
        var options = new DbContextOptionsBuilder<PrivilegedDbContext>()
            .UseNpgsql(_privilegedConn)
            .Options;
        return new PrivilegedDbContext(options);
    }

    /// <summary>App-role context with <c>app.current_clinic</c> pinned. The connection is opened and
    /// held so the session GUC survives across every SaveChanges on this context (RLS stays satisfied).</summary>
    private async Task<AppDbContext> OpenAppContextAsync(Guid clinicId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_appConn)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_clinic', {0}, false)", clinicId.ToString());
        return db;
    }

    private Subscription NewSubscription(Guid clinicId, string plan) => new()
    {
        ClinicId = clinicId,
        PlanName = plan,
        Status = SubscriptionStatus.Active,
        RenewalDate = DateTime.UtcNow.AddDays(30),
    };

    private Guid AddUser(PrivilegedDbContext db, string email, bool isSuperAdmin = false)
    {
        var user = new User
        {
            EmailOrPhone = email.ToLowerInvariant(),
            PasswordHash = _passwordHash,
            IsSuperAdmin = isSuperAdmin,
        };
        db.Users.Add(user);
        return user.Id;
    }

    private static Guid AddMembership(PrivilegedDbContext db, Guid clinicId, Guid userId, ClinicRole role)
    {
        var membership = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = userId,
            Role = role,
            Status = MembershipStatus.Active,
        };
        db.ClinicMemberships.Add(membership);
        return membership.Id;
    }

    private static Appointment AddTodayAppointment(
        AppDbContext db, QueueSession session, Guid membershipId, Guid bookedBy,
        AppointmentStatus status, int? real, ref int temp,
        AppointmentType type = AppointmentType.Normal, AppointmentMethod method = AppointmentMethod.WalkIn)
    {
        var appt = new Appointment
        {
            ClinicId = session.ClinicId,
            QueueSession = session,
            PatientMembershipId = membershipId,
            BookedByUserId = bookedBy,
            Type = type,
            Method = method,
            TemporaryNumber = ++temp,
            RealNumber = real,
            Status = status,
        };
        db.Appointments.Add(appt);
        return appt;
    }

    private static PatientProfile NewProfile(Guid membershipId, string? blood, string? chronic, string? allergies, string? meds) => new()
    {
        ClinicId = DemoSeederClinicOf(membershipId),
        ClinicMembershipId = membershipId,
        BloodType = blood,
        ChronicConditions = chronic,
        Allergies = allergies,
        CurrentMedications = meds,
    };

    // Profiles are only ever created for Ghada memberships in this seed.
    private static Guid DemoSeederClinicOf(Guid _) => GhadaId;

    private static void AddAudit(AppDbContext db, Guid clinicId, Guid actor, string action, string entityType, Guid entityId)
        => db.AuditLogs.Add(new AuditLog
        {
            ClinicId = clinicId,
            ActorUserId = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Timestamp = DateTime.UtcNow,
        });

    private int WeightedClinicHour()
    {
        // Morning (10–13) and evening (17–20) clinics; a light midday tail.
        var buckets = new[] { 10, 10, 11, 11, 12, 13, 17, 17, 18, 18, 19, 20 };
        return buckets[_rng.Next(buckets.Length)];
    }

    private static async Task BackdateCreatedAtAsync(AppDbContext db, IReadOnlyList<(Guid Id, DateTime Ts)> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var ids = rows.Select(r => r.Id).ToArray();
        var timestamps = rows.Select(r => DateTime.SpecifyKind(r.Ts, DateTimeKind.Utc)).ToArray();

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE appointments AS a
            SET "CreatedAt" = v.ts
            FROM (SELECT unnest(@ids) AS id, unnest(@tss) AS ts) AS v
            WHERE a."Id" = v.id;
            """,
            new NpgsqlParameter("ids", ids),
            new NpgsqlParameter("tss", timestamps));
    }

    private async Task UpdateAggregateRatingAsync(Guid clinicId)
    {
        await using var app = await OpenAppContextAsync(clinicId);
        var scores = await app.Ratings.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Select(r => (double)r.Score)
            .ToListAsync();
        if (scores.Count == 0)
        {
            return;
        }

        var average = Math.Round(scores.Average(), 2);
        await using var priv = PrivilegedContext();
        var clinic = await priv.Clinics.FirstAsync(c => c.Id == clinicId);
        clinic.AggregateRating = average;
        await priv.SaveChangesAsync();
    }

    private static SeedResult BuildResult(SeededIds ids)
    {
        var accounts = new List<AccountRow>
        {
            new("admin@clinicqueue.app", "SuperAdmin", "— (platform)", "Platform-wide, no clinic"),
            new("ghada@clinic.test", "ClinicOwner", "Dr. Ghada", "Owner: branding, settings, reports"),
            new("doctor@clinic.test", "Doctor", "Dr. Ghada", "Doctor console + medical records"),
            new("reception@clinic.test", "Receptionist", "Dr. Ghada", "Reception console + day close"),
            new("patient1@clinic.test", "Patient", "Dr. Ghada", "Waiting, 7 ahead → WAIT AT HOME"),
            new("patient2@clinic.test", "Patient", "Dr. Ghada", "Waiting, 2 ahead → HEAD TO CLINIC"),
            new("patient3@clinic.test", "Patient", "Dr. Ghada", "Currently INSIDE with the doctor"),
            new("patient4@clinic.test", "Patient", "Dr. Ghada", "Booked (temp no.), not arrived → confirm arrival"),
            new("urgent@clinic.test", "Patient", "Dr. Ghada", "URGENT type, confirmed present"),
            new("multi@clinic.test", "Reception+Patient", "Ghada + Smile", "THE multi-clinic switcher account"),
            new("newbie@clinic.test", "Patient", "— (none)", "No membership → /find-clinic onboarding"),
        };

        return new SeedResult(
            Password,
            new ClinicSummary("Dr. Ghada Kamel Clinic", "ghada", $"{BaseUrl}/r/ghada"),
            new ClinicSummary("Smile Dental Clinic", "smile", $"{BaseUrl}/r/smile"),
            accounts);
    }
}
