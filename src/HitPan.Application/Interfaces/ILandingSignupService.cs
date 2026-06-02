using HitPan.Application.DTOs.Landing;

namespace HitPan.Application.Interfaces;

// 랜딩 가입 처리 서비스 (사장님 결재 2026-06-01)
//
// 8명제 정합:
//  #1 — 랜딩 zero-DB passthrough: 사등은 ERP로 자동등록, 백오피스는 메타만 박제
//  #2 — 본사 계정 발급 (고객사가 만드는 게 아님)
//  #3 — 평문 사업자정보는 본사 ERP 한 곳에서만, 백오피스는 시리얼·해시만
//
// 헌법 정합:
//  #18·#22 — 본사 메타 최소주의 (biz_no_hash CHAR(64) BINARY UNIQUE, 평문 0)
//  #25 — 쉽게·정확하게·안전하게
//  #32 — 받아쓰기 금지: 실 구현 시 정직 로그
public interface ILandingSignupService
{
    Task<SignupResponse> SubmitAsync(SignupRequest request, CancellationToken ct = default);

    // 사장님 결재 박제 2026-06-02 (의중 B 모두결재) — 결제 박제 완료 시 tenants.status='pending' → 'active'
    // 베타: PaymentPage HandlePay 박제 후 호출 / 정식: 토스 webhook (TossWebhookController)에서 호출
    Task<SignupResponse> ConfirmPaymentAsync(string signupToken, CancellationToken ct = default);
}
