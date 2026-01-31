namespace Fwda.Shared.Encryption;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// A string that is encrypted when serialized and decrypted when deserialized
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class EncryptedString : IEquatable<EncryptedString>, IEquatable<string>
{
    public string Value { get; }

    public EncryptedString() => Value = string.Empty;

    public EncryptedString(string value) => Value = value;

    public override string ToString() => Value;

    public static implicit operator string(EncryptedString es) => es.Value;

    public static implicit operator EncryptedString(string s) => new (s);

    public bool Equals(EncryptedString? other) => other != null && Value == other.Value;
    
    public bool Equals(string? other) => other != null && Value == other;

    public override bool Equals(object? obj) => Equals(obj as EncryptedString);

    public override int GetHashCode() => Value.GetHashCode();
}