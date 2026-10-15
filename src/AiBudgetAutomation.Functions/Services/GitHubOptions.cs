using System.ComponentModel.DataAnnotations;

namespace AiBudgetAutomation.Functions.Services;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    [Required]
    public string Enterprise { get; init; } = string.Empty;

    [Required]
    public string Token { get; init; } = string.Empty;

    [Required]
    public string ApiVersion { get; init; } = "2026-03-10";
}
