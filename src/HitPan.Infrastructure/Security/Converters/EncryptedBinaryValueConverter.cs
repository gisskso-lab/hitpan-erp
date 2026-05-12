using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HitPan.Infrastructure.Security.Converters;

// VARBINARY 컬럼용 AES 변환자 — 형사영역 6개 컬럼 + raw_response_encrypted 대상.
// 기존 EncryptedValueConverter는 string ↔ Base64 string (VARCHAR 저장).
// 본 컨버터는 string ↔ byte[] (VARBINARY 저장) — DDL 정책 (CRIMINAL_DOMAIN_POLICY.md) 부합.
//
// 적용 대상 (W2 D2 ALTER 적용분):
//   employees.resident_no_encrypted     VARBINARY(255)
//   employees.salary_encrypted          VARBINARY(255)
//   employees.salary_extra_encrypted    VARBINARY(500)
//   partners.ceo_resident_no_encrypted  VARBINARY(255)
//   etax_send_history.raw_response_encrypted VARBINARY(4096)

public sealed class EncryptedBinaryValueConverter : ValueConverter<string, byte[]>
{
    public EncryptedBinaryValueConverter(IEncryptionService encryptionService)
        : base(
            value => encryptionService.EncryptBytes(System.Text.Encoding.UTF8.GetBytes(value)),
            value => System.Text.Encoding.UTF8.GetString(encryptionService.DecryptBytes(value)))
    {
    }
}

public sealed class NullableEncryptedBinaryValueConverter : ValueConverter<string?, byte[]?>
{
    public NullableEncryptedBinaryValueConverter(IEncryptionService encryptionService)
        : base(
            value => value == null ? null : encryptionService.EncryptBytes(System.Text.Encoding.UTF8.GetBytes(value)),
            value => value == null ? null : System.Text.Encoding.UTF8.GetString(encryptionService.DecryptBytes(value)))
    {
    }
}
