namespace Fwda.Shared.Encryption;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>
/// YAML converter for EncryptedString that encrypts on write and decrypts on read
/// </summary>
public class EncryptedStringConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(EncryptedString);

    public object ReadYaml(IParser parser, Type type)
    {
        var scalar = parser.Consume<Scalar>();
        var encrypted = scalar.Value;
        var decrypted = CryptoHelper.Decrypt(encrypted);
        return new EncryptedString(decrypted);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var es = (EncryptedString)value!;
        var encrypted = CryptoHelper.Encrypt(es.Value);
        emitter.Emit(new Scalar(encrypted));
    }
}