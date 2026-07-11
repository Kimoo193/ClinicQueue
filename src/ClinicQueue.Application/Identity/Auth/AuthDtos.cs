using FluentValidation;

namespace ClinicQueue.Application.Identity.Auth;

public sealed record RegisterRequest(string EmailOrPhone, string Password, Guid? ClinicId = null);
public sealed record LoginRequest(string EmailOrPhone, string Password, Guid? ClinicId = null);

/// <summary>One clinic the user is an active member of (for the client's clinic switcher).</summary>
public sealed record ClinicMembershipSummary(Guid ClinicId, string ClinicName, string Role);

/// <summary>
/// Auth result. <see cref="Roles"/> and <see cref="ClinicId"/> describe the token's single active
/// tenant context; <see cref="Memberships"/> lists every clinic the user can switch to.
/// </summary>
public sealed record AuthUserResponse(
    Guid UserId,
    IReadOnlyList<string> Roles,
    Guid? ClinicId = null,
    IReadOnlyList<ClinicMembershipSummary>? Memberships = null);

/// <summary>Re-issue a tenant-scoped token for another clinic the user is a member of.</summary>
public sealed record SwitchClinicRequest(Guid ClinicId);

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.EmailOrPhone).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.EmailOrPhone).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
