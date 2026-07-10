using FluentValidation;

namespace ClinicQueue.Application.Queue.Emergency;

public sealed record PauseQueueSessionRequest(string Reason);

public sealed record PauseQueueSessionResponse(
    Guid QueueSessionId,
    string Status,
    DateTime PausedAt);

public sealed record ResumeQueueSessionResponse(
    Guid QueueSessionId,
    string Status);

public sealed class PauseQueueSessionRequestValidator : AbstractValidator<PauseQueueSessionRequest>
{
    public PauseQueueSessionRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
