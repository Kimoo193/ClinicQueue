namespace ClinicQueue.Application.Queue.Positioning;

/// <summary>Patient travel instruction derived from count-ahead vs clinic threshold (FR-025, FR-026).</summary>
public enum QueueInstruction
{
    WaitHome = 0,
    HeadToClinic = 1
}

public static class ThresholdLabeling
{
    /// <summary>
    /// Wait home when more people are ahead than the clinic threshold; otherwise head to clinic.
    /// </summary>
    public static QueueInstruction Label(int countAhead, int headToClinicThreshold)
    {
        if (headToClinicThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(headToClinicThreshold));
        }

        if (countAhead < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(countAhead));
        }

        return countAhead > headToClinicThreshold
            ? QueueInstruction.WaitHome
            : QueueInstruction.HeadToClinic;
    }
}
