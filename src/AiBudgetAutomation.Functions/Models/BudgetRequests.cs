namespace AiBudgetAutomation.Functions.Models;

public sealed record BudgetValidationRequest(
    string RequestId,
    string RequesterEmail,
    string GitHubUsername,
    string CostCenter,
    decimal RequestedTotalUsd,
    DateOnly? ExpirationDate,
    string Justification);

public sealed record BudgetFulfillmentRequest(
    BudgetValidationRequest Request,
    string ValidationId,
    string ApprovedBy,
    DateTimeOffset ApprovedAt)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ValidationId))
        {
            errors.Add("ValidationId is required.");
        }

        if (string.IsNullOrWhiteSpace(ApprovedBy))
        {
            errors.Add("ApprovedBy is required.");
        }

        if (ApprovedAt == default || ApprovedAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            errors.Add("ApprovedAt must contain a valid approval timestamp.");
        }

        return errors;
    }
}
