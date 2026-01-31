namespace Fwda.Shared.Options;

using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Serialization;

/// <summary>
/// Portal configuration for a single application
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class PortalOptions : IEquatable<PortalOptions>
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;
    
    [YamlMember(Alias = "display")]
    public string Display { get; set; } = string.Empty;
    
    [YamlMember(Alias = "hostname")]
    public string Hostname { get; set; } = string.Empty;

    [YamlMember(Alias = "cookie_domain")]
    public string CookieDomain { get; set; } = string.Empty;

    [YamlMember(Alias = "oidc")]
    public OidcOptions Oidc { get; set; } = new();

    public bool Equals(PortalOptions? other)
    {
        if (other is null) return false;

        return Name == other.Name &&
               Display == other.Display &&
               Hostname == other.Hostname &&
               Oidc.Equals(other.Oidc);
    }

    public override bool Equals(object? obj) => Equals(obj as PortalOptions);

    public override int GetHashCode() => HashCode.Combine(Name, Display, Hostname, Oidc);
}
