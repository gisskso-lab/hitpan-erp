using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 <b>PDF 생성은 프로세스 안에서 한 번에 하나</b> — W-G1-51 갈래 「PDF 동시 생성 잠금」(2026-11-20 · 대표 결재 §99).
/// </summary>
/// <remarks>
/// <para><b>[결함]</b> 한 프로세스에서 QuestPDF 2026.2.4 <c>GeneratePdf()</c> 가 겹치면 일부 PDF 의 글꼴별
/// ToUnicode 가 U+0000 으로 채워진다. 종이 모양은 그대로(화소 0 차이)인데 <b>글자 복사 · 검색 · 받는 쪽 추출 ·
/// 낭독기</b>가 깨진다. 직원 둘이 동시에 인쇄 · 메일 · 팩스를 누르면 난다(이 PC 무잠금 병렬 18~45%).
/// 근거 = <c>docs/검증/선행/20261120_선행검증서_WG1-51_PDF동시생성잠금_1V.md</c> ·
/// <c>docs/검증/20261120_WG1-51_갈래59_간헐빨강_원인가르기_PDF동시생성_검증팀장.md</c>.</para>
/// <para><b>[잠금]</b> <c>static</c> 객체 1개를 <see cref="PdfRenderService"/> · <see cref="PdfExportService"/> 가
/// 같이 쓴다 — 두 서비스 모두 <c>AddScoped</c>(요청마다 새 인스턴스)라 인스턴스 필드 잠금은 효과가 없다.
/// 범위는 <c>GeneratePdf()</c> 호출 한 줄뿐이다. QuestPDF 는 <c>Document.Create</c> 람다(레이아웃 · 글꼴 부분집합)를
/// <c>GeneratePdf()</c> <b>안에서</b> 돌리므로 구성도 잠금 안이다. DB 조회 · 그림 미리 열기는 잠금 밖이다.</para>
/// <para>🔴 <b>이 안에 <c>await</c> 를 넣지 말 것.</b> <c>lock</c> 을 고른 까닭이 그것이다 — 컴파일러가 <c>lock</c> 안
/// <c>await</c> 를 막는다. 같은 스레드 재진입은 허용되므로 혹시 모를 중첩에도 교착이 없다
/// (<c>SemaphoreSlim.Wait</c> 는 재진입 시 교착 · <c>await</c> 를 못 막음 → 안 씀).</para>
/// <para>🔴 <b>제품 · 시험 코드는 날 <c>GeneratePdf()</c> 대신 <see cref="PdfGenerationGate.GeneratePdfOneAtATime"/> 를 부른다.</b>
/// 회귀 게이트 <c>PdfConcurrentGenerationGateTests</c>(G4)가 통로 밖 날 호출을 센다.
/// 처리량은 무잠금보다 약 2.5배 낮아진다(1V K4: 60장 527ms → 1292ms) — 글자층이 깨진 PDF 를 내보내는 것보다
/// 이 대기를 받아들인다(지시서 §6 C6). 대기 타임아웃은 두지 않는다(§13 ⑥).
/// QuestPDF 2026.7.2 이상 판올림(공식 고침)은 별건이다 — 판올림 뒤에도 이 통로와 게이트는 남긴다.</para>
/// </remarks>
public static class PdfGenerationGate
{
    private static readonly object OneAtATime = new();

    /// <summary>
    /// <paramref name="document"/> 를 PDF 바이트로 만든다 — 프로세스 안의 다른 PDF 생성이 끝날 때까지 기다린 뒤 혼자 돈다.
    /// </summary>
    public static byte[] GeneratePdfOneAtATime(this IDocument document)
    {
        lock (OneAtATime)
        {
            return document.GeneratePdf();
        }
    }
}
