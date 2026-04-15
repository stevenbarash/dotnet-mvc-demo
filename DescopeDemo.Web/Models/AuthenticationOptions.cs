namespace DescopeDemo.Web.Models;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string ValidationMode { get; set; } = "JwtBearer";
}
