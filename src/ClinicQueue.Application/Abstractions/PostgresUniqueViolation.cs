using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Abstractions;

/// <summary>
/// Detects PostgreSQL unique_violation (SQLSTATE 23505) without taking a hard
/// Application → Npgsql dependency. Walks InnerException for PostgresException.
/// </summary>
public static class PostgresUniqueViolation
{
    public const string SqlState = "23505";

    public static bool Matches(DbUpdateException exception, string? constraintNameContains = null)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name != "PostgresException")
            {
                continue;
            }

            var state = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
            if (state != SqlState)
            {
                continue;
            }

            if (constraintNameContains is null)
            {
                return true;
            }

            var constraint = current.GetType().GetProperty("ConstraintName")?.GetValue(current) as string;
            var haystack = string.IsNullOrEmpty(constraint) ? current.Message : constraint;
            return haystack.Contains(constraintNameContains, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
