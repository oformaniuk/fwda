namespace Fwda.Proxy.Serialization;

using System.Text.Json.Serialization;
using Fwda.Proxy.Models;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RootResponse))]
[JsonSerializable(typeof(HealthResponse))]
internal partial class AuthJsonContext : JsonSerializerContext
{
}
