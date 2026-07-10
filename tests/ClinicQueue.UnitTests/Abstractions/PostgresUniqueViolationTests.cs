using ClinicQueue.Application.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ClinicQueue.UnitTests.Abstractions;

public sealed class PostgresUniqueViolationTests
{
    [Fact]
    public void Matches_true_for_nested_23505()
    {
        var pg = new PostgresException(
            messageText: "duplicate key value violates unique constraint \"IX_appointments_active_membership\"",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: PostgresUniqueViolation.SqlState);
        var ex = new DbUpdateException("save failed", pg);

        PostgresUniqueViolation.Matches(ex).Should().BeTrue();
        PostgresUniqueViolation.Matches(ex, "active_membership").Should().BeTrue();
        PostgresUniqueViolation.Matches(ex, "TemporaryNumber").Should().BeFalse();
    }

    [Fact]
    public void Matches_false_for_non_unique_db_errors()
    {
        var pg = new PostgresException(
            messageText: "deadlock detected",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: "40P01");
        var ex = new DbUpdateException("save failed", pg);
        PostgresUniqueViolation.Matches(ex).Should().BeFalse();
    }
}
