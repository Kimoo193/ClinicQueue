namespace ClinicQueue.Application.Abstractions;

/// <summary>Password hashing port (BCrypt work factor ≥ 12 — FR-011).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
