namespace HitPan.Application.Common;

/// <summary>
/// 🔴 2026-10-05 슬롯 폐기(작4) — <b>기기 승인제는 걷었다.</b> 이 값 하나가 그 사실이다.
/// </summary>
/// <remarks>
/// <para>
/// [왜 이 파일이 생겼나] 승인제는 <c>appsettings.json</c> 의 <c>DeviceApproval:Enabled</c> 로 켜고 껐다.
/// 사장님이 승인을 <b>걷어냄</b>으로 확정하셨는데(9/28 6번 지시 · 10/5 「슬롯제한 기능을 폐기」),
/// 그 파일은 고칠 수 없다(헌법 #21 — WASM 부트 필수). ⇒ <b>코드가 그 값을 읽지 않는다</b>(9/28 설계 §3 (가)).
/// </para>
/// <para>
/// [누가 보나] 두 자리가 <b>이 값 하나</b>를 본다 — 한쪽만 고치면 앞뒤가 안 맞는다(9/28 §2 🔴 「쪼개면 사람이 갇힌다」).
/// ① <c>TenantDeviceService</c> 생성자(새 기기 줄 상태) · ④ <c>DeviceAuthMiddleware</c> 생성자(업무 API 통행).
/// </para>
/// <para>
/// 🔴 <c>const</c> 금지 — <c>const bool false</c> 는 그 뒤 갈래를 닿지 않는 코드(CS0162)로 만든다(#19 경고 0).
/// <c>static readonly</c> 라 컴파일러는 값을 모른다 — 갈래는 남고 경고는 없다.
/// </para>
/// <para>
/// [되살리기] 이전 판 재게시. 이 값을 <c>true</c> 로 바꾸는 것은 <b>사장님 결재 사항</b>이다
/// (승인 화면·관문 렌더도 이번 판에서 걷었으므로 값만 바꾸면 직원이 갇힌다).
/// </para>
/// </remarks>
public static class DeviceApprovalRetirement
{
    /// <summary>
    /// 기기 승인제 사용 여부 — <b>늘 false</b>(2026-10-05 슬롯 폐기 · 작4 A-1).
    /// <c>DeviceApproval:Enabled</c> 설정은 파일에 남지만 <b>아무도 읽지 않는다.</b>
    /// </summary>
    public static readonly bool ApprovalEnabled = false;
}
