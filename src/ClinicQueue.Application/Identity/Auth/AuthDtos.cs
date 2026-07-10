using FluentValidation;

namespace ClinicQueue.Application.Identity.Auth;

public sealed record RegisterRequest(string EmailOrPhone, string Password);
public sealed record LoginRequest(string EmailOrPhone, string Password, Guid? ClinicId = null);
public sealed record AuthUserResponse(Guid UserId, IReadOnlyList<string> Roles);

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
