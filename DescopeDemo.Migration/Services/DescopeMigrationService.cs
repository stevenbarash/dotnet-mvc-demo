using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DescopeDemo.Migration.Models;

namespace DescopeDemo.Migration.Services;

public class MigrationReport
{
    public int Created { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class DescopeMigrationService
{
    private readonly string _projectId;
    private readonly string _managementKey;
    private readonly HttpClient _httpClient;

    public DescopeMigrationService(string projectId, string managementKey)
    {
        _projectId = projectId;
        _managementKey = managementKey;
        _httpClient = new HttpClient { BaseAddress = new Uri("https://api.descope.com") };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", $"{_projectId}:{_managementKey}");
    }

    public async Task<MigrationReport> MigrateUsersAsync(List<LegacyUser> users)
    {
        var report = new MigrationReport();
        var descopeUsers = users.Select(BuildDescopeUser).ToList();

        var requestBody = new { users = descopeUsers, invite = false, sendMail = false, sendSMS = false };
        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        Console.WriteLine("Sending batch create request...");
        Console.WriteLine($"Users to migrate: {users.Count}");

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/v1/mgmt/user/create/batch", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            if (result.TryGetProperty("createdUsers", out var createdUsers))
                report.Created = createdUsers.GetArrayLength();
            Console.WriteLine($"Successfully created {report.Created} users.");
        }
        else
        {
            report.Failed = users.Count;
            report.Errors.Add($"Batch create failed: {response.StatusCode} — {responseBody}");
            Console.WriteLine($"Migration failed: {response.StatusCode}");
            Console.WriteLine(responseBody);
        }

        return report;
    }

    private object BuildDescopeUser(LegacyUser user)
    {
        var roleNames = user.Roles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.ToLowerInvariant())
            .ToArray();

        return new
        {
            loginId = user.Email,
            email = user.Email,
            name = user.DisplayName,
            verifiedEmail = true,
            roleNames,
            hashedPassword = BuildHashedPassword(user)
        };
    }

    private object BuildHashedPassword(LegacyUser user)
    {
        return user.HashAlgorithm.ToLowerInvariant() switch
        {
            "bcrypt" => new { bcrypt = new { hash = user.PasswordHash } },
            "pbkdf2" => new { pbkdf2 = new { hash = user.PasswordHash, salt = "", iterations = 10000, type = "sha256" } },
            "argon2" => new { argon2 = new { hash = user.PasswordHash, salt = "", iterations = 3, memory = 65536, threads = 4, type = "argon2id" } },
            _ => new { bcrypt = new { hash = user.PasswordHash } }
        };
    }
}
