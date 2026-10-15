using System.Net;
using System.Text.Json;
using AiBudgetAutomation.Functions.Models;
using AiBudgetAutomation.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AiBudgetAutomation.Functions.Functions;

public sealed class BudgetRequestFunctions(
    BudgetValidationService validationService,
    IGitHubBudgetClient gitHubBudgetClient,
    ILogger<BudgetRequestFunctions> logger)
{
    [Function("ValidateBudgetRequest")]
    public async Task<HttpResponseData> ValidateAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "budget-requests/validate")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await ReadRequestAsync<BudgetValidationRequest>(request, cancellationToken);
        if (payload is null)
        {
            return await WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "INVALID_REQUEST",
                "The request body must contain a valid budget request.",
                cancellationToken);
        }

        try
        {
            var result = await validationService.ValidateAsync(payload, cancellationToken);
            return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
        }
        catch (BudgetConfigurationException exception)
        {
            logger.LogError(exception, "Budget validation configuration is incomplete.");
            return await WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "BUDGET_CONFIGURATION_ERROR",
                exception.Message,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "GitHub budget validation failed.");
            return await WriteErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "GITHUB_API_ERROR",
                "GitHub budget data could not be retrieved.",
                cancellationToken);
        }
    }

    [Function("FulfillBudgetRequest")]
    public async Task<HttpResponseData> FulfillAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "budget-requests/fulfill")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await ReadRequestAsync<BudgetFulfillmentRequest>(request, cancellationToken);
        if (payload is null)
        {
            return await WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "INVALID_REQUEST",
                "The request body must contain a valid fulfillment request.",
                cancellationToken);
        }

        var requestErrors = payload.Validate();
        if (requestErrors.Count > 0)
        {
            return await WriteJsonAsync(
                request,
                HttpStatusCode.BadRequest,
                new ApiError("INVALID_APPROVAL", "Approval information is invalid.", requestErrors),
                cancellationToken);
        }

        try
        {
            var validation = await validationService.ValidateAsync(payload.Request, cancellationToken);
            if (!validation.Valid)
            {
                return await WriteJsonAsync(request, HttpStatusCode.Conflict, validation, cancellationToken);
            }

            var result = await gitHubBudgetClient.UpsertUserBudgetAsync(
                payload.Request.GitHubUsername,
                checked((int)payload.Request.RequestedTotalUsd),
                payload.Request.ExpirationDate,
                cancellationToken);

            return await WriteJsonAsync(
                request,
                HttpStatusCode.OK,
                new BudgetFulfillmentResponse(
                    payload.Request.RequestId,
                    payload.ValidationId,
                    result.OperationId,
                    result.Budget.Id,
                    result.Operation,
                    result.PreviousBudget,
                    result.Budget,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (BudgetConfigurationException exception)
        {
            logger.LogError(exception, "Budget fulfillment configuration is incomplete.");
            return await WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "BUDGET_CONFIGURATION_ERROR",
                exception.Message,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "GitHub budget fulfillment failed.");
            return await WriteErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "GITHUB_API_ERROR",
                "GitHub rejected or could not complete the budget operation.",
                cancellationToken);
        }
    }

    private static async Task<T?> ReadRequestAsync<T>(
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await request.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static Task<HttpResponseData> WriteErrorAsync(
        HttpRequestData request,
        HttpStatusCode status,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        WriteJsonAsync(request, status, new ApiError(code, message), cancellationToken);

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request,
        HttpStatusCode status,
        T value,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(value, cancellationToken);
        return response;
    }
}
