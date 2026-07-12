using ClinicQueue.Infrastructure.Persistence;
using ClinicQueue.Seeder;
using Microsoft.EntityFrameworkCore;

// ClinicQueue demo seeder.
//
// Runs through the SAME three-role model as production (see docs/architecture.md):
//   ConnectionStrings__Migrations → owner (clinicqueue)            — DDL / EF migrations only.
//   ConnectionStrings__Privileged → clinicqueue_bypassrls (BYPASSRLS) — pre-auth / no-tenant writes
//                                                                    (clinics, users, memberships, subscriptions).
//   ConnectionStrings__Default    → clinicqueue_app (NOBYPASSRLS)  — every tenant-scoped write, with
//                                                                    app.current_clinic set per clinic.
//
// It deliberately does NOT seed as the superuser. If the app/bypass roles are missing or unprivileged
// the seed fails loudly — that is a real defect in the role model worth surfacing, not something to
// paper over by falling back to the owner connection.

static string Require(string name) =>
    Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Environment variable {name} is required. Run via ./scripts/seed-demo.sh, which wires all three role connections.");

var migrationsConn = Require("ConnectionStrings__Migrations");
var privilegedConn = Require("ConnectionStrings__Privileged");
var appConn = Require("ConnectionStrings__Default");

Console.WriteLine("ClinicQueue demo seed");
Console.WriteLine("=====================");

// 1) Migrations (owner connection) — bring the schema up to date. Idempotent by design.
Console.Write("• Applying migrations (owner role)… ");
var migrationsOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(migrationsConn, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
    .Options;
await using (var migrationsDb = new AppDbContext(migrationsOptions))
{
    await migrationsDb.Database.MigrateAsync();
}
Console.WriteLine("done.");

// 2) Seed. Cleanup + identity writes go through the privileged (BYPASSRLS) context; all tenant data
//    goes through an app-role context with app.current_clinic pinned to the target clinic.
var seeder = new DemoSeeder(privilegedConn, appConn);
SeedResult result;
try
{
    result = await seeder.RunAsync();
}
catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg
                                                                 && pg.SqlState == "42501")
{
    // 42501 = insufficient_privilege → the app/bypass role lacks a grant, or RLS rejected the row.
    Console.Error.WriteLine();
    Console.Error.WriteLine("SEED FAILED under the non-owner role model (PostgreSQL 42501 insufficient_privilege / RLS).");
    Console.Error.WriteLine("This means the seed could not run as the real app/bypass roles — exactly the bug this");
    Console.Error.WriteLine("harness is meant to catch. Ensure ./scripts/seed-demo.sh provisioned the roles and grants.");
    Console.Error.WriteLine($"Detail: {pg.MessageText}");
    return 1;
}

DemoReport.Print(result);
return 0;
