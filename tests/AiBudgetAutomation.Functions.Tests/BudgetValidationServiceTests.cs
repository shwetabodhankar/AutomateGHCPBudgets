using AiBudgetAutomation.Functions.Models;
using AiBudgetAutomation.Functions.Services;

namespace AiBudgetAutomation.Functions.Tests;

public sealed class BudgetValidationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidateAsync_ReturnsFieldErrorsWithoutCallingGitHub()
    {
        var client = new FakeGitHubBudgetClient(new BudgetValidationContext(0, 100, 100));
        var service = CreateService(client);
        var request = new BudgetValidationRequest(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            10.50m,
            DateOnly.FromDateTime(Now.UtcDateTime),
            string.Empty);

        var result = await service.ValidateAsync(request, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Reasons, reason => reason.Code == "REQUEST_ID_REQUIRED");
        Assert.Contains(result.Reasons, reason => reason.Code == "INVALID_AMOUNT");
        Assert.Contains(result.Reasons, reason => reason.Code == "INVALID_EXPIRATION");
        Assert.Equal(0, client.ValidationCallCount);
    }

    [Fact]
    public async Task ValidateAsync_UsesOnlyAdditionalCommitmentForCapacity()
    {
        var client = new FakeGitHubBudgetClient(new BudgetValidationContext(40, 60, 60));
        var service = CreateService(client);

        var result = await service.ValidateAsync(CreateValidRequest(100), CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Equal(60, result.AdditionalCommitmentUsd);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public async Task ValidateAsync_RejectsRequestOverCostCenterCapacity()
    {
        var client = new FakeGitHubBudgetClient(new BudgetValidationContext(40, 59, 100));
        var service = CreateService(client);

        var result = await service.ValidateAsync(CreateValidRequest(100), CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Reasons, reason => reason.Code == "COST_CENTER_CAPACITY_EXCEEDED");
        Assert.DoesNotContain(result.Reasons, reason => reason.Code == "UNIVERSAL_CAPACITY_EXCEEDED");
    }

    [Fact]
    public async Task ValidateAsync_RejectsRequestOverUniversalCapacity()
    {
        var client = new FakeGitHubBudgetClient(new BudgetValidationContext(40, 100, 59));
        var service = CreateService(client);

        var result = await service.ValidateAsync(CreateValidRequest(100), CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Reasons, reason => reason.Code == "UNIVERSAL_CAPACITY_EXCEEDED");
        Assert.DoesNotContain(result.Reasons, reason => reason.Code == "COST_CENTER_CAPACITY_EXCEEDED");
    }

    private static BudgetValidationService CreateService(IGitHubBudgetClient client) =>
        new(client, new FixedTimeProvider(Now));

    private static BudgetValidationRequest CreateValidRequest(decimal requestedTotalUsd) =>
        new(
            "RITM0012345",
            "requester@example.com",
            "octocat",
            "CC-1234",
            requestedTotalUsd,
            new DateOnly(2026, 12, 31),
            "Project delivery");

    private sealed class FakeGitHubBudgetClient(BudgetValidationContext context) : IGitHubBudgetClient
    {
        public int ValidationCallCount { get; private set; }

        public Task<BudgetValidationContext> GetValidationContextAsync(
            string gitHubUsername,
            string costCenter,
            CancellationToken cancellationToken)
        {
            ValidationCallCount++;
            return Task.FromResult(context);
        }

        public Task<BudgetUpsertResult> UpsertUserBudgetAsync(
            string gitHubUsername,
            int requestedTotalUsd,
            DateOnly? expirationDate,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
