using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Medical;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Medical;

public sealed record GetPatientProfileQuery(
    Guid ClinicId,
    Guid TargetMembershipId,
    Guid ActorUserId);

public sealed record UpdatePatientProfileCommand(
    Guid ClinicId,
    Guid TargetMembershipId,
    Guid ActorUserId,
    string? BloodType,
    string? ChronicConditions,
    string? Allergies,
    string? CurrentMedications);

public interface IGetPatientProfileHandler
{
    Task<PatientProfileResponse> HandleAsync(
        GetPatientProfileQuery query,
        CancellationToken cancellationToken = default);
}

public interface IUpdatePatientProfileHandler
{
    Task<PatientProfileResponse> HandleAsync(
        UpdatePatientProfileCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class GetPatientProfileHandler : IGetPatientProfileHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public GetPatientProfileHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PatientProfileResponse> HandleAsync(
        GetPatientProfileQuery query,
        CancellationToken cancellationToken = default)
    {
        var membership = await _db.ClinicMemberships.AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.Id == query.TargetMembershipId && m.ClinicId == query.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Patient membership not found.");

        var isSelf = membership.UserId == query.ActorUserId;
        var isClinicalStaff = !isSelf && await IsActiveClinicalStaffAsync(
            query.ClinicId, query.ActorUserId, cancellationToken);
        MedicalAccessPolicy.EnsureCanReadProfile(isSelf, isClinicalStaff);

        var profile = await _db.PatientProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ClinicMembershipId == membership.Id, cancellationToken);

        // FR-087: every medical-file read is audit-logged, including a patient's own profile.
        _audit.Append(
            query.ClinicId,
            query.ActorUserId,
            "ProfileRead",
            nameof(PatientProfile),
            profile?.Id,
            null);
        await _db.SaveChangesAsync(cancellationToken);

        return ToResponse(membership.Id, profile);
    }

    private async Task<bool> IsActiveClinicalStaffAsync(
        Guid clinicId,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        await _db.ClinicMemberships.AsNoTracking().AnyAsync(
            m => m.ClinicId == clinicId
                 && m.UserId == actorUserId
                 && m.Status == MembershipStatus.Active
                 && (m.Role == ClinicRole.Doctor || m.Role == ClinicRole.ClinicOwner),
            cancellationToken);

    internal static PatientProfileResponse ToResponse(Guid membershipId, PatientProfile? profile) =>
        new(
            membershipId,
            profile?.BloodType,
            profile?.ChronicConditions,
            profile?.Allergies,
            profile?.CurrentMedications);
}

/// <summary>Creates the profile on first write; patient (self) only (FR-080).</summary>
public sealed class UpdatePatientProfileHandler : IUpdatePatientProfileHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public UpdatePatientProfileHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PatientProfileResponse> HandleAsync(
        UpdatePatientProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var membership = await _db.ClinicMemberships.AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.Id == command.TargetMembershipId && m.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Patient membership not found.");

        MedicalAccessPolicy.EnsureCanUpdateProfile(membership.UserId == command.ActorUserId);

        var profile = await LoadOrCreateProfileAsync(command.ClinicId, membership.Id, cancellationToken);
        ApplyFields(profile, command);

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "ProfileUpdate",
            nameof(PatientProfile),
            profile.Id,
            null);

        await _db.SaveChangesAsync(cancellationToken);

        return GetPatientProfileHandler.ToResponse(membership.Id, profile);
    }

    private async Task<PatientProfile> LoadOrCreateProfileAsync(
        Guid clinicId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var profile = await _db.PatientProfiles
            .FirstOrDefaultAsync(p => p.ClinicMembershipId == membershipId, cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        profile = new PatientProfile { ClinicId = clinicId, ClinicMembershipId = membershipId };
        _db.PatientProfiles.Add(profile);
        return profile;
    }

    private static void ApplyFields(PatientProfile profile, UpdatePatientProfileCommand command)
    {
        profile.BloodType = command.BloodType?.Trim();
        profile.ChronicConditions = command.ChronicConditions?.Trim();
        profile.Allergies = command.Allergies?.Trim();
        profile.CurrentMedications = command.CurrentMedications?.Trim();
    }
}
