using System.Text.Json.Serialization;

namespace AiBudgetAutomation.Functions.Services;

public sealed record GitHubBudget
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("budget_scope")]
    public required string Scope { get; init; }

    [JsonPropertyName("budget_entity_name")]
    public string? EntityName { get; init; }

    [JsonPropertyName("user")]
    public string? User { get; init; }

    [JsonPropertyName("budget_amount")]
    public int Amount { get; init; }

    [JsonPropertyName("consumed_amount")]
    public decimal ConsumedAmount { get; init; }

    [JsonPropertyName("budget_product_sku")]
    public required string ProductSku { get; init; }

    [JsonPropertyName("expires_at")]
    public DateOnly? ExpiresAt { get; init; }
}

internal sealed record GitHubBudgetListResponse
{
    [JsonPropertyName("budgets")]
    public required IReadOnlyList<GitHubBudget> Budgets { get; init; }

    [JsonPropertyName("has_next_page")]
    public bool HasNextPage { get; init; }
}

internal sealed record GitHubBudgetMutationResponse
{
    [JsonPropertyName("budget")]
    public required GitHubBudget Budget { get; init; }
}

public sealed record BudgetValidationContext(
    decimal CurrentIndividualBudgetUsd,
    decimal CostCenterRemainingUsd,
    decimal UniversalRemainingUsd);

public sealed record BudgetUpsertResult(
    string OperationId,
    string Operation,
    GitHubBudget? PreviousBudget,
    GitHubBudget Budget);
