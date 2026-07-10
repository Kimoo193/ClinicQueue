using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Identity;

public sealed record InviteReceptionistCommand(
    Guid ClinicId,
    Guid ActorUserId,
    string Email,
    string Name);

public sealed record InviteReceptionistResult(Guid MembershipId, Guid UserId, string Name);

public sealed record ReceptionistMembershipDto(
    Guid MembershipId,
    Guid UserId,
    string EmailOrPhone,
    string Status,
    DateTime CreatedAt);

public sealed record RevokeReceptionistCommand(
    Guid ClinicId,
    Guid ActorUserId,
    Guid MembershipId);

public sealed record InviteReceptionistRequest(string Email, string Name);

public sealed class InviteReceptionistRequestValidator : AbstractValidator<InviteReceptionistRequest>
{
    public InviteReceptionistRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public interface IInviteReceptionistHandler
{
    Task<InviteReceptionistResult> HandleAsync(
        InviteReceptionistCommand command,
        CancellationToken cancellationToken = default);
}

public interface IListReceptionistsHandler
{
    Task<IReadOnlyList<ReceptionistMembershipDto>> HandleAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default);
}

public interface IRevokeReceptionistHandler
{
    Task HandleAsync(RevokeReceptionistCommand command, CancellationToken cancellationToken = default);
}

/// <summary>ClinicOwner invite / list / revoke receptionist memberships (FR-015).</summary>
public sealed class InviteReceptionistHandler : IInviteReceptionistHandler
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;

    public InviteReceptionistHandler(
        IAppDbContext db,
        IPasswordHasher passwordHasher,
        IAuditWriter audit)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _audit = audit;
    }

    public async Task<InviteReceptionistResult> HandleAsync(
        InviteReceptionistCommand command,
        CancellationToken cancellationToken = default)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(command.Name))
        {
            throw new InvalidOperationException("Email and name are required.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.EmailOrPhone == email, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                EmailOrPhone = email,
                PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString("N") + "Aa1!")
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var existing = await _db.ClinicMemberships.AsNoTracking()
            .AnyAsync(
                m => m.ClinicId == command.ClinicId
                     && m.UserId == user.Id
                     && m.Role == ClinicRole.Receptionist,
                cancellationToken);
        if (existing)
        {
            throw new InvalidOperationException("This user is already a receptionist for the clinic.");
        }

        var membership = new ClinicMembership
        {
            ClinicId = command.ClinicId,
            UserId = user.Id,
            Role = ClinicRole.Receptionist,
            InvitedByUserId = command.ActorUserId,
            Status = MembershipStatus.Active
        };
        _db.ClinicMemberships.Add(membership);

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "membership.receptionist.invited",
            nameof(ClinicMembership),
            membership.Id);

        await _db.SaveChangesAsync(cancellationToken);

        return new InviteReceptionistResult(membership.Id, user.Id, command.Name.Trim());
    }
}

public sealed class ListReceptionistsHandler : IListReceptionistsHandler
{
    private readonly IAppDbContext _db;

    public ListReceptionistsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReceptionistMembershipDto>> HandleAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _db.ClinicMemberships.AsNoTracking()
            .Where(m => m.ClinicId == clinicId && m.Role == ClinicRole.Receptionist)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new ReceptionistMembershipDto(
                m.Id,
                m.UserId,
                m.User!.EmailOrPhone,
                m.Status.ToString(),
                m.CreatedAt))
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}

public sealed class RevokeReceptionistHandler : IRevokeReceptionistHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public RevokeReceptionistHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task HandleAsync(
        RevokeReceptionistCommand command,
        CancellationToken cancellationToken = default)
    {
        var membership = await _db.ClinicMemberships
            .FirstOrDefaultAsync(
                m => m.Id == command.MembershipId
                     && m.ClinicId == command.ClinicId
                     && m.Role == ClinicRole.Receptionist,
                cancellationToken)
            ?? throw new InvalidOperationException("Receptionist membership not found.");

        if (membership.Status == MembershipStatus.Restricted)
        {
            return;
        }

        membership.Status = MembershipStatus.Restricted;

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "membership.receptionist.revoked",
            nameof(ClinicMembership),
            membership.Id);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
