using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicQueue.Infrastructure.Migrations
{
    /// <summary>
    /// Closes the RLS bypass (Golden Rule #1 / constitution Principle I): every clinic-scoped
    /// table now has ROW LEVEL SECURITY <b>ENABLED and FORCED</b> so the tenant_isolation policy
    /// applies even to the table owner, and DML is granted to the non-owner application role
    /// (<c>clinicqueue_app</c>, RLS-enforced) and the cross-tenant role (<c>clinicqueue_bypassrls</c>).
    /// Idempotent: policies are dropped/recreated and grants are guarded by role existence, so it
    /// runs safely whether or not the dedicated roles have been provisioned yet
    /// (roles are created by docker/postgres-init or the test fixture).
    /// </summary>
    /// <inheritdoc />
    public partial class AddForceRlsAndAppRoleGrants : Migration
    {
        // Clinic-scoped tables (implement IClinicScoped) — get FORCE RLS + tenant_isolation policy.
        private static readonly string[] TenantTables =
        {
            "clinic_memberships",
            "subscriptions",
            "dependents",
            "audit_logs",
            "outbox_notifications",
            "push_subscriptions",
            "queue_sessions",
            "appointments",
            "patient_profiles",
            "visits",
            "prescriptions",
            "clinic_export_jobs",
            "ratings",
        };

        // Non-tenant tables the application role still needs DML on (no RLS).
        private static readonly string[] NonTenantTables =
        {
            "users",
            "clinics",
            "refresh_tokens",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                // ENABLE + FORCE so the owner/migration role is ALSO subject to RLS.
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");

                // Recreate the policy idempotently with the fail-closed predicate:
                // a missing/blank app.current_clinic → NULL → matches no row.
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"""
                    CREATE POLICY tenant_isolation ON {table}
                    USING ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''))
                    WITH CHECK ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''));
                    """);
            }

            foreach (var table in TenantTables)
            {
                GrantDml(migrationBuilder, table);
            }

            foreach (var table in NonTenantTables)
            {
                GrantDml(migrationBuilder, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to prior state: keep RLS ENABLED + policy, but remove FORCE and grants.
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                RevokeDml(migrationBuilder, table);
            }

            foreach (var table in NonTenantTables)
            {
                RevokeDml(migrationBuilder, table);
            }
        }

        private static void GrantDml(MigrationBuilder migrationBuilder, string table)
        {
            foreach (var role in new[] { "clinicqueue_app", "clinicqueue_bypassrls" })
            {
                migrationBuilder.Sql($"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                            GRANT SELECT, INSERT, UPDATE, DELETE ON {table} TO {role};
                        END IF;
                    END $$;
                    """);
            }
        }

        private static void RevokeDml(MigrationBuilder migrationBuilder, string table)
        {
            foreach (var role in new[] { "clinicqueue_app", "clinicqueue_bypassrls" })
            {
                migrationBuilder.Sql($"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                            REVOKE SELECT, INSERT, UPDATE, DELETE ON {table} FROM {role};
                        END IF;
                    END $$;
                    """);
            }
        }
    }
}
