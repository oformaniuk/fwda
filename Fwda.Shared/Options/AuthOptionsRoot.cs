namespace Fwda.Shared.Options
{
    using YamlDotNet.Serialization;

    public class AuthOptionsRoot : IEquatable<AuthOptionsRoot>
    {
        [YamlMember(Alias = "auth")]
        public AuthOptions Auth { get; set; } = new();
        
        public bool Equals(AuthOptionsRoot? other) => other is not null && Auth.Equals(other.Auth);

        public override bool Equals(object? obj) => Equals(obj as AuthOptionsRoot);

        public override int GetHashCode() => Auth.GetHashCode();
    }
}