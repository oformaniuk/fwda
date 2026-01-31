namespace Fwda.Shared.Options;

using System.Diagnostics.CodeAnalysis;
using Fwda.Shared.Encryption;
using YamlDotNet.Serialization;

/// <summary>
/// Authentication configuration containing session settings and portals
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class AuthOptions : IEquatable<AuthOptions>
{
    public const string SectionName = "auth";
    
    [YamlMember(Alias = "session_secret")]
    public EncryptedString SessionSecret { get; init; } = new();
    
    [YamlMember(Alias = "session_timeout_minutes")]
    public int SessionTimeoutMinutes { get; set; } = 60;
    
    [YamlMember(Alias = "portals")]
    public Dictionary<string, PortalOptions> Portals { get; set; } = new();

    public bool Equals(AuthOptions? other)
    {
        if (other is null) return false;
        if (SessionSecret.Value != other.SessionSecret.Value || SessionTimeoutMinutes != other.SessionTimeoutMinutes) return false;
        if (Portals.Count != other.Portals.Count) return false;

        foreach (var kvp in Portals)
        {
            if (!other.Portals.TryGetValue(kvp.Key, out var otherPortal) || !kvp.Value.Equals(otherPortal)) return false;
        }
        
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as AuthOptions);

    public override int GetHashCode() => HashCode.Combine(SessionSecret.Value, SessionTimeoutMinutes, Portals);
}