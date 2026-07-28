namespace HitPan.Contracts.Provisioning;

// prov 서버 — 토큰 갱신 (작업지시서 20260425작3 §2.3)
//   호출 흐름:
//     - AccessToken 만료(24h) 5분 전 클라이언트가 RefreshToken으로 갱신 요청
//     - prov 서버가 RefreshToken 유효성 + HW ID 일치 검증
//     - 새 AccessToken 발급 (RefreshToken은 30d 슬라이딩)
//
// Idempotency-Key 헤더 적용 (DESIGN_PRINCIPLES §5.3) — 네트워크 재시도로 토큰 발급 중복 방지
public sealed record IssueTokenRequest(
    string HardwareIdSha256,
    string RefreshToken);

public sealed record IssueTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessExpiresAt);

public sealed record IssueTokenError(
    string Code,    // refresh_token_invalid | refresh_token_expired | hardware_mismatch
    string Message);

// prov 서버 헬스체크 — 클라이언트가 부팅 시 1회 호출
public sealed record ProvHealthResponse(
    string Status,             // "ok" | "degraded" | "down"
    string Version,            // prov 서버 버전
    DateTime ServerTimeUtc);
