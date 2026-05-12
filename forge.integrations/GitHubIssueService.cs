using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

public record GitHubIssueRequest(
    string Title,
    string Body,
    string? Repository = null,
    string[]? Labels = null);

public record GitHubIssueResult(
    int Number,
    string Url,
    string HtmlUrl);

public interface IGitHubIssueService
{
    Task<GitHubIssueResult> CreateIssueAsync(int userId, int integrationId, GitHubIssueRequest request, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default);
}

public class GitHubIssueService(
    IHttpClientFactory httpClientFactory,
    IUserIntegrationService integrationService,
    ILogger<GitHubIssueService> logger) : IGitHubIssueService
{
    public async Task<GitHubIssueResult> CreateIssueAsync(int userId, int integrationId, GitHubIssueRequest request, CancellationToken ct = default)
    {
        var (client, repo) = await CreateAuthenticatedClient(userId, integrationId, ct);
        var targetRepo = request.Repository ?? repo;

        var payload = new
        {
            title = request.Title,
            body = request.Body,
            labels = request.Labels ?? Array.Empty<string>(),
        };

        var response = await client.PostAsJsonAsync(
            $"https://api.github.com/repos/{targetRepo}/issues", payload, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var issueResult = new GitHubIssueResult(
            result.GetProperty("number").GetInt32(),
            result.GetProperty("url").GetString()!,
            result.GetProperty("html_url").GetString()!);

        logger.LogInformation("GitHub: Created issue #{Number} in {Repo} for user {UserId}",
            issueResult.Number, targetRepo, userId);

        return issueResult;
    }

    public async Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        try
        {
            var (client, repo) = await CreateAuthenticatedClient(userId, integrationId, ct);
            var response = await client.GetAsync(
                $"https://api.github.com/repos/{repo}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GitHub: TestConnection failed for user {UserId}", userId);
            return false;
        }
    }

    private async Task<(HttpClient Client, string Repository)> CreateAuthenticatedClient(int userId, int integrationId, CancellationToken ct)
    {
        var credsJson = await integrationService.GetDecryptedCredentialsAsync(userId, integrationId, ct)
            ?? throw new InvalidOperationException("No credentials found for GitHub integration");

        var creds = JsonSerializer.Deserialize<JsonElement>(credsJson);
        var token = creds.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Missing access_token in GitHub credentials");
        var repository = creds.GetProperty("repository").GetString()
            ?? throw new InvalidOperationException("Missing repository in GitHub credentials");

        var client = httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QB-Engineer/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return (client, repository);
    }
}
