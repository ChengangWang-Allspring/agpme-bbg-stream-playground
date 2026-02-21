// Shared/Helpers/AwsSecretHelper.cs
using System.Text.Json;
using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace Agpme.Bbg.Stream.Playground.Shared.Helpers;

/// <summary>
/// Minimal helper to read a specific key from an AWS Secrets Manager secret,
/// using a named local AWS profile + secret ARN
/// </summary>
public static class AwsSecretHelper
{
    /// <summary>
    /// Returns the value for <paramref name="secretKeyName"/> from the JSON SecretString
    /// associated with the given <paramref name="secretArn"/>.
    /// If the secret is not JSON or the key is not found, throws InvalidOperationException.
    /// Region resolution order:
    ///   1) explicit <paramref name="region"/>
    ///   2) derived from ARN (if ARN-like)
    ///   3) "us-east-1"
    /// </summary>
    public static async Task<string> GetSecretValueByKeyAsync(
        string profile,
        string secretArn,
        string secretKeyName,
        string? region = null)
    {
        if (string.IsNullOrWhiteSpace(secretArn))
            throw new ArgumentException("Secret ARN or Name must be provided.", nameof(secretArn));
        if (string.IsNullOrWhiteSpace(secretKeyName))
            throw new ArgumentException("SecretKeyName must be provided.", nameof(secretKeyName));

        var resolvedRegion = !string.IsNullOrWhiteSpace(region)
            ? region!
            : TryParseRegionFromArn(secretArn) ?? "us-east-1";

        using var client = CreateSecretsClient(profile, resolvedRegion);

        var response = await client.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = secretArn,
            VersionStage = "AWSCURRENT"
        });

        if (string.IsNullOrWhiteSpace(response.SecretString))
            throw new InvalidOperationException("Secret has no SecretString payload.");

        // Expect a JSON object: { "<secretKeyName>": "<connection-string>", ... }
        try
        {
            using var doc = JsonDocument.Parse(response.SecretString);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(secretKeyName, out var val) &&
                val.ValueKind == JsonValueKind.String)
            {
                return val.GetString()!;
            }
            throw new InvalidOperationException(
                $"Secret JSON does not contain key '{secretKeyName}'.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Secret payload is not valid JSON (a key-based lookup is required).", ex);
        }
    }

    /// <summary>
    /// Maps env string to a named shared-credentials profile.
    /// DEV -> "dev", UAT -> "uat", PROD -> "prod".
    /// </summary>
    public static string ResolveProfileForEnv(string? env) =>
        (env ?? "DEV").Trim().ToUpperInvariant() switch
        {
            "PROD" => "prod",
            "UAT" => "uat",
            _ => "dev",
        };

    private static AmazonSecretsManagerClient CreateSecretsClient(string? profile, string region)
    {
        var chain = new CredentialProfileStoreChain();

        if (!string.IsNullOrWhiteSpace(profile) &&
            chain.TryGetAWSCredentials(profile, out var credsFromProfile))
        {
            return new AmazonSecretsManagerClient(credsFromProfile, RegionEndpoint.GetBySystemName(region));
        }

        if (chain.TryGetAWSCredentials("default", out var credsFromDefault))
        {
            return new AmazonSecretsManagerClient(credsFromDefault, RegionEndpoint.GetBySystemName(region));
        }

        // Fallback to SDK default chain (env vars, role, IMDS, etc.)
        return new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(region));
    }

    /// <summary>
    /// arn:aws:secretsmanager:{region}:{account}:secret:{name}
    /// Returns region if the input looks like an ARN; otherwise null.
    /// </summary>
    private static string? TryParseRegionFromArn(string arn)
    {
        if (!arn.StartsWith("arn:", StringComparison.OrdinalIgnoreCase))
            return null;

        // arn:partition:service:region:account-id:resource...
        var parts = arn.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4 ? parts[3] : null;
    }
}