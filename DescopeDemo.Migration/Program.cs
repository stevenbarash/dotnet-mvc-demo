using System.Globalization;
using DescopeDemo.Migration.Models;
using DescopeDemo.Migration.Services;
using CsvHelper;
using CsvHelper.Configuration;

Console.WriteLine("=== Descope Demo — User Migration Tool ===");
Console.WriteLine();

var projectId = Environment.GetEnvironmentVariable("DESCOPE_PROJECT_ID") ?? "";
var managementKey = Environment.GetEnvironmentVariable("DESCOPE_MANAGEMENT_KEY") ?? "";

if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(managementKey))
{
    Console.WriteLine("Error: Set DESCOPE_PROJECT_ID and DESCOPE_MANAGEMENT_KEY environment variables.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  export DESCOPE_PROJECT_ID=your-project-id");
    Console.WriteLine("  export DESCOPE_MANAGEMENT_KEY=your-management-key");
    Console.WriteLine("  dotnet run [path-to-csv]");
    return 1;
}

var csvPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "SampleData", "legacy-users.csv");

if (!File.Exists(csvPath))
{
    Console.WriteLine($"Error: CSV file not found at {csvPath}");
    return 1;
}

Console.WriteLine($"Reading users from: {csvPath}");

var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, TrimOptions = TrimOptions.Trim };

List<LegacyUser> users;
using (var reader = new StreamReader(csvPath))
using (var csv = new CsvReader(reader, config))
{
    users = csv.GetRecords<LegacyUser>().ToList();
}

Console.WriteLine($"Found {users.Count} users to migrate.");
Console.WriteLine();

var service = new DescopeMigrationService(projectId, managementKey);
var report = await service.MigrateUsersAsync(users);

Console.WriteLine();
Console.WriteLine("=== Migration Report ===");
Console.WriteLine($"  Created: {report.Created}");
Console.WriteLine($"  Failed:  {report.Failed}");

if (report.Errors.Count > 0)
{
    Console.WriteLine("  Errors:");
    foreach (var error in report.Errors)
        Console.WriteLine($"    - {error}");
}

Console.WriteLine();
Console.WriteLine("Migration complete.");

return report.Failed > 0 ? 1 : 0;
