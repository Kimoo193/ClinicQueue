using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Queue;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Booking;

public sealed record RegisterDependentCommand(
    Guid ClinicId,
    Guid GuardianUserId,
    string Name,
    DateOnly DateOfBirth,
    string RelationshipToGuardian);

public sealed record RegisterDependentResult(
    Guid DependentId,
    string Name,
    DateOnly DateOfBirth,
    string RelationshipToGuardian);

public sealed record DependentBookingCommand(
    Guid ClinicId,
    Guid GuardianUserId,
    Guid DependentId,
    DateOnly Day,
    AppointmentMethod Method,
    bool PossibleUrgentFlag);

public sealed record DependentDto(
    Guid DependentId,
    Guid ClinicId,
    string Name,
    DateOnly DateOfBirth,
    string RelationshipToGuardian);

public sealed record RegisterDependentRequest(
    Guid ClinicId,
    string Name,
    DateOnly DateOfBirth,
    string RelationshipToGuardian);

public interface IRegisterDependentHandler
{
    Task<RegisterDependentResult> HandleAsync(
        RegisterDependentCommand command,
        CancellationToken cancellationToken = default);
}

public interface IDependentBookingHandler
{
    Task<CreateAppointmentResult> HandleAsync(
        DependentBookingCommand command,
        CancellationToken cancellationToken = default);
}

public interface IListDependentsHandler
{
    Task<IReadOnlyList<DependentDto>> HandleAsync(
        Guid guardianUserId,
        Guid? clinicId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Registers a Dependent under a guardian for a clinic (FR-046 prerequisite).</summary>
public sealed class RegisterDependentHandler : IRegisterDependentHandler
{
    private readonly IAppDbContext _db;

    public RegisterDependentHandler(IAppDbContext db) => _db = db;

    public async Task<RegisterDependentResult> HandleAsync(
        RegisterDependentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ArgumentException("Dependent name is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.RelationshipToGuardian))
        {
            throw new ArgumentException("Relationship to guardian is required.", nameof(command));
        }

        _ = await _db.Clinics.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.ClinicId, cancellationToken)
            ?? throw new InvalidOperationException("Clinic not found.");

        // Guardian must have an active patient membership at the clinic.
        _ = await _db.ClinicMemberships
            .FirstOrDefaultAsync(
                m => m.ClinicId == command.ClinicId
                     && m.UserId == command.GuardianUserId
                     && m.Role == ClinicRole.Patient
                     && m.Status == MembershipStatus.Active,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Active patient membership required to register a dependent.");

        var dependent = new Dependent
        {
            ClinicId = command.ClinicId,
            GuardianUserId = command.GuardianUserId,
            Name = command.Name.Trim(),
            DateOfBirth = command.DateOfBirth,
            RelationshipToGuardian = command.RelationshipToGuardian.Trim()
        };

        _db.Dependents.Add(dependent);
        await _db.SaveChangesAsync(cancellationToken);

        return new RegisterDependentResult(
            dependent.Id,
            dependent.Name,
            dependent.DateOfBirth,
            dependent.RelationshipToGuardian);
    }
}

/// <summary>
/// Books an appointment on behalf of a registered Dependent (FR-046).
/// The Dependent is the patient-of-record; BookedByUserId remains the guardian.
/// </summary>
public sealed class DependentBookingHandler : IDependentBookingHandler
{
    private readonly ICreateAppointmentHandler _create;
    private readonly IAppDbContext _db;

    public DependentBookingHandler(ICreateAppointmentHandler create, IAppDbContext db)
    {
        _create = create;
        _db = db;
    }

    public async Task<CreateAppointmentResult> HandleAsync(
        DependentBookingCommand command,
        CancellationToken cancellationToken = default)
    {
        _ = await _db.Dependents
            .FirstOrDefaultAsync(
                d => d.Id == command.DependentId
                     && d.ClinicId == command.ClinicId
                     && d.GuardianUserId == command.GuardianUserId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Dependent not found for this guardian/clinic. Register the dependent first.");

        return await _create.HandleAsync(
            new CreateAppointmentCommand(
                command.ClinicId,
                command.GuardianUserId,
                command.Day,
                command.Method,
                command.PossibleUrgentFlag,
                command.DependentId),
            cancellationToken);
    }
}

public sealed class ListDependentsHandler : IListDependentsHandler
{
    private readonly IAppDbContext _db;

    public ListDependentsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<DependentDto>> HandleAsync(
        Guid guardianUserId,
        Guid? clinicId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Dependents.AsNoTracking()
            .Where(d => d.GuardianUserId == guardianUserId);

        if (clinicId is { } cid)
        {
            query = query.Where(d => d.ClinicId == cid);
        }

        return await query
            .OrderBy(d => d.Name)
            .Select(d => new DependentDto(
                d.Id,
                d.ClinicId,
                d.Name,
                d.DateOfBirth,
                d.RelationshipToGuardian))
            .ToListAsync(cancellationToken);
    }
}

public sealed class RegisterDependentRequestValidator : AbstractValidator<RegisterDependentRequest>
{
    public RegisterDependentRequestValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DateOfBirth).NotEmpty()
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.");
        RuleFor(x => x.RelationshipToGuardian).NotEmpty().MaximumLength(100);
    }
}
