using FluentAssertions;

namespace ClinicQueue.UnitTests.Jobs;

/// <summary>
/// Lightweight architecture guard: background jobs under BYPASSRLS must not issue
/// ExecuteUpdateAsync / ExecuteDeleteAsync on tenant tables without an explicit ClinicId
/// predicate in the same expression chain. Prefer per-clinic queries (see AutoAbsenceJob,
/// BookingReminderJob, OutboxDispatchJob).
/// </summary>
public sealed class BackgroundJobTenantGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClinicQueue.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate ClinicQueue.slnx from test base directory.");
    }

    private static IEnumerable<string> JobSourceFiles(string repoRoot)
    {
        var jobsDir = Path.Combine(repoRoot, "src", "ClinicQueue.Infrastructure", "Jobs");
        foreach (var path in Directory.EnumerateFiles(jobsDir, "*.cs"))
        {
            yield return path;
        }

        // OutboxDispatchJob lives with notification infrastructure.
        yield return Path.Combine(
            repoRoot,
            "src",
            "ClinicQueue.Infrastructure",
            "Notifications",
            "NotificationInfrastructure.cs");
    }

    [Fact]
    public void Background_job_sources_do_not_ExecuteUpdate_or_ExecuteDelete_without_ClinicId()
    {
        var root = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in JobSourceFiles(root))
        {
            if (!File.Exists(file))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            // Strip block comments so doc samples cannot false-positive.
            var scrubbed = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"/\*.*?\*/",
                " ",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (var op in new[] { "ExecuteUpdateAsync", "ExecuteDeleteAsync", "ExecuteUpdate(", "ExecuteDelete(" })
            {
                var index = 0;
                while ((index = scrubbed.IndexOf(op, index, StringComparison.Ordinal)) >= 0)
                {
                    // Window: from preceding semicolon/newline to end of statement-ish span.
                    var start = scrubbed.LastIndexOf('\n', index);
                    start = start < 0 ? 0 : start;
                    var end = scrubbed.IndexOf(';', index);
                    if (end < 0)
                    {
                        end = Math.Min(scrubbed.Length, index + 400);
                    }

                    var window = scrubbed[start..end];
                    if (!window.Contains("ClinicId", StringComparison.Ordinal))
                    {
                        violations.Add($"{Path.GetFileName(file)}: {op} without ClinicId in expression window");
                    }

                    index += op.Length;
                }
            }
        }

        violations.Should().BeEmpty(
            "BYPASSRLS jobs must never bulk-mutate tenant tables without an explicit ClinicId predicate. Violations: {0}",
            string.Join("; ", violations));
    }

    [Fact]
    public void AutoAbsenceJob_and_OutboxDispatchJob_enumerate_or_filter_by_ClinicId()
    {
        var root = FindRepoRoot();
        var autoAbsence = File.ReadAllText(
            Path.Combine(root, "src", "ClinicQueue.Infrastructure", "Jobs", "AutoAbsenceJob.cs"));
        var outbox = File.ReadAllText(
            Path.Combine(root, "src", "ClinicQueue.Infrastructure", "Notifications", "NotificationInfrastructure.cs"));

        autoAbsence.Should().Contain("foreach (var clinic in clinics)");
        autoAbsence.Should().Contain("a.ClinicId == clinicId");

        // Outbox: discover clinics, then batch with ClinicId predicate.
        outbox.Should().Contain("ProcessClinicBatchAsync");
        outbox.Should().Contain("n.ClinicId == clinicId");
    }
}
