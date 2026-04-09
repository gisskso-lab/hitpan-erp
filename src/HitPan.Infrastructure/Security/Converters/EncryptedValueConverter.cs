using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HitPan.Infrastructure.Security.Converters;

public sealed class EncryptedValueConverter : ValueConverter<string?, string?>
{
    public EncryptedValueConverter(IEncryptionService encryptionService)
        : base(
            value => value == null ? null : encryptionService.Encrypt(value),
            value => value == null ? null : encryptionService.Decrypt(value))
    {
    }
}
