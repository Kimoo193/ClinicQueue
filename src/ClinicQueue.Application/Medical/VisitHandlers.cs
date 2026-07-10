using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Medical;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Medical;

public sealed record CreateVisitCommand(
    Guid ClinicId,
    Guid AppointmentId,
    Guid ActorUserId,
    ClinicRole ActorRole,
    string Diagnosis,
    string Notes);

public sealed record UpdateVisitCommand(
    Guid ClinicId,
    Guid VisitId,
    Guid ActorUserId,
    ClinicRole ActorRole,
    string Diagnosis,
    string Notes);

public sealed record GetVisitByAppointmentCommand(
    Guid ClinicId,
    Guid AppointmentId,
    Guid ActorUserId,
    ClinicRole ActorRole);

public sealed record IssuePrescriptionCommand(
    Guid ClinicId,
    Guid VisitId,
    Guid ActorUserId,
    ClinicRole ActorRole,
    string Drug,
    string Dose,
    string Frequency,
    string Duration);

public interface ICreateVisitHandler
{
    Task<VisitResponse> HandleAsync(CreateVisitCommand command, CancellationToken cancellationToken = default);
}

public interface IUpdateVisitHandler
{
    Task<VisitResponse> HandleAsync(UpdateVisitCommand command, CancellationToken cancellationToken = default);
}

public interface IGetVisitHandler
{
    Task<VisitResponse> HandleAsync(GetVisitByAppointmentCommand command, CancellationToken cancellationToken = default);
}

public interface IIssuePrescriptionHandler
{
    Task<PrescriptionResponse> HandleAsync(IssuePrescriptionCommand command, CancellationToken cancellationToken = default);
}

public sealed class CreateVisitHandler : ICreateVisitHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public CreateVisitHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<VisitResponse> HandleAsync(
        CreateVisitCommand command,
        CancellationToken cancellationToken = default)
    {
        MedicalAccessPolicy.EnsureCanCreate(command.ActorRole);

        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(
                a => a.Id == command.AppointmentId && a.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Appointment not found.");

        if (appointment.Status is not (AppointmentStatus.Inside or AppointmentStatus.Completed or AppointmentStatus.Present))
        {
            throw new InvalidOperationException(
                "Visit can only be recorded for a present, inside, or completed appointment.");
        }

        var existing = await _db.Visits.AnyAsync(
            v => v.AppointmentId == appointment.Id,
            cancellationToken);
        if (existing)
        {
            throw new InvalidOperationException("A visit already exists for this appointment.");
        }

        var visit = new Visit
        {
            ClinicId = command.ClinicId,
            AppointmentId = appointment.Id,
            TreatingDoctorUserId = command.ActorUserId,
            Diagnosis = command.Diagnosis.Trim(),
            Notes = command.Notes?.Trim() ?? string.Empty
        };
        _db.Visits.Add(visit);

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "VisitCreate",
            nameof(Visit),
            visit.Id,
            JsonSerializer.Serialize(new { appointmentId = appointment.Id }, QueueNotificationRules.CamelCaseJson));

        await _db.SaveChangesAsync(cancellationToken);
        return VisitMapping.Map(visit, null);
    }
}

public sealed class UpdateVisitHandler : IUpdateVisitHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public UpdateVisitHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<VisitResponse> HandleAsync(
        UpdateVisitCommand command,
        CancellationToken cancellationToken = default)
    {
        var visit = await _db.Visits
            .Include(v => v.Prescription)
            .FirstOrDefaultAsync(
                v => v.Id == command.VisitId && v.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Visit not found.");

        MedicalAccessPolicy.EnsureCanWrite(
            command.ActorRole,
            command.ActorUserId,
            visit.TreatingDoctorUserId);

        visit.Diagnosis = command.Diagnosis.Trim();
        visit.Notes = command.Notes?.Trim() ?? string.Empty;

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "VisitUpdate",
            nameof(Visit),
            visit.Id,
            null);

        await _db.SaveChangesAsync(cancellationToken);
        return VisitMapping.Map(visit, visit.Prescription);
    }
}

public sealed class GetVisitHandler : IGetVisitHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public GetVisitHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<VisitResponse> HandleAsync(
        GetVisitByAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        MedicalAccessPolicy.EnsureCanRead(command.ActorRole);

        var visit = await _db.Visits.AsNoTracking()
            .Include(v => v.Prescription)
            .FirstOrDefaultAsync(
                v => v.AppointmentId == command.AppointmentId && v.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Visit not found.");

        // FR-087: every medical-file read is audit-logged.
        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "VisitRead",
            nameof(Visit),
            visit.Id,
            JsonSerializer.Serialize(new
            {
                appointmentId = command.AppointmentId,
                role = command.ActorRole.ToString()
            }, QueueNotificationRules.CamelCaseJson));
        await _db.SaveChangesAsync(cancellationToken);

        return VisitMapping.Map(visit, visit.Prescription);
    }
}

public sealed class IssuePrescriptionHandler : IIssuePrescriptionHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public IssuePrescriptionHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PrescriptionResponse> HandleAsync(
        IssuePrescriptionCommand command,
        CancellationToken cancellationToken = default)
    {
        var visit = await _db.Visits
            .Include(v => v.Prescription)
            .FirstOrDefaultAsync(
                v => v.Id == command.VisitId && v.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Visit not found.");

        MedicalAccessPolicy.EnsureCanWrite(
            command.ActorRole,
            command.ActorUserId,
            visit.TreatingDoctorUserId);

        if (visit.Prescription is not null)
        {
            throw new InvalidOperationException("A prescription already exists for this visit.");
        }

        var prescription = new Prescription
        {
            ClinicId = command.ClinicId,
            VisitId = visit.Id,
            Drug = command.Drug.Trim(),
            Dose = command.Dose.Trim(),
            Frequency = command.Frequency.Trim(),
            Duration = command.Duration.Trim()
        };
        _db.Prescriptions.Add(prescription);

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "PrescriptionIssue",
            nameof(Prescription),
            prescription.Id,
            JsonSerializer.Serialize(new { visitId = visit.Id }, QueueNotificationRules.CamelCaseJson));

        await _db.SaveChangesAsync(cancellationToken);

        return new PrescriptionResponse(
            prescription.Id,
            prescription.Drug,
            prescription.Dose,
            prescription.Frequency,
            prescription.Duration);
    }
}

internal static class VisitMapping
{
    public static VisitResponse Map(Visit visit, Prescription? prescription) =>
        new(
            visit.Id,
            visit.AppointmentId,
            visit.TreatingDoctorUserId,
            visit.Diagnosis,
            visit.Notes,
            prescription is null
                ? null
                : new PrescriptionResponse(
                    prescription.Id,
                    prescription.Drug,
                    prescription.Dose,
                    prescription.Frequency,
                    prescription.Duration));
}
