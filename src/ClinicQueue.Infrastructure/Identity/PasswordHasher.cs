using ClinicQueue.Application.Abstractions;

namespace ClinicQueue.Infrastructure.Identity;

/// <summary>
/// BCrypt password hashing with work factor ≥ 12 (FR-011).
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);

    public bool Verify(string password, string passwordHash) =>
        BCrypt.Net.BCrypt.Verify(password, passwordHash);
}
