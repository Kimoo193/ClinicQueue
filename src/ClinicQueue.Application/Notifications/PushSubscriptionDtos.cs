using FluentValidation;

namespace ClinicQueue.Application.Notifications;

public sealed record UpsertPushSubscriptionRequest(
    Guid ClinicId,
    string Endpoint,
    string P256dh,
    string Auth);

public sealed class UpsertPushSubscriptionRequestValidator : AbstractValidator<UpsertPushSubscriptionRequest>
{
    public UpsertPushSubscriptionRequestValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.P256dh).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Auth).NotEmpty().MaximumLength(512);
    }
}
