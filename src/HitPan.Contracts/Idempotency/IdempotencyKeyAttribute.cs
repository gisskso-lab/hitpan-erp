namespace HitPan.Contracts.Idempotency;

// 컨트롤러/액션에 붙여 멱등 처리를 의무화한다.
//   [IdempotencyKey] 가 붙은 액션:
//     - 헤더 누락 시 400 BadRequest
//     - 헤더 + 캐시 적중 시 캐시된 응답 반환 (실제 핸들러 미호출)
//     - 헤더 + 캐시 미스 시 정상 처리 후 응답 캐싱
//   SkipCacheBody = true 일 때:
//     - 응답 본문에 민감 정보(JWT, 비번 등) 포함되어 캐싱이 부적절한 경우
//     - 헤더 검증·중복 차단은 동작, 응답 본문은 캐시하지 않음 (재요청 시 재실행)
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IdempotencyKeyAttribute : Attribute
{
    public bool SkipCacheBody { get; init; }

    // 액션별 TTL 오버라이드. null = 기본 24h 사용.
    public int? OverrideTtlSeconds { get; init; }
}
