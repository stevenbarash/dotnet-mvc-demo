namespace DescopeDemo.Migration.Models;

public class LegacyUser
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string HashAlgorithm { get; set; } = "bcrypt";
    public string Roles { get; set; } = "";
}
