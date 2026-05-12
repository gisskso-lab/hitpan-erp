using HitPan.Infrastructure.Security;
using HitPan.Infrastructure.Security.Converters;

namespace HitPan.Tests.Security;

/// <summary>
/// W2 D4 (2026-05-12) — EncryptedBinaryValueConverter 단위 테스트.
///
/// 검증 범위:
/// - EF Core ValueConverter의 string ↔ byte[] 변환식이 라운드트립을 보장
/// - NullableEncryptedBinaryValueConverter의 null 처리
/// - 헌법 #5 (AES-256), CRIMINAL_DOMAIN_POLICY.md §3 형사영역 5개 컬럼 대상
/// </summary>
public class EncryptedBinaryValueConverterTests : IDisposable
{
    private readonly string? _originalKey;
    private readonly IEncryptionService _encryption;

    public EncryptedBinaryValueConverterTests()
    {
        _originalKey = Environment.GetEnvironmentVariable("ERP_ENCRYPTION_KEY");

        var testKey = new byte[32];
        for (int i = 0; i < 32; i++) testKey[i] = (byte)((i * 7 + 13) & 0xFF);
        Environment.SetEnvironmentVariable("ERP_ENCRYPTION_KEY", Convert.ToBase64String(testKey));

        _encryption = new EncryptionService();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ERP_ENCRYPTION_KEY", _originalKey);
    }

    [Fact(DisplayName = "EBC-01: ConvertToProviderExpression + ConvertFromProviderExpression 라운드트립")]
    public void Converter_Roundtrip_Plain_To_Bytes_To_Plain()
    {
        var converter = new EncryptedBinaryValueConverter(_encryption);
        var plain = "880101-1234567";

        // ValueConverter의 변환식을 컴파일해 실행
        var toBytes = converter.ConvertToProviderExpression.Compile();
        var fromBytes = converter.ConvertFromProviderExpression.Compile();

        var bytes = toBytes(plain);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 16);

        var back = fromBytes(bytes);
        Assert.Equal(plain, back);
    }

    [Fact(DisplayName = "EBC-02: Nullable 컨버터 — null 입력 시 null 반환")]
    public void NullableConverter_Null_Input_Returns_Null()
    {
        var converter = new NullableEncryptedBinaryValueConverter(_encryption);

        var toBytes = converter.ConvertToProviderExpression.Compile();
        var fromBytes = converter.ConvertFromProviderExpression.Compile();

        Assert.Null(toBytes(null));
        Assert.Null(fromBytes(null));
    }

    [Fact(DisplayName = "EBC-03: Nullable 컨버터 — 평문 라운드트립")]
    public void NullableConverter_Roundtrip_NonNull()
    {
        var converter = new NullableEncryptedBinaryValueConverter(_encryption);

        var toBytes = converter.ConvertToProviderExpression.Compile();
        var fromBytes = converter.ConvertFromProviderExpression.Compile();

        var plain = "5000000";
        var bytes = toBytes(plain);
        Assert.NotNull(bytes);

        var back = fromBytes(bytes);
        Assert.Equal(plain, back);
    }
}
