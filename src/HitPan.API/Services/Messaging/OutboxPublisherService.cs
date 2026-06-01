using System.Data;
using System.Text.RegularExpressions;
using Dapper;

namespace HitPan.API.Services.Messaging;

/// <summary>
/// 백오피스 측 Outbox 퍼블리셔 구현 (WS-20260601-20).
///
/// 헌법 정합
///   - #3 INSERT ONLY: messaging_outbox 는 INSERT 만. UPDATE 는 폴러의 processed_at 갱신(메타)만 허용.
///   - #15 빈 catch 금지: 평문 검출 시 InvalidOperationException 강제 발생 (silent swallow 금지).
///   - #16 MySqlConnection thread-safe 위반 금지: 호출자 트랜잭션 그대로 사용.
///   - #18·#22 본사 업무데이터 0 + 데이터 최소주의: payload 평문 사업자 정보 박제 시 즉시 차단.
///   - 8명제 #3 백오피스 평문 0.
/// </summary>
public sealed class OutboxPublisherService : IOutboxPublisher
{
    private readonly ILogger<OutboxPublisherService> _logger;

    // 평문 사업자 정보 차단 가드 (사전 검출, 빈 catch 금지 헌법 #15 정합).
    //   - 사업자번호 패턴: 3-2-5 또는 10자리 숫자
    //   - 평문 JSON 키: business_number / company_name / representative_name / address / phone / email
    private static readonly Regex BizNumberRegex =
        new(@"\b\d{3}-?\d{2}-?\d{5}\b", RegexOptions.Compiled);

    private static readonly string[] ForbiddenJsonKeys = new[]
    {
        "\"business_number\"",
        "\"company_name\"",
        "\"representative_name\"",
        "\"address\"",
        "\"phone\"",
        "\"mobile\"",
        "\"email\"",
        "\"resident_number\"",
    };

    public OutboxPublisherService(ILogger<OutboxPublisherService> logger)
    {
        _logger = logger;
    }

    public async Task<long> PublishAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string eventType,
        string targetSerial,
        string payloadJson,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("eventType 필수", nameof(eventType));
        }
        if (string.IsNullOrWhiteSpace(targetSerial))
        {
            throw new ArgumentException("targetSerial 필수 (HP-/HR-YYMM-XXXXXXXX-CRC)", nameof(targetSerial));
        }
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("payloadJson 필수", nameof(payloadJson));
        }

        // 평문 가드 — 8명제 #3 + 헌법 #18·#22.
        // payload 에 평문 사업자 정보가 박제되면 머지 전 즉시 차단.
        GuardAgainstPlaintext(payloadJson, eventType, targetSerial);

        const string sql = @"
INSERT INTO messaging_outbox (event_type, target_serial, payload, occurred_at, retry_count)
VALUES (@EventType, @TargetSerial, @Payload, NOW(6), 0);
SELECT LAST_INSERT_ID();";

        var command = new CommandDefinition(
            sql,
            new
            {
                EventType = eventType,
                TargetSerial = targetSerial,
                Payload = payloadJson,
            },
            transaction: transaction,
            cancellationToken: ct);

        var outboxId = await connection.ExecuteScalarAsync<long>(command);

        _logger.LogInformation(
            "Outbox PUBLISH: outbox_id={OutboxId} event={EventType} serial={Serial}",
            outboxId, eventType, targetSerial);

        return outboxId;
    }

    private void GuardAgainstPlaintext(string payloadJson, string eventType, string targetSerial)
    {
        foreach (var key in ForbiddenJsonKeys)
        {
            if (payloadJson.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Outbox 평문 키 검출 BLOCK: event={EventType} serial={Serial} key={Key}",
                    eventType, targetSerial, key);
                throw new InvalidOperationException(
                    $"Outbox payload 평문 키 박제 금지 (8명제 #3·헌법 #18·#22): {key}");
            }
        }

        if (BizNumberRegex.IsMatch(payloadJson))
        {
            _logger.LogError(
                "Outbox 평문 사업자번호 검출 BLOCK: event={EventType} serial={Serial}",
                eventType, targetSerial);
            throw new InvalidOperationException(
                "Outbox payload 평문 사업자번호 박제 금지 (8명제 #3·헌법 #18·#22)");
        }
    }
}
