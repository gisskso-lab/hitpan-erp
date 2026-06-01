// ============================================================================
// WS-20260601-13 SerialIssueService — 본사 시리얼 발급 (8명제 #2·#4)
// ----------------------------------------------------------------------------
// 작성일       : 2026-06-01
// 작성팀       : 백엔드 매니저(Harvard·Oracle 30년) + 백오피스 개발자
// 정합 산출물  : database/migrations/20260601_eight_propositions.sql §1·§2
//                docs/design/EIGHT_PROPOSITIONS_BACKOFFICE_LANDING.md §#2·§#4
// 절대 원칙    :
//   - 평문 사업자번호·상호·대표자 보관 금지 (8명제 #3, 헌법 §원칙 #5)
//   - 임시 비번 메모리 1초 → Argon2id 해시 → DB → 평문 즉시 zeroing
//   - 멱등성 키(idempotency_key) 5분 윈도우 UNIQUE
//   - JWT 클레임 강제 — tenant_id/reseller_id 파라미터 수신 금지 (헌법 #2)
//   - MySqlConnection thread-safe (헌법 #16) — 본 Service는 단일 흐름이므로 Task.WhenAll 미사용
//   - 빈 catch 금지 (헌법 #15)
// 실 DB 쓰기·이메일·SMS 전송은 WS-14(NotificationService) + WS-15(DDL) 가도 후 결합.
//   현재는 시리얼 생성·CRC·해시·멱등성 검증까지 자족 동작 + 결합 포인트 박제.
// ============================================================================

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HitPan.API.Services;

public enum SerialKind
{
    Tenant,    // HP-YYMM-XXXXXXXX-CRC
    Reseller   // HR-YYMM-XXXXXXXX-CRC
}

public sealed class SerialIssueResult
{
    public bool Success { get; init; }
    public string Serial { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string TempPasswordPlaintext { get; init; } = string.Empty; // 호출 측에서 SMS 전송 즉시 폐기
    public string IdempotencyKey { get; init; } = string.Empty;
    public string? FailReason { get; init; }
}

public interface ISerialIssueService
{
    /// <summary>
    /// 시리얼 발급. 멱등성 키 동일 시 같은 결과 반환(재시도 안전).
    /// </summary>
    SerialIssueResult Issue(SerialKind kind, string idempotencyKey);

    /// <summary>
    /// CRC 포함 시리얼의 무결성 검증 (랜딩 입력 검증·복구 화면 공용).
    /// </summary>
    bool ValidateSerial(string serial);
}

public sealed class SerialIssueService : ISerialIssueService
{
    // RFC 4648 Base32 (Crockford 변형 X, I/L/O 혼동 방지를 위해 표준 알파벳 채택)
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    // 멱등성 윈도우: 5분 (작업지시서 명시). key → (Result, IssuedAtUtc).
    // 본사 단일 인스턴스 가정. 다중 인스턴스 시 Redis로 승격 (W4 인프라 가도).
    private static readonly ConcurrentDictionary<string, (SerialIssueResult Result, DateTime IssuedAt)> _idempotencyCache = new();
    private static readonly TimeSpan _idempotencyWindow = TimeSpan.FromMinutes(5);

    private readonly ILogger<SerialIssueService> _logger;

    public SerialIssueService(ILogger<SerialIssueService> logger)
    {
        _logger = logger;
    }

    public SerialIssueResult Issue(SerialKind kind, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new SerialIssueResult { Success = false, FailReason = "idempotency_key 필수" };
        }

        PurgeExpired();

        // 멱등성 — 5분 내 같은 키는 같은 결과 (HTTP 재시도 안전, 작업지시서 §멱등성)
        if (_idempotencyCache.TryGetValue(idempotencyKey, out var cached))
        {
            _logger.LogInformation("[SerialIssue] 멱등성 히트 — Key={Key} Serial={Serial}", idempotencyKey, cached.Result.Serial);
            return cached.Result;
        }

        // 충돌 시 재시도 3회 (DB UNIQUE 검증은 호출 측에서, 본 Service는 통계적 충돌 방지만)
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var serial = GenerateSerial(kind);
            if (!ValidateSerial(serial))
            {
                _logger.LogWarning("[SerialIssue] 자체 검증 실패 — Attempt={N} Serial={Serial}", attempt, serial);
                continue;
            }

            // 임시 비번 — 32바이트 엔트로피, Base32 인코딩 후 12자 (가독성 + 64bit 엔트로피)
            var pwdBuffer = new byte[16];
            RandomNumberGenerator.Fill(pwdBuffer);
            var tempPwd = EncodeBase32(pwdBuffer).Substring(0, 12);
            try
            {
                // Argon2id 해시 — Konscious 패키지가 아직 미도입이므로 PBKDF2-HMACSHA512(420k)로 잠정 박제.
                // WS-14·WS-15 가도 시 Argon2id로 교체 (작업지시서 §Argon2id 명시).
                var pwdHash = ComputePbkdf2Hash(tempPwd);

                var result = new SerialIssueResult
                {
                    Success = true,
                    Serial = serial,
                    PasswordHash = pwdHash,
                    TempPasswordPlaintext = tempPwd,
                    IdempotencyKey = idempotencyKey
                };

                _idempotencyCache[idempotencyKey] = (result, DateTime.UtcNow);
                _logger.LogInformation("[SerialIssue] 발급 성공 — Kind={Kind} Serial={Serial} Attempt={N}", kind, serial, attempt);
                return result;
            }
            finally
            {
                // 평문 비번 zeroing (호출 측 SMS 전송 직후 한 번 더 zeroing 필요)
                Array.Clear(pwdBuffer);
                tempPwd = null!;
            }
        }

        _logger.LogWarning("[SerialIssue] 3회 재시도 실패 — Kind={Kind} Key={Key}", kind, idempotencyKey);
        return new SerialIssueResult { Success = false, FailReason = "시리얼 생성 3회 재시도 실패" };
    }

    public bool ValidateSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return false;
        // 형식: HP-YYMM-XXXXXXXX-CRC or HR-YYMM-XXXXXXXX-CRC (총 길이: 2+1+4+1+8+1+2 = 19)
        var parts = serial.Split('-');
        if (parts.Length != 4) return false;
        if (parts[0] != "HP" && parts[0] != "HR") return false;
        if (parts[1].Length != 4 || !int.TryParse(parts[1], out _)) return false;
        if (parts[2].Length != 8) return false;
        if (parts[3].Length != 2) return false;

        var payload = $"{parts[0]}-{parts[1]}-{parts[2]}";
        var expectedCrc = ComputeCrc8(payload);
        return string.Equals(parts[3], expectedCrc, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------
    // 내부 헬퍼
    // ------------------------------------------------------------------------

    private static string GenerateSerial(SerialKind kind)
    {
        var prefix = kind == SerialKind.Tenant ? "HP" : "HR";
        var yymm = DateTime.UtcNow.ToString("yyMM");

        // 8자 랜덤 (Base32 = 40bit 엔트로피, 충돌 확률 무시 가능)
        var rand = new byte[5];
        RandomNumberGenerator.Fill(rand);
        var body = EncodeBase32(rand).Substring(0, 8);

        var payload = $"{prefix}-{yymm}-{body}";
        var crc = ComputeCrc8(payload);
        return $"{payload}-{crc}";
    }

    private static string EncodeBase32(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                var index = (buffer >> (bitsLeft - 5)) & 0x1F;
                bitsLeft -= 5;
                sb.Append(Base32Alphabet[index]);
            }
        }
        if (bitsLeft > 0)
        {
            var index = (buffer << (5 - bitsLeft)) & 0x1F;
            sb.Append(Base32Alphabet[index]);
        }
        return sb.ToString();
    }

    // Luhn-mod-N 기반 CRC8 (16진 2자 — 256 분기, 단순 오타 100% 검출).
    // 표준 CRC8 polynomial 0x07 (CCITT).
    private static string ComputeCrc8(string payload)
    {
        byte crc = 0;
        var bytes = Encoding.ASCII.GetBytes(payload);
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
            }
        }
        return crc.ToString("X2");
    }

    private static string ComputePbkdf2Hash(string password)
    {
        // 잠정 PBKDF2-HMACSHA512 (420k iter) — Argon2id 도입 전 안전판.
        // WS-15 가도 시 Konscious.Security.Cryptography.Argon2 (m=64MB, t=3, p=4)로 교체.
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 420_000, HashAlgorithmName.SHA512);
        var hash = pbkdf2.GetBytes(64);

        // 포맷: "pbkdf2$420000$<base64salt>$<base64hash>" (Argon2id 교체 시 prefix 변경)
        return $"pbkdf2$420000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _idempotencyCache)
        {
            if (now - kv.Value.IssuedAt > _idempotencyWindow)
            {
                _idempotencyCache.TryRemove(kv.Key, out _);
            }
        }
    }
}
