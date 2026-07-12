namespace ClinicQueue.Seeder;

internal static class DemoReport
{
    public static void Print(SeedResult result)
    {
        Console.WriteLine();
        Console.WriteLine("Demo seed complete");
        Console.WriteLine("==================");
        Console.WriteLine($"Password for all accounts: {result.Password}");
        Console.WriteLine();
        Console.WriteLine("Clinics");
        Console.WriteLine("-------");
        Console.WriteLine($"  {result.Ghada.Name}");
        Console.WriteLine($"    slug: {result.Ghada.Slug}");
        Console.WriteLine($"    QR:   /r/{result.Ghada.Slug}  ({result.Ghada.QrUrl})");
        Console.WriteLine($"  {result.Smile.Name}");
        Console.WriteLine($"    slug: {result.Smile.Slug}");
        Console.WriteLine($"    QR:   /r/{result.Smile.Slug}  ({result.Smile.QrUrl})");
        Console.WriteLine();
        Console.WriteLine("Accounts");
        Console.WriteLine("--------");
        Console.WriteLine($"  {"Email",-28} {"Role",-18} {"Clinic",-14} Scenario");
        foreach (var row in result.Accounts)
        {
            Console.WriteLine(
                $"  {row.Email,-28} {row.Role,-18} {row.Clinic,-14} {row.Scenario}");
        }
        Console.WriteLine();
        Console.WriteLine("Next: open http://localhost:5173 and sign in, or run npm run test:e2e in web/");
    }
}
