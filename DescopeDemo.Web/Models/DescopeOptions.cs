using System.ComponentModel.DataAnnotations;

namespace DescopeDemo.Web.Models;

public sealed class DescopeOptions
{
    public const string SectionName = "Descope";

    [Required(ErrorMessage = "Descope ProjectId is required. Set it in appsettings.json or user secrets.")]
    public string ProjectId { get; set; } = string.Empty;

    public string? ManagementKey { get; set; }
}
