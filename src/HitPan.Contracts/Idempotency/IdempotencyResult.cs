namespace HitPan.Contracts.Idempotency;

// 멱등 처리 결과 (미들웨어 → 액션 흐름 추적용)
public sealed record IdempotencyResult(
    int StatusCode,
    string ResponseBody,
    bool FromCache,
    DateTime ExpiresAt);

// 멱등 키 충돌 시 사용자 응답 표준 본문
//   같은 키 + 다른 본문 → 409 Conflict
public sealed record IdempotencyConflictResponse(
    string Error,
    string Message)
{
    public static IdempotencyConflictResponse SameKeyDifferentBody() =>
        new(
            Error: "idempotency_key_conflict",
            Message: "동일한 요청 키로 다른 내용이 재시도되었습니다. 새 요청으로 다시 시도해주세요.");
}
