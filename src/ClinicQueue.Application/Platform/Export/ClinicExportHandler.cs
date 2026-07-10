using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Platform.Export;

public sealed record StartClinicExportCommand(Guid ClinicId, Guid RequestedByUserId);

public sealed record ClinicExportJobResult(Guid ExportJobId, string Status);

public sealed record ClinicExportPayloadDto(
    Guid ClinicId,
    DateTime ExportedAtUtc,
    IReadOnlyList<ExportedPatientDto> Patients,
    IReadOnlyList<ExportedAppointmentDto> Appointments,
    IReadOnlyList<ExportedVisitDto> Visits);

public sealed record ExportedPatientDto(
    Guid MembershipId,
    Guid? ProfileId,
    Guid UserId,
    string? BloodType,
    string? ChronicConditions,
    string? Allergies,
    string? CurrentMedications);

public sealed record ExportedAppointmentDto(
    Guid AppointmentId,
    Guid QueueSessionId,
    Guid? PatientMembershipId,
    Guid? DependentId,
    Guid BookedByUserId,
    string Type,
    string Method,
    string Status,
    int TemporaryNumber,
    int? RealNumber,
    int CompanionsCount,
    DateTime? DepositConfirmedAt,
    DateTime CreatedAt);

public sealed record ExportedVisitDto(
    Guid VisitId,
    Guid AppointmentId,
    Guid TreatingDoctorUserId,
    string Diagnosis,
    string Notes,
    DateTime CreatedAt);

public interface IClinicExportHandler
{
    Task<ClinicExportJobResult> StartAsync(
        StartClinicExportCommand command,
        CancellationToken cancellationToken = default);

    Task<ClinicExportPayloadDto?> GetPayloadAsync(
        Guid clinicId,
        Guid exportJobId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Clinic-scoped self-service export of patients, appointments, and visits (FR-105).
/// Creates a job record and materializes the payload immediately (Hangfire-ready shape).
/// </summary>
public sealed class ClinicExportHandler : IClinicExportHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IAppDbContext _db;

    public ClinicExportHandler(IAppDbContext db) => _db = db;

    public async Task<ClinicExportJobResult> StartAsync(
        StartClinicExportCommand command,
        CancellationToken cancellationToken = default)
    {
        var clinicExists = await _db.Clinics.AnyAsync(c => c.Id == command.ClinicId, cancellationToken);
        if (!clinicExists)
        {
            throw new InvalidOperationException("Clinic not found.");
        }

        var job = new ClinicExportJob
        {
            ClinicId = command.ClinicId,
            RequestedByUserId = command.RequestedByUserId,
            Status = ClinicExportJobStatus.Pending,
        };
        _db.ClinicExportJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var payload = await BuildPayloadAsync(command.ClinicId, cancellationToken);
            job.PayloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            job.Status = ClinicExportJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            job.Status = ClinicExportJobStatus.Failed;
            job.Error = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }

        return new ClinicExportJobResult(job.Id, job.Status.ToString());
    }

    public async Task<ClinicExportPayloadDto?> GetPayloadAsync(
        Guid clinicId,
        Guid exportJobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _db.ClinicExportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == exportJobId && j.ClinicId == clinicId, cancellationToken);

        if (job is null || job.Status != ClinicExportJobStatus.Completed || string.IsNullOrWhiteSpace(job.PayloadJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ClinicExportPayloadDto>(job.PayloadJson, JsonOptions);
    }

    private async Task<ClinicExportPayloadDto> BuildPayloadAsync(
        Guid clinicId,
        CancellationToken cancellationToken)
    {
        // Strict clinic scope — never include other tenants' rows (FR-105 / Principle I).
        var memberships = await _db.ClinicMemberships.AsNoTracking()
            .Where(m => m.ClinicId == clinicId && m.Role == ClinicRole.Patient)
            .Select(m => new { m.Id, m.UserId })
            .ToListAsync(cancellationToken);

        var profiles = await _db.PatientProfiles.AsNoTracking()
            .Where(p => p.ClinicId == clinicId)
            .ToDictionaryAsync(p => p.ClinicMembershipId, cancellationToken);

        var patients = memberships.Select(m =>
        {
            profiles.TryGetValue(m.Id, out var profile);
            return new ExportedPatientDto(
                m.Id,
                profile?.Id,
                m.UserId,
                profile?.BloodType,
                profile?.ChronicConditions,
                profile?.Allergies,
                profile?.CurrentMedications);
        }).ToList();

        var appointments = await _db.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == clinicId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ExportedAppointmentDto(
                a.Id,
                a.QueueSessionId,
                a.PatientMembershipId,
                a.DependentId,
                a.BookedByUserId,
                a.Type.ToString(),
                a.Method.ToString(),
                a.Status.ToString(),
                a.TemporaryNumber,
                a.RealNumber,
                a.CompanionsCount,
                a.DepositConfirmedAt,
                a.CreatedAt))
            .ToListAsync(cancellationToken);

        var visits = await _db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId)
            .OrderBy(v => v.CreatedAt)
            .Select(v => new ExportedVisitDto(
                v.Id,
                v.AppointmentId,
                v.TreatingDoctorUserId,
                v.Diagnosis,
                v.Notes,
                v.CreatedAt))
            .ToListAsync(cancellationToken);

        return new ClinicExportPayloadDto(
            clinicId,
            DateTime.UtcNow,
            patients,
            appointments,
            visits);
    }
}
