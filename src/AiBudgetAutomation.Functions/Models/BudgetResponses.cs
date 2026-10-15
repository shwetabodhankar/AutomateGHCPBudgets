using AiBudgetAutomation.Functions.Services;

namespace AiBudgetAutomation.Functions.Models;

public sealed record ValidationReason(string Code, string Message);

public sealed record BudgetValidationResponse(
    string RequestId,
    bool Valid,
    string ValidationId,
    decimal CurrentIndividualBudgetUsd,
    decimal RequestedTotalUsd,
    decimal AdditionalCommitmentUsd,
    decimal CostCenterRemainingUsd,
    decimal UniversalRemainingUsd,
    DateTimeOffset ValidatedAt,
    IReadOnlyList<ValidationReason> Reasons);

public sealed record BudgetFulfillmentResponse(
    string RequestId,
    string ValidationId,
    string OperationId,
    string BudgetId,
    string Operation,
    GitHubBudget? PreviousBudget,
    GitHubBudget ResultingBudget,
    DateTimeOffset FulfilledAt);

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyList<string>? Details = null);
