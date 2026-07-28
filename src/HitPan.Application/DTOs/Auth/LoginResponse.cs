namespace HitPan.Application.DTOs.Auth;

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>로그인 시점에 last_login_at 이 비어 있었으면 true (온보딩 라우팅용).</summary>
    public bool RedirectToWelcome { get; set; }

    /// <summary>
    /// 기기 등록 결과(있으면 클라이언트가 localStorage에 보관).
    /// fingerprint가 없거나 이미 등록된 경우 null일 수 있음.
    /// </summary>
    public string? DeviceId { get; set; }

    // ── 접속 기기 슬롯 안내 (작1 F3, 사장님 정의 정상 흐름 2026-07-02) ──
    //   사장님 정의: 접속 시 등록기기면 통과 / 처음 기기면 "등록하시겠습니까?"(슬롯 여유) 또는
    //   "등록된 기기가 아닙니다"(슬롯 초과) 안내가 떠야 정상. 현재는 조용히 자동 등록만.
    //   1차(작1)에서 상태를 응답에 실어 클라이언트가 안내를 띄울 기반을 놓는다.
    //   실제 "등록하시겠습니까?" 사용자 확인 다이얼로그(2단계 상호작용)는 2차 프론트 봉합.

    /// <summary>이번 로그인에서 이 기기가 처음 등록된 신규 기기면 true (첫 접속 안내용).</summary>
    public bool DeviceNewlyRegistered { get; set; }

    /// <summary>기기 슬롯 관련 안내 문구(있으면 클라이언트가 노출). 정상 등록/재접속이면 null.</summary>
    public string? DeviceNotice { get; set; }

    /// <summary>
    /// 헌법 #24 약관 v2.0.0 미동의 시 true → 클라이언트가 /terms 강제 이동.
    /// AuthController에서 ITermsConsentService.HasAgreedAsync 결과로 저장.
    /// </summary>
    public bool RequiresTermsConsent { get; set; }

    // ── 고리2(A안): 업데이트 동의 Y/N 팝업용 (헌법 #30 본사 의존 0, 로컬 자가완결) ──
    //   AuthController.Login 이 로컬 정보만으로 채운다(본사 안 거침). 정보 출처가 아직 없으면
    //   UpdateAvailable=false 안전 폴백(로그인은 절대 안 깨진다).

    /// <summary>현재 설치된 ERP 어셈블리 버전(예: "1.0.0"). 항상 채워진다.</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>새 버전(Major/동의 필요) 동의 팝업을 띄울지 여부. 정보 없으면 false 폴백.</summary>
    public bool UpdateAvailable { get; set; }

    /// <summary>동의 대상 새 버전(예: "2.0.0"). UpdateAvailable=true 일 때만 의미.</summary>
    public string? LatestVersion { get; set; }

    /// <summary>업데이트 채널(예: "Major"). 화면 표기/기록용.</summary>
    public string? UpdateChannel { get; set; }

    /// <summary>동의 팝업에 함께 보여줄 안내 문구(워치독 manifest 출처). 없으면 기본 문구.</summary>
    public string? ConsentMessage { get; set; }
}
