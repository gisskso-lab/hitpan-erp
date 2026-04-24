namespace HitPan.Contracts.Idempotency;

// 멱등 처리 표준 상수 (DESIGN_PRINCIPLES §5.3)
public static class IdempotencyConstants
{
    // 표준 헤더명
    public const string HeaderName = "Idempotency-Key";

    // TTL — 24시간 (사장님 결재 사항 §5.3)
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    // 정리 주기
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    // 키 길이 제약 (UUID v4 권장이지만 클라이언트 자유)
    public const int MinKeyLength = 8;
    public const int MaxKeyLength = 64;
}
