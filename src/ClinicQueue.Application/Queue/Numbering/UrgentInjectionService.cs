using ClinicQueue.Domain.Queue;

namespace ClinicQueue.Application.Queue.Numbering;

/// <summary>
/// Deterministic 2:1 (configurable) urgent-injection decisions (FR-024, FR-031).
/// Pure: no I/O — applied inside the locked numbering transaction by
/// <see cref="AssignRealNumberHandler"/>.
/// </summary>
public interface IUrgentInjectionService
{
    /// <summary>
    /// Decides whether to assign a real number and how to update
    /// <c>NormalSinceLastUrgent</c> for a confirm-arrival or Instant Entry.
    /// </summary>
    UrgentInjectionResult Decide(UrgentInjectionRequest request);

    /// <summary>
    /// After a Normal confirmation, true when a waiting Present Urgent (no real number)
    /// should be injected next (ratio met).
    /// </summary>
    bool ShouldInjectWaitingUrgent(int normalSinceLastUrgent, int urgentRatio);
}

public sealed record UrgentInjectionRequest(
    AppointmentType Type,
    bool IsInstantEntry,
    int NormalSinceLastUrgent,
    int UrgentRatio,
    int HighestAssignedRealNumber);

public sealed record UrgentInjectionResult(
    bool AssignRealNumber,
    int? RealNumber,
    int NewNormalSinceLastUrgent);

public sealed class UrgentInjectionService : IUrgentInjectionService
{
    public UrgentInjectionResult Decide(UrgentInjectionRequest request)
    {
        if (request.UrgentRatio < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "UrgentRatio must be ≥ 1.");
        }

        if (request.NormalSinceLastUrgent < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "NormalSinceLastUrgent must be ≥ 0.");
        }

        if (request.HighestAssignedRealNumber < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "HighestAssignedRealNumber must be ≥ 0.");
        }

        var next = request.HighestAssignedRealNumber + 1;

        // Instant Entry: insert immediately, ratio-neutral (Clarification Q3 / FR-031).
        if (request.IsInstantEntry)
        {
            return new UrgentInjectionResult(
                AssignRealNumber: true,
                RealNumber: next,
                NewNormalSinceLastUrgent: request.NormalSinceLastUrgent);
        }

        if (request.Type == AppointmentType.Normal)
        {
            return new UrgentInjectionResult(
                AssignRealNumber: true,
                RealNumber: next,
                NewNormalSinceLastUrgent: request.NormalSinceLastUrgent + 1);
        }

        // Urgent: interleave only when the ratio is met — never jump to absolute front.
        if (request.NormalSinceLastUrgent >= request.UrgentRatio)
        {
            return new UrgentInjectionResult(
                AssignRealNumber: true,
                RealNumber: next,
                NewNormalSinceLastUrgent: 0);
        }

        // Ratio not met — urgent waits (Present without RealNumber) until enough Normals.
        return new UrgentInjectionResult(
            AssignRealNumber: false,
            RealNumber: null,
            NewNormalSinceLastUrgent: request.NormalSinceLastUrgent);
    }

    public bool ShouldInjectWaitingUrgent(int normalSinceLastUrgent, int urgentRatio)
    {
        if (urgentRatio < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(urgentRatio));
        }

        return normalSinceLastUrgent >= urgentRatio;
    }
}
