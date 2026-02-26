namespace Agpme.Bbg.Playground.Simulator.Api.Configuration;

public sealed class AwsSecretsOptions
{
    public string? Region { get; set; }       // e.g., "us-east-1"
    public string? Profile { get; set; }      // e.g., "dev" (optional)
    public string Arn { get; set; } = default!;
    public string KeyName { get; set; } = default!;
}