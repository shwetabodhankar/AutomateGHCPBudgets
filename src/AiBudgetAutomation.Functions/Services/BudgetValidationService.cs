using AiBudgetAutomation.Functions.Models;

namespace AiBudgetAutomation.Functions.Services;

public sealed class BudgetValidationService(
    IGitHubBudgetClient gitHubBudgetClient,
    TimeProvider timeProvider)
{
    public async Task<BudgetValidationResponse> ValidateAsync(
        BudgetValidationRequest request,
        CancellationToken cancellationToken)
    {
        var reasons = ValidateFields(request);
        if (reasons.Count > 0)
        {
            return CreateResponse(request, null, reasons);
        }

        var context = await gitHubBudgetClient.GetValidationContextAsync(
            request.GitHubUsername,
            request.CostCenter,
            cancellationToken);

        var additionalCommitment = Math.Max(
            0,
            request.RequestedTotalUsd - context.CurrentIndividualBudgetUsd);

        if (additionalCommitment > context.CostCenterRemainingUsd)
        {
            reasons.Add(new ValidationReason(
                "COST_CENTER_CAPACITY_EXCEEDED",
                "The additional commitment exceeds the cost center's remaining budget."));
        }

        if (additionalCommitment > context.UniversalRemainingUsd)
        {
            reasons.Add(new ValidationReason(
                "UNIVERSAL_CAPACITY_EXCEEDED",
                "The additional commitment exceeds the universal budget's remaining capacity."));
        }

        return CreateResponse(request, context, reasons);
    }

    private List<ValidationReason> ValidateFields(BudgetValidationRequest request)
    {
        var reasons = new List<ValidationReason>();

        AddRequiredReason(reasons, request.RequestId, "REQUEST_ID_REQUIRED", "RequestId is required.");
        AddRequiredReason(reasons, request.RequesterEmail, "REQUESTER_REQUIRED", "RequesterEmail is required.");
        AddRequiredReason(reasons, request.GitHubUsername, "GITHUB_USERNAME_REQUIRED", "GitHubUsername is required.");
        AddRequiredReason(reasons, request.CostCenter, "COST_CENTER_REQUIRED", "CostCenter is required.");
        AddRequiredReason(reasons, request.Justification, "JUSTIFICATION_REQUIRED", "Justification is required.");

        if (request.RequestedTotalUsd <= 0 || decimal.Truncate(request.RequestedTotalUsd) != request.RequestedTotalUsd)
        {
            reasons.Add(new ValidationReason(
                "INVALID_AMOUNT",
                "RequestedTotalUsd must be a positive whole-dollar amount."));
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (request.ExpirationDate is null || request.ExpirationDate <= today)
        {
            reasons.Add(new ValidationReason(
                "INVALID_EXPIRATION",
                "ExpirationDate must be a future date."));
        }

        return reasons;
    }

    private BudgetValidationResponse CreateResponse(
        BudgetValidationRequest request,
        BudgetValidationContext? context,
        IReadOnlyList<ValidationReason> reasons)
    {
        var currentBudget = context?.CurrentIndividualBudgetUsd ?? 0;

        return new BudgetValidationResponse(
            request.RequestId,
            reasons.Count == 0,
            Guid.NewGuid().ToString("N"),
            currentBudget,
            request.RequestedTotalUsd,
            Math.Max(0, request.RequestedTotalUsd - currentBudget),
            context?.CostCenterRemainingUsd ?? 0,
            context?.UniversalRemainingUsd ?? 0,
            timeProvider.GetUtcNow(),
            reasons);
    }

    private static void AddRequiredReason(
        ICollection<ValidationReason> reasons,
        string value,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reasons.Add(new ValidationReason(code, message));
        }
    }
}
