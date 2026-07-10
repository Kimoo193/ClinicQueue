namespace ClinicQueue.Application.Medical;

/// <summary>
/// Thrown when a caller lacks medical least-privilege access (FR-085).
/// Endpoints map this to HTTP 403.
/// </summary>
public sealed class MedicalAccessException : InvalidOperationException
{
    public MedicalAccessException(string message) : base(message)
    {
    }
}
