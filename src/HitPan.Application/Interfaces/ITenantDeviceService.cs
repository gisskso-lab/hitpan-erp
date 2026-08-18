using HitPan.Application.DTOs.Device;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 테넌트 기기(Device) 관리 서비스.
/// - 히트판은 디바이스 개수 과금 — 계정 수 무제한, 총 기기 수 제한.
/// - 신규 기기 등록 시 티어별 PC/모바일 한도 체크 후 자동 승인(MVP).
/// </summary>
public interface ITenantDeviceService
{
    /// <summary>테넌트의 전체 기기 목록 (TenantAdmin용).</summary>
    Task<List<DeviceListDto>> GetAllAsync(string tenantId, CancellationToken ct = default);

    /// <summary>남은 슬롯 정보 — KPI 카드 노출용.</summary>
    Task<DeviceQuotaDto> GetQuotaAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 로그인 시 호출. 지문(fingerprint)이 있으면 last_seen_at 갱신,
    /// 없으면 신규 등록하며 티어별 한도 검사를 수행한다.
    /// - 허용되면 (true, "", deviceId, newlyRegistered), 거부되면 (false, 사유, null, false) 반환.
    /// - newlyRegistered: 이번 호출에서 처음 등록된 신규 기기면 true (작1 F3 첫 접속 안내용).
    /// </summary>
    Task<(bool allowed, string reason, string? deviceId, bool newlyRegistered)> RegisterOrRefreshAsync(
        string tenantId,
        string userId,
        RegisterDeviceRequest req,
        string ipAddress,
        CancellationToken ct = default);

    /// <summary>기기 폐기 (TenantAdmin만 호출) — status='revoked'.</summary>
    Task RevokeAsync(string deviceId, string tenantId, string userId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// 기기 승인 — 대표계정(tenant_admin)만 (20260811작1 (B)).
    /// 사장님 설계: "승인대기. 대표에게 기기승인의 권한을 주기"
    ///
    /// pending → approved 로 바꾸고 누가 언제 승인했는지 남긴다.
    /// 승인 시점에 한도를 **다시 확인**한다 — 대기 중에 다른 기기가 슬롯을 채웠을 수 있다.
    /// </summary>
    /// <summary>기기 승인 (대표계정). 반환값 = 새로 발급한 인증키 원문 (이미 승인된 기기면 null).</summary>
    /// <param name="assignUserId">
    /// 🔴 <b>대표가 승인하며 고른 "이 기기를 쓸 사람"</b> (20260818작2 · 2-5).
    /// <para>
    /// QR 로 들어온 폰은 <c>user_id</c> 가 <c>NULL</c> 이다 — <b>등록 시점엔 누구 폰인지 모르기 때문</b>이다.
    /// 대표가 승인하는 자리에서 사람을 지정한다.
    /// </para>
    /// <para>
    /// ⚠️ <c>null</c> 이면 <b>기존 값을 그대로 둔다</b>(지우지 않는다) — PC 경로처럼 이미 사람이
    /// 붙어 있는 기기의 주인을 승인 한 번으로 날리면 안 된다.
    /// ⚠️ <c>users(user_id)</c> FK 라 <b>존재하는 사용자</b>만 받는다.
    /// </para>
    /// </param>
    Task<string?> ApproveAsync(string deviceId, string tenantId, string approverUserId,
        string? assignUserId = null, CancellationToken ct = default);

    /// <summary>
    /// 직원 PC 가 입력한 인증키를 <b>자기 줄에서 대조</b>한다. 맞으면 기기 번호, 틀리면 null.
    ///
    /// <para>
    /// 🔴 20260818작1 (1-1) — <b>키는 "맞나 틀리나"만 판정한다. 무엇을 열지는 키가 정하지 않는다.</b>
    /// 종전엔 키만 보고 줄을 검색해서 <b>남의 키를 넣으면 남의 줄이 열렸다</b>(회사 공용 열쇠).
    /// 이제 <paramref name="sessionDeviceId"/> 로 줄을 먼저 특정하고 그 줄의 해시와만 대조한다.
    /// </para>
    /// <para>🔴 성공하면 해시를 소거한다 — <b>1회용</b>(사장님 결재 4). 되살리는 길은 <see cref="ReissueAuthKeyAsync"/>.</para>
    /// </summary>
    /// <param name="sessionDeviceId">이 세션이 관문에서 발급받은 장비넘버. 비면 아무것도 열지 않는다.</param>
    Task<string?> VerifyAuthKeyAsync(string authKey, string tenantId, string? sessionDeviceId, CancellationToken ct = default);

    /// <summary>
    /// 인증키 재발급 — 대표계정만 (20260818작1 (1-8) · 사장님 결재 4 <i>"1회용 + 재발급 화면 필요"</i>).
    ///
    /// <para>
    /// 🔴 <b>1-1 과 한 몸이다.</b> 키를 1회용으로 만든 이상, 오타 내거나 잃은 직원을
    /// 되살릴 길이 반드시 있어야 한다. 없으면 <b>영구 차단</b>이고 그것이 8/10 사고와 같은 모양이다.
    /// </para>
    /// <para>반환값 = 새 인증키 원문. <b>이 순간에만 존재한다</b> — 화면에만 보여주고 알림에 싣지 않는다.</para>
    /// </summary>
    Task<string?> ReissueAuthKeyAsync(string deviceId, string tenantId, string approverUserId, CancellationToken ct = default);

    /// <summary>
    /// 기기 승인 거부 — 대표계정만 (20260811작1 (B)).
    /// <b>pending → rejected</b>. 사장님이 "모르는 기기다" 라고 판단한 경우다.
    ///
    /// <para>
    /// 🔴 20260818작1 (1-4) — <b>거절은 폐기가 아니다.</b> 종전엔 <c>revoked</c> 로 보내
    /// <b>"이번엔 아니다" 와 "폐기" 가 같은 칸</b>에 있었다. 그 결과 거절당한 직원은
    /// 로그인 자체가 막혀 <b>다시 신청할 길이 없었다</b>(사장님 오더 <i>"거절하면 첫 화면 회귀"</i> 불가).
    /// 이제 <c>rejected</c> 인 기기는 <b>다시 접속하면 대기 줄에 다시 선다.</b>
    /// </para>
    /// </summary>
    Task RejectAsync(string deviceId, string tenantId, string approverUserId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// 미들웨어용 — <b>이 헤더만으로 문을 열어도 되는가.</b>
    ///
    /// <para>
    /// 🔴 20260818작1 (1-2) — <b>메인PC 한 줄로 좁혔다.</b> <c>device_id</c> 는 비밀이 아니라
    /// (화면·서버 로그·localStorage 에 평문) 승인된 아무 번호나 헤더에 넣으면 통과했다.
    /// 이 길은 인증키를 받은 적 없는 <b>메인PC 를 구하려고</b> 낸 길이므로 거기까지만 연다.
    /// </para>
    /// <para>
    /// ⚠️ 이것은 <b>"도용 차단" 이 아니다</b> — 메인PC 번호를 손에 넣은 자는 여전히 통과한다.
    /// <b>통과 가능한 범위를 좁힌 것</b>이다. G-32-d 가 그 한계를 값으로 세워 두었다.
    /// </para>
    /// <para>🔴 화면에 상태를 알려줄 때는 이것을 쓰지 마라 — <see cref="IsDeviceApprovedAsync"/> 다.</para>
    /// </summary>
    Task<bool> IsDeviceAllowedAsync(string deviceId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 관문용 — <b>이 기기가 승인은 났는가</b> (20260818작1).
    ///
    /// <para>
    /// 🔴 <see cref="IsDeviceAllowedAsync"/> 와 <b>묻는 것이 다르다. 합치면 안 된다.</b>
    /// 관문이 1-2 로 좁힌 판정을 쓰면 <b>승인받은 평범한 직원 기기가 영원히 대기 화면에 갇힌다</b> —
    /// 대표가 [예] 를 눌러도 넘어가지 않는다(8/10 사고와 같은 모양).
    /// </para>
    /// </summary>
    Task<bool> IsDeviceApprovedAsync(string deviceId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 🔴 <b>대표에게 연락할 곳</b> — 직원이 관문 앞에서 <b>누구에게 전화할지</b> 알기 위한 값 (20260818작2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// [왜 필요한가] 종전 안내는 <i>"관리자에게 문의하세요"</i> 로 끝났다.
    /// 직원은 <b>누구에게</b> 전화할지 모른다 — 갈 곳 없는 안내는 흐름이 끊긴 것이다(헌법 #20).
    /// </para>
    /// <para>
    /// 🔴 <b>고객사 안에서만 보이는 값이다</b>(헌법 #18·#22). 본사로 보내지 않는다.
    /// </para>
    /// <para>
    /// ⚠️ 못 찾으면 <c>null</c> 이다 — 이름이 없다고 화면이 죽으면 안 되고,
    /// 그때는 종전 문구(<i>"관리자에게 문의"</i>)로 그대로 간다.
    /// </para>
    /// </remarks>
    Task<AdminContactDto?> GetAdminContactAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 모바일기기 등록 QR 토큰 발급 — 대표계정만 (20260811작1 (D)).
    /// 사장님 오더: "모바일 등록기기 버튼 클릭시 QR생성"
    ///
    /// 10분 만료·1회용. 평문 토큰은 저장하지 않고 SHA-256 해시만 남긴다.
    /// 돌려주는 평문은 QR 에 담기는 그 순간에만 존재한다.
    /// </summary>
    Task<string> IssueMobileRegisterTokenAsync(string tenantId, string issuerUserId, CancellationToken ct = default);

    /// <summary>
    /// QR 토큰으로 모바일기기 등록 (20260811작1 (D)).
    ///
    /// 🔴 <b>2026-08-18 20260818작2 (2-4b) — 옛 설명을 정정한다.</b>
    /// <para>
    /// [옛 문장] <i>"QR 을 띄운 것 자체가 대표계정의 승인이므로 별도 승인 단계 없이 바로 등록된다"</i>
    /// </para>
    /// <para>
    /// [지금] <b>그 결재는 2026-08-16 에 사장님이 뒤집으셨다</b> — <i>"PC환경 절차와 같은 절차가 있어야 함."</i>
    /// QR 로 들어온 폰도 <b>대표 승인 대기줄에 선다.</b>
    /// 근거: <c>docs/운영기록/20260816작2</c> §7 결재 2 · <c>docs/운영기록/20260818작2</c> §2 (2-4).
    /// </para>
    /// <para>
    /// ⚠️ <b>이 설명을 되돌리지 마라.</b> 옛 문장이 남아 있으면 다음 사람이 그것을 근거로
    /// 봉합을 되돌린다(2-4b 가 존재하는 이유 그 자체다).
    /// </para>
    ///
    /// 반환: (성공 여부, 메시지, 기기 ID, 🔴 대표 연락처)
    /// <para>
    /// ⚠️ <c>adminContact</c> 를 여기 실어 보내는 이유 — 폰 화면은 <c>[AllowAnonymous]</c> 라
    /// 로그인 토큰이 없어 <c>admin-contact</c> 를 따로 부를 수 없다.
    /// <b>회사(tenant)는 QR 토큰에서만 나오므로</b>(헌법 #2) 그것을 아는 이 자리가 함께 돌려준다.
    /// 못 찾으면 <c>null</c> 이고, 화면은 연락처 줄 없이 안내만 띄운다.
    /// </para>
    /// </summary>
    /// <param name="knownDeviceId">
    /// 🔴 <b>이 폰이 지난번에 받아 보관해 둔 자기 번호</b> (20260818작2 · 2-3).
    /// 값이 있으면 <b>지문보다 먼저</b> 이것으로 기존 줄을 찾는다 — PC 경로와 <b>같은 순서</b>다.
    /// ⚠️ 처음 오는 폰·옛 폰은 <c>null</c> 이며, 그때는 종전대로 지문으로 찾는다(헌법 #37 호환 보존).
    /// </param>
    Task<(bool ok, string message, string? deviceId, AdminContactDto? adminContact)> RegisterMobileByTokenAsync(
        string token,
        string deviceName,
        string fingerprint,
        string ipAddress,
        string? userAgent,
        string? knownDeviceId = null,
        CancellationToken ct = default);
}
