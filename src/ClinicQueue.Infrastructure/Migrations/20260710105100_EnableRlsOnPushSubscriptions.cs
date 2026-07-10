using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicQueue.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnableRlsOnPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: covers DBs that already applied AddPushSubscriptions without RLS.
            migrationBuilder.Sql("ALTER TABLE push_subscriptions ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON push_subscriptions;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON push_subscriptions
                USING ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''))
                WITH CHECK ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON push_subscriptions;");
            migrationBuilder.Sql("ALTER TABLE push_subscriptions DISABLE ROW LEVEL SECURITY;");
        }
    }
}
