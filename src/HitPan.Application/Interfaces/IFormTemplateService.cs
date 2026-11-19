using HitPan.Application.DTOs.Settings;
// ⬛ 옛 줄(주석 보존 · #1 · W-G1-51 갈래 F-1 편집창 고침 1 [4] P3-3 — 결과 형식이 이 층으로 와서 쓰지 않음): using HitPan.Application.Services;

namespace HitPan.Application.Interfaces;

public interface IFormTemplateService
{
    Task<IReadOnlyList<FormTemplateDto>> ListAsync(string tenantId, string? formType = null, bool activeOnly = true, CancellationToken ct = default);
    Task<FormTemplateDto?> GetDefaultAsync(string tenantId, string formType, CancellationToken ct = default);
    Task<FormTemplateDto> CreateAsync(string tenantId, CreateFormTemplateRequest request, CancellationToken ct = default);
    Task<FormTemplateDto> UpdateAsync(string tenantId, string templateId, UpdateFormTemplateRequest request, CancellationToken ct = default);
    Task DeactivateAsync(string tenantId, string templateId, CancellationToken ct = default);

    // 테넌트 신규 생성 시 6대 양식 기본 템플릿 시드 (plain 모드 6건)
    Task SeedDefaultsAsync(string tenantId, CancellationToken ct = default);

    // 🔴 [2026-11-20 W-G1-51 갈래 F-1 · 편집창 부분] 양식지 저장 뒤 확인창 「예」 — Y 템플릿 만들기(설계 §7-3 「한 함수」).
    //   구현체 = FormTemplateService 1개(#12 grep · 시험 쪽 가짜 구현 0). tenantId 는 JWT 클레임에서만(#2).
    // ⬛ 옛 줄(주석 보존 · #1): Task<FormTemplateService.YTemplateResult> CreateYTemplateAsync(string tenantId, string templateId, CancellationToken ct = default);
    // 🔴 [W-G1-51 갈래 F-1 · 편집창 고침 1 · [4] P3-3 · PM 판단] 결과 형식을 구현 클래스 안에서 이 층(아래)으로 옮김 — 이름 · 값 무변.
    Task<YTemplateResult> CreateYTemplateAsync(string tenantId, string templateId, CancellationToken ct = default);
}

// ── 🔴 [2026-11-20 W-G1-51 갈래 F-1 · 편집창 고침 1 · [4] P3-3 · PM 판단] Y 템플릿 만들기 결과 형식 ─────────
//   종전에는 구현 클래스(FormTemplateService) 안 중첩 형식이었다 → 인터페이스가 구현 층을 거꾸로 참조했다.
//   결과 기록(YTemplateResult)과 그 칸의 열거(YTemplateOutcome)를 함께 옮긴다 — 기록만 옮기면 칸 형식이 여전히 구현 층을 가리킨다.
//   이름 · 열거 값 · 순서 · 설명 글자는 그대로(화면은 열거 이름 글자를 읽는다 — 이름 무변이 조건).
//   부르는 곳(#12 grep): FormTemplateService.cs · FormTemplateController.cs(`var` · `Outcome.ToString()`) — 시험 · 화면 직접 참조 0.

/// <summary>Y 템플릿 만들기 결과.</summary>
public enum YTemplateOutcome
{
    /// <summary>새 Y 를 만들었다.</summary>
    Created,
    /// <summary>같은 그림(해시) · 같은 칸 좌표의 Y 가 이미 있어 새로 만들지 않았다(재전진 멱등 · ⓘ-10 · D-12).</summary>
    Reused,
    /// <summary>원본 그림 경로가 비었거나 파일이 없다 — 복제 0 · 원본 print_background=0(설계 §7-3 멱등 줄 · ⓘ-6).</summary>
    NoPicture,
    /// <summary>원본 그림이 그 회사 그림 폴더 밖이거나 링크를 지난다 — 복제 0 · 원본 무변(결정적 실패 · CTO Q2).</summary>
    OutsideTenantRoot,
    /// <summary>그 회사에 그 양식이 없다(남의 회사 양식 id 포함 · ⓘ-8).</summary>
    NotFound,
    /// <summary>양식지가 아니거나 꺼진 양식 — 대상 아님 · 원본 무변.</summary>
    NotTarget,
    /// <summary>
    /// 원본(또는 이어 쓸 후보 Y) 그림 파일을 지금 읽지 못했다 — 접근 거부 · 잠김 · 경로가 폴더 등 「없음」 이 아닌 읽기 실패.
    /// 그 행 무변(그림 찍기 켬 유지) · 다음 기동에 다시(고침 1 · [3-V] P2-1 · PM 판단).
    /// </summary>
    PictureUnreadable,
}

/// <summary>Y 템플릿 만들기 결과 — 만든(또는 이어 쓴) Y 의 id 는 Created · Reused 일 때만.</summary>
public sealed record YTemplateResult(YTemplateOutcome Outcome, string? YTemplateId);
