using AiBudgetAutomation.Functions.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddOptions<GitHubOptions>()
    .BindConfiguration(GitHubOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddHttpClient<IGitHubBudgetClient, GitHubBudgetClient>()
    .AddStandardResilienceHandler();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BudgetValidationService>();

builder.Build().Run();
