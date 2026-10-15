using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AiBudgetAutomation.Functions.Services;

public sealed class GitHubBudgetClient(
    HttpClient httpClient,
    IOptions<GitHubOptions> options) : IGitHubBudgetClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GitHubOptions options = options.Value;

    public async Task<BudgetValidationContext> GetValidationContextAsync(
        string gitHubUsername,
        string costCenter,
        CancellationToken cancellationToken)
    {
        var individualTask = ListBudgetsAsync("user", gitHubUsername, cancellationToken);
        var costCenterTask = ListBudgetsAsync("multi_user_cost_center", null, cancellationToken);
        var universalTask = ListBudgetsAsync("multi_user_customer", null, cancellationToken);

        await Task.WhenAll(individualTask, costCenterTask, universalTask);

        var individualBudget = (await individualTask)
            .Where(IsAiCreditBudget)
            .FirstOrDefault(budget => string.Equals(
                budget.User,
                gitHubUsername,
                StringComparison.OrdinalIgnoreCase));

        var costCenterBudget = (await costCenterTask)
            .Where(IsAiCreditBudget)
            .FirstOrDefault(budget => string.Equals(
                budget.EntityName,
                costCenter,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new BudgetConfigurationException(
                $"No AI-credit universal budget is configured for cost center '{costCenter}'.");

        var universalBudget = (await universalTask)
            .Where(IsAiCreditBudget)
            .OrderBy(GetRemainingAmount)
            .FirstOrDefault()
            ?? throw new BudgetConfigurationException(
                "No enterprise universal AI-credit budget is configured.");

        return new BudgetValidationContext(
            individualBudget?.Amount ?? 0,
            GetRemainingAmount(costCenterBudget),
            GetRemainingAmount(universalBudget));
    }

    public async Task<BudgetUpsertResult> UpsertUserBudgetAsync(
        string gitHubUsername,
        int requestedTotalUsd,
        DateOnly? expirationDate,
        CancellationToken cancellationToken)
    {
        var existingBudget = (await ListBudgetsAsync("user", gitHubUsername, cancellationToken))
            .Where(IsAiCreditBudget)
            .FirstOrDefault(budget => string.Equals(
                budget.User,
                gitHubUsername,
                StringComparison.OrdinalIgnoreCase));

        var operationId = Guid.NewGuid().ToString("N");
        GitHubBudget resultingBudget;
        string operation;

        if (existingBudget is null)
        {
            operation = "Created";
            var createPayload = new
            {
                budget_amount = requestedTotalUsd,
                prevent_further_usage = true,
                budget_scope = "user",
                budget_entity_name = string.Empty,
                budget_type = "BundlePricing",
                budget_product_sku = "ai_credits",
                budget_alerting = new
                {
                    will_alert = false,
                    alert_recipients = Array.Empty<string>()
                },
                user = gitHubUsername,
                expires_at = expirationDate
            };

            resultingBudget = await SendMutationAsync(
                HttpMethod.Post,
                GetBudgetsPath(),
                createPayload,
                cancellationToken);
        }
        else
        {
            operation = "Updated";
            var updatePayload = new
            {
                budget_amount = requestedTotalUsd,
                prevent_further_usage = true,
                expires_at = expirationDate
            };

            resultingBudget = await SendMutationAsync(
                HttpMethod.Patch,
                $"{GetBudgetsPath()}/{Uri.EscapeDataString(existingBudget.Id)}",
                updatePayload,
                cancellationToken);
        }

        var verifiedBudget = await GetBudgetAsync(resultingBudget.Id, cancellationToken);
        if (verifiedBudget.Amount != requestedTotalUsd ||
            verifiedBudget.ExpiresAt != expirationDate ||
            !string.Equals(verifiedBudget.User, gitHubUsername, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException(
                "GitHub returned a budget state that does not match the requested values.");
        }

        return new BudgetUpsertResult(
            operationId,
            operation,
            existingBudget,
            verifiedBudget);
    }

    private async Task<IReadOnlyList<GitHubBudget>> ListBudgetsAsync(
        string scope,
        string? user,
        CancellationToken cancellationToken)
    {
        var budgets = new List<GitHubBudget>();
        var page = 1;

        while (true)
        {
            var query = $"?scope={Uri.EscapeDataString(scope)}&per_page=100&page={page}";
            if (!string.IsNullOrWhiteSpace(user))
            {
                query += $"&user={Uri.EscapeDataString(user)}";
            }

            using var request = CreateRequest(HttpMethod.Get, GetBudgetsPath() + query);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var pageResult = await response.Content.ReadFromJsonAsync<GitHubBudgetListResponse>(
                JsonOptions,
                cancellationToken)
                ?? throw new HttpRequestException("GitHub returned an empty budget response.");

            budgets.AddRange(pageResult.Budgets);
            if (!pageResult.HasNextPage)
            {
                return budgets;
            }

            page++;
        }
    }

    private async Task<GitHubBudget> GetBudgetAsync(
        string budgetId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"{GetBudgetsPath()}/{Uri.EscapeDataString(budgetId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<GitHubBudget>(JsonOptions, cancellationToken)
            ?? throw new HttpRequestException("GitHub returned an empty budget response.");
    }

    private async Task<GitHubBudget> SendMutationAsync(
        HttpMethod method,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        request.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GitHubBudgetMutationResponse>(
            JsonOptions,
            cancellationToken);

        return result?.Budget
            ?? throw new HttpRequestException("GitHub returned an empty mutation response.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri($"https://api.github.com{path}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", options.ApiVersion);
        request.Headers.UserAgent.ParseAdd("AVEVA-AI-Budget-Automation/1.0");
        return request;
    }

    private string GetBudgetsPath() =>
        $"/enterprises/{Uri.EscapeDataString(options.Enterprise)}/settings/billing/budgets";

    private static bool IsAiCreditBudget(GitHubBudget budget) =>
        string.Equals(budget.ProductSku, "ai_credits", StringComparison.OrdinalIgnoreCase);

    private static decimal GetRemainingAmount(GitHubBudget budget) =>
        Math.Max(0, budget.Amount - budget.ConsumedAmount);
}

public sealed class BudgetConfigurationException(string message) : Exception(message);
