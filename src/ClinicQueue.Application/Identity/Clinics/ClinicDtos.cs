using FluentValidation;

namespace ClinicQueue.Application.Identity.Clinics;

public sealed record CreateClinicRequest(
    string Name,
    string Specialty,
    string? Contact,
    string AdminEmail,
    string AdminPassword);

public sealed record CreateClinicResponse(Guid ClinicId, string SubdomainSlug, string? QrCodeUrl);

/// <summary>Public branding profile for QR / subdomain entry (FR-003, FR-007). Not an auth authority.</summary>
public sealed record PublicClinicProfileResponse(
    Guid ClinicId,
    string Name,
    string Specialty,
    string SubdomainSlug,
    string? LogoUrl,
    string? PrimaryColor,
    string? SecondaryColor);

public sealed record UpdateBrandingRequest(string? LogoUrl, string? PrimaryColor, string? SecondaryColor);

public sealed record UpdateSettingsRequest(
    int? UrgentRatio,
    int? HeadToClinicThreshold,
    int? AutoAbsenceTimeoutMinutes,
    int? RepeatedAbsenceThreshold,
    string? WorkingHoursJson,
    string? Timezone);

public sealed class CreateClinicRequestValidator : AbstractValidator<CreateClinicRequest>
{
    public CreateClinicRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Specialty).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdminEmail).NotEmpty().MaximumLength(320);
        RuleFor(x => x.AdminPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public sealed class UpdateBrandingRequestValidator : AbstractValidator<UpdateBrandingRequest>
{
    public UpdateBrandingRequestValidator()
    {
        RuleFor(x => x.PrimaryColor).MaximumLength(32).When(x => x.PrimaryColor is not null);
        RuleFor(x => x.SecondaryColor).MaximumLength(32).When(x => x.SecondaryColor is not null);
        RuleFor(x => x.LogoUrl).MaximumLength(2048).When(x => x.LogoUrl is not null);
    }
}

public sealed class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
{
    public UpdateSettingsRequestValidator()
    {
        RuleFor(x => x.UrgentRatio).InclusiveBetween(1, 10).When(x => x.UrgentRatio.HasValue);
        RuleFor(x => x.HeadToClinicThreshold).InclusiveBetween(1, 50).When(x => x.HeadToClinicThreshold.HasValue);
        RuleFor(x => x.AutoAbsenceTimeoutMinutes).InclusiveBetween(1, 240).When(x => x.AutoAbsenceTimeoutMinutes.HasValue);
        RuleFor(x => x.RepeatedAbsenceThreshold).InclusiveBetween(1, 20).When(x => x.RepeatedAbsenceThreshold.HasValue);
        RuleFor(x => x.Timezone).MaximumLength(100).When(x => x.Timezone is not null);
    }
}
