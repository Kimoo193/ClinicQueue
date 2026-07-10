using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Medical;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Abstractions;

/// <summary>
/// Application-facing persistence port. Implemented by Infrastructure <c>AppDbContext</c>.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Clinic> Clinics { get; }
    DbSet<ClinicMembership> ClinicMemberships { get; }
    DbSet<Dependent> Dependents { get; }
    DbSet<QueueSession> QueueSessions { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<OutboxNotification> OutboxNotifications { get; }
    DbSet<PushSubscription> PushSubscriptions { get; }
    DbSet<PatientProfile> PatientProfiles { get; }
    DbSet<Visit> Visits { get; }
    DbSet<Prescription> Prescriptions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
