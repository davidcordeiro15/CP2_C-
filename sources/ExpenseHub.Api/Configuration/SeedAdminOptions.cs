using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Configuration;

internal sealed class SeedAdminOptions
{
    public const string SectionName = "SeedAdmin";

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}
