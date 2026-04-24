namespace HitPan.Contracts.Provisioning;

// prov 서버 — 신규 설치 PC를 본사에 등록 (작업지시서 20260425작3 §2.3)
//   호출 흐름:
//     1) 클라이언트 ERP가 첫 부팅 시 HW ID(SHA-256) + 라이선스 키 + 회사명 전송
//     2) prov 서버가 라이선스 검증 → tenant_id 발급 → JWT 24h 토큰 발급
//     3) 클라이언트는 토큰을 .env에 저장 + Cloudflare Tunnel 토큰도 함께 받음
//
// 호환 매트릭스:
//   - 본 ERP 와 prov 서버는 다른 솔루션이지만 같은 HitPan.Contracts 참조
//   - 마커스 리 합류 후 src/HitPan.Provisioning/ 신규 프로젝트로 분리
public sealed record RegisterDeviceRequest(
    string HardwareIdSha256,   // 클라이언트가 SHA-256 해시한 HW ID (역추적 불가)
    string LicenseKey,         // 사장님이 발급한 라이선스 키 (AES-256 암호화 저장)
    string CompanyName,        // 표시용 회사명
    string ContactEmail,       // CS 연락용
    string InstallerVersion);  // 예: "1.0.7"

public sealed record RegisterDeviceResponse(
    string TenantId,           // prov가 발급한 테넌트 식별자 (본 ERP 의 tenant_id와 동기)
    string AccessToken,        // 24h JWT
    string RefreshToken,       // 30d 갱신용
    string TunnelToken,        // Cloudflare Tunnel 등록 토큰 (베타: workers.dev)
    DateTime AccessExpiresAt);

// 라이선스 검증 실패 / HW ID 중복 / 정원 초과 등 표준 에러 응답
public sealed record RegisterDeviceError(
    string Code,    // license_invalid | hardware_already_registered | quota_exceeded
    string Message);
