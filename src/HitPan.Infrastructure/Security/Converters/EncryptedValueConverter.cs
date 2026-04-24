using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HitPan.Infrastructure.Security.Converters;

// AES 암호화 변환자 — 2종 (사장님 헌법 #19 warnings 0 준수).
// EF Core 값 변환자는 nullable/non-nullable 속성 시그니처가 정확히 일치해야 CS8620 경고가 안 뜬다.
// → 비-null 속성용 (BizNo 등): EncryptedValueConverter
// → null 허용 속성용 (Tel·Email 등):  NullableEncryptedValueConverter
//
// 둘 다 같은 IEncryptionService 사용. 등록도 함께 (Program.cs / Infrastructure DI).

public sealed class EncryptedValueConverter : ValueConverter<string, string>
{
    public EncryptedValueConverter(IEncryptionService encryptionService)
        : base(
            value => encryptionService.Encrypt(value),
            value => encryptionService.Decrypt(value))
    {
    }
}

public sealed class NullableEncryptedValueConverter : ValueConverter<string?, string?>
{
    public NullableEncryptedValueConverter(IEncryptionService encryptionService)
        : base(
            value => value == null ? null : encryptionService.Encrypt(value),
            value => value == null ? null : encryptionService.Decrypt(value))
    {
    }
}
