namespace Fwda.Shared.Options;

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Fwda.Shared.Encryption;
using YamlDotNet.Serialization;

/// <summary>
/// OIDC configuration for a portal
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class OidcOptions : IEquatable<OidcOptions>
{
    [YamlMember(Alias = "issuer")]
    public string Issuer { get; set; } = string.Empty;

    [YamlMember(Alias = "client_id")]
    public string ClientId { get; set; } = string.Empty;

    [YamlMember(Alias = "client_secret")]
    public EncryptedString ClientSecret { get; set; } = new ();

    [YamlMember(Alias = "scopes")]
    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];

    public bool Equals(OidcOptions? other)
    {
        if (other is null) return false;

        return Issuer == other.Issuer &&
               ClientId == other.ClientId &&
               ClientSecret.Value == other.ClientSecret.Value &&
               Scopes.SequenceEqual(other.Scopes);
    }

    public override bool Equals(object? obj) => Equals(obj as OidcOptions);

    public override int GetHashCode() => HashCode.Combine(Issuer, ClientId, ClientSecret.Value, Scopes);
}