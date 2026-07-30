# Value Converter 인터페이스 명세서 — AES-256 형사영역 + raw 데이터

> **작성:** 2026-05-12 야간 / 보안매니저 + 백엔드매니저
> **헌법:** #5 암호화, #18 본사 송신 0, #22 데이터 최소주의
> **선행 결재:** 사장님 결재 #4 (형사영역 정책) 2026-05-12 완료
> **참조:** CRIMINAL_DOMAIN_POLICY.md, ETAX_SEND_HISTORY_DDL.md

---

## 1. 적용 대상 컬럼 (총 8개)

### 1.1 형사영역 6개 (CRIMINAL_DOMAIN_POLICY.md)

| 테이블 | 컬럼 | 타입 | 처리 근거 |
|---|---|---|---|
| employees | resident_no_encrypted | VARBINARY(255) | 소득세법 §127·§164 + 4대보험법 |
| employees | salary_encrypted | VARBINARY(255) | 근로기준법 §48 + 개인정보보호법 §29 |
| employees | salary_extra_encrypted | VARBINARY(500) | 개인정보보호법 §29 |
| partners | ceo_resident_no_encrypted | VARBINARY(255) | 부가가치세법 §32 + 소득세법 §127 |

### 1.2 마이그 인프라 2개

| 테이블 | 컬럼 | 타입 | 용도 |
|---|---|---|---|
| migration_errors | raw_data | JSON | 실패 레코드 원본 (사고 추적, 헌법 #15) |
| etax_send_history | raw_response_encrypted | VARBINARY(4096) | ASP 응답 원본 |

---

## 2. Value Converter 인터페이스 (EF Core)

### 2.1 공통 인터페이스

```csharp
namespace HitPan.Infrastructure.Crypto;

/// <summary>
/// AES-256 암호화 컨버터 — DB 저장 시 자동 암호화 / 조회 시 자동 복호화.
/// 헌법 #5·#18·#22 준수: 마스터키는 ERP 로컬 환경변수, 본사 송신 0.
/// </summary>
public interface IAesValueConverter
{
    byte[] Encrypt(string? plaintext);
    string? Decrypt(byte[]? ciphertext);
}

public sealed class AesValueConverter : IAesValueConverter
{
    private readonly byte[] _masterKey;
    private readonly ILogger<AesValueConverter> _logger;

    public AesValueConverter(IConfiguration config, ILogger<AesValueConverter> logger)
    {
        // 마스터키 = ERP 로컬 환경변수 (HITPAN_AES_MASTER_KEY)
        // 본사 송신 절대 금지 (헌법 #18·#22)
        var keyBase64 = config["HitPan:AesMasterKey"]
            ?? Environment.GetEnvironmentVariable("HITPAN_AES_MASTER_KEY")
            ?? throw new InvalidOperationException("AES master key missing — set HITPAN_AES_MASTER_KEY.");

        _masterKey = Convert.FromBase64String(keyBase64);
        if (_masterKey.Length != 32)
            throw new InvalidOperationException("AES master key must be 256-bit (32 bytes).");

        _logger = logger;
    }

    public byte[] Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return Array.Empty<byte>();

        using var aes = Aes.Create();
        aes.Key = _masterKey;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);  // IV 앞부분에 저장

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plaintext);
        }

        return ms.ToArray();
    }

    public string? Decrypt(byte[]? ciphertext)
    {
        if (ciphertext == null || ciphertext.Length == 0) return null;
        if (ciphertext.Length < 16)
        {
            _logger.LogWarning("Decrypt failed: ciphertext too short ({Len})", ciphertext.Length);
            return null;
        }

        try
        {
            using var aes = Aes.Create();
            aes.Key = _masterKey;

            var iv = new byte[16];
            Array.Copy(ciphertext, 0, iv, 0, 16);
            aes.IV = iv;

            using var ms = new MemoryStream(ciphertext, 16, ciphertext.Length - 16);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AES decrypt failed");
            return null;  // 헌법 #15: 빈 catch 금지 → LogWarning 후 null 반환
        }
    }
}
```

### 2.2 EF Core ValueConverter 등록

```csharp
public sealed class EncryptedStringConverter : ValueConverter<string?, byte[]>
{
    public EncryptedStringConverter(IAesValueConverter aes)
        : base(
            v => aes.Encrypt(v),
            v => aes.Decrypt(v))
    {
    }
}
```

### 2.3 OnModelCreating 매핑 (예시)

```csharp
modelBuilder.Entity<Employee>(entity =>
{
    entity.Property(e => e.ResidentNo)
        .HasColumnName("resident_no_encrypted")
        .HasConversion(new EncryptedStringConverter(_aesConverter));

    entity.Property(e => e.Salary)
        .HasColumnName("salary_encrypted")
        .HasConversion(new EncryptedStringConverter(_aesConverter));

    entity.Property(e => e.SalaryExtra)
        .HasColumnName("salary_extra_encrypted")
        .HasConversion(new EncryptedStringConverter(_aesConverter));
});

modelBuilder.Entity<Partner>(entity =>
{
    entity.Property(p => p.CeoResidentNo)
        .HasColumnName("ceo_resident_no_encrypted")
        .HasConversion(new EncryptedStringConverter(_aesConverter));
});
```

---

## 3. 마스터키 관리 (헌법 #18·#22)

### 3.1 키 저장 위치
- **로컬:** `HITPAN_AES_MASTER_KEY` 환경변수 (Windows 시스템 환경변수)
- **백업:** 사장님 USB 별도 보관 (오프라인)
- **본사 송신:** 절대 금지 (헌법 #18·#22)

### 3.2 키 생성 (1회만)
```powershell
# PowerShell - 256비트 키 생성
$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$base64 = [Convert]::ToBase64String($bytes)
Write-Output "HITPAN_AES_MASTER_KEY=$base64"
# → 출력값을 시스템 환경변수에 저장
# → USB 백업
```

### 3.3 키 분실 시 대응
- 평문 복호화 불가 → 영구 손실
- 사장님 USB 백업키 사용
- 백업도 분실 = **데이터 영구 손실** (사고 매뉴얼 필요)

---

## 4. Dapper 사용 시 (마이그 코드)

Dapper는 EF Core 컨버터 자동 적용 안 됨 → 명시적 호출 필요.

```csharp
// MdbToHitpanMapper.MapEmployeeAsync 예시
await connection.ExecuteAsync(@"
    INSERT INTO employees (
        employee_id, tenant_id, ...,
        resident_no_encrypted, salary_encrypted, salary_extra_encrypted,
        ...
    ) VALUES (
        @EmployeeId, @TenantId, ...,
        @ResidentNoEncrypted, @SalaryEncrypted, @SalaryExtraEncrypted,
        ...
    )",
    new {
        // ...
        ResidentNoEncrypted = _aes.Encrypt(GetStr(row, "SW_JUMIN")),
        SalaryEncrypted = _aes.Encrypt(GetInt(row, "SW_PAY").ToString()),
        SalaryExtraEncrypted = _aes.Encrypt(GetStr(row, "SW_PAYoth")),
    },
    transaction: tx);
```

---

## 5. raw_data·raw_response 별도 처리

### 5.1 migration_errors.raw_data (JSON)
- **저장 시:** JSON 직렬화 후 AES-256 적용
- **조회 시:** super_admin 권한 + step-up 인증
- **조회 컨트롤러는 raw_data 응답에 포함 X** (INFRA_API_SPEC.md §4)

```csharp
public void AddError(string jobId, string tenantId, object rawData, ...)
{
    var json = JsonSerializer.Serialize(rawData);
    var encrypted = _aes.Encrypt(json);

    // raw_data = byte[] (VARBINARY) — 또는 BLOB 저장 컬럼 확장 검토
    // 본 DDL은 JSON 컬럼 → JSON 텍스트 그대로 + 컬럼 자체 암호화는 별도 검토
}
```

⚠️ **명세 보완 필요:** migration_errors.raw_data 컬럼이 현재 DDL은 `JSON` 타입.
JSON 컬럼 + AES = 호환 어려움 (JSON 검색 불가).
대안: `raw_data_encrypted VARBINARY(8192)` 컬럼 추가 + JSON은 마스킹 버전만 보관.
→ **W2 D2 보안매니저 + DB매니저 추가 검토 안건.**

### 5.2 etax_send_history.raw_response_encrypted
- VARBINARY(4096) 명시 → AES-256 직접 적용 OK
- ASP 응답 원본 보존

---

## 6. 마스킹 로직 (조회 시)

### 6.1 화면 표시 기본 = 마스킹

```csharp
public static class MaskingHelper
{
    public static string MaskResidentNo(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.Length < 13) return "***";
        return $"{plain.Substring(0, 6)}-*******";
    }

    public static string MaskSalary(decimal? amount) => "●●●";

    public static string MaskPhone(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.Length < 8) return "***";
        // 010-1234-5678 → 010-****-5678
        return Regex.Replace(plain, @"(\d{3})[-]?\d{4}[-]?(\d{4})", "$1-****-$2");
    }
}
```

### 6.2 [보기] 클릭 시 step-up

- 비밀번호 재입력 또는 간편인증 (카카오/토스/금융인증/PASS)
- 5분 평문 노출 후 자동 마스킹 복귀
- 감사로그 INSERT (누가·언제·어떤 컬럼·어떤 직원)

---

## 7. 감사로그 (헌법 #18 v3)

```sql
CREATE TABLE IF NOT EXISTS sensitive_access_log (
    log_id        CHAR(36) PRIMARY KEY,
    tenant_id     CHAR(36) NOT NULL,
    user_id       CHAR(36) NOT NULL,
    action        ENUM('view','export','print') NOT NULL,
    target_table  VARCHAR(50) NOT NULL,
    target_column VARCHAR(50) NOT NULL,
    target_id     CHAR(36) NOT NULL,
    client_ip     VARCHAR(45) NULL,
    user_agent    VARCHAR(255) NULL,
    accessed_at   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_tenant_user (tenant_id, user_id, accessed_at DESC),
    INDEX idx_target (target_table, target_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

⚠️ **별도 작업지시서 필요 (작12-X).**

---

## 8. 단위 테스트 시나리오

### 8.1 암호화·복호화 라운드트립
```csharp
[Fact]
public void Encrypt_Decrypt_Roundtrip()
{
    var aes = new AesValueConverter(_config, _logger);
    var plain = "880101-1234567";
    var cipher = aes.Encrypt(plain);
    Assert.NotEqual(plain, Encoding.UTF8.GetString(cipher));  // 평문 X
    var back = aes.Decrypt(cipher);
    Assert.Equal(plain, back);
}
```

### 8.2 동일 평문 → 다른 암호문 (IV 랜덤)
```csharp
[Fact]
public void Encrypt_Same_Plain_Different_Cipher()
{
    var cipher1 = _aes.Encrypt("test");
    var cipher2 = _aes.Encrypt("test");
    Assert.NotEqual(cipher1, cipher2);  // IV 랜덤
    Assert.Equal(_aes.Decrypt(cipher1), _aes.Decrypt(cipher2));
}
```

### 8.3 빈 값·NULL 처리
```csharp
[Theory]
[InlineData(null)]
[InlineData("")]
public void Encrypt_Null_Or_Empty_Returns_Empty(string? input)
{
    var cipher = _aes.Encrypt(input);
    Assert.Empty(cipher);
}
```

### 8.4 키 분실 시 graceful 실패
```csharp
[Fact]
public void Decrypt_Invalid_Cipher_Logs_Warning_Returns_Null()
{
    var fake = new byte[] { 0x01, 0x02, 0x03 };
    var result = _aes.Decrypt(fake);
    Assert.Null(result);
    // _logger.LogWarning 호출 검증
}
```

---

## 9. 헌법 부합 매트릭스

| 헌법 | 적용 |
|---|---|
| #5 AES-256 Value Converter | ✅ |
| #15 빈 catch 금지 | ✅ LogWarning 의무 |
| #18 본사 송신 0 | ✅ 마스터키 로컬 |
| #22 데이터 최소주의 | ✅ 마스킹 기본 + step-up |
| #23 5중 검증 | ✅ 단위 테스트 + SAST |
| #24 책임 분산 | ✅ 키 분실 매뉴얼 |

---

## 10. 사장님 결재 사항

| # | 사항 | 결재 |
|---|---|---|
| 1 | AES-256 ValueConverter 구조 | ✅ |
| 2 | 마스터키 ERP 로컬 환경변수 + USB 백업 | ✅ |
| 3 | migration_errors.raw_data 컬럼 = JSON or VARBINARY 재검토 | ⚠️ W2 D2 추가 안건 |
| 4 | sensitive_access_log 신규 테이블 | ⚠️ 별도 작업지시서 |
| 5 | step-up 5분 평문 노출 정책 | ✅ |

---

**작성:** 보안매니저 + 백엔드매니저
**검토:** DB매니저, 설계팀장 브라운킴, 법무팀장
**최종 검증:** CTO 래리 앨리슨
**적용 시점:** W2 D3 코드 추출 시
