namespace AiBudgetAutomation.Functions.Services;

public interface IGitHubBudgetClient
{
    Task<BudgetValidationContext> GetValidationContextAsync(
        string gitHubUsername,
        string costCenter,
        CancellationToken cancellationToken);

    Task<BudgetUpsertResult> UpsertUserBudgetAsync(
        string gitHubUsername,
        int requestedTotalUsd,
        DateOnly? expirationDate,
        CancellationToken cancellationToken);
}
