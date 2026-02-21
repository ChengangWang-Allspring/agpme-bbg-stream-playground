using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agpme.Bbg.Stream.Playground.Shared.Config;


public sealed class AwsSecretsOptions
{
    public string? Region { get; set; }
    public string? Profile { get; set; }
    public string? Arn { get; set; }
    public string? SecretKeyName { get; set; }
}

