using HitPan.Web.Components.Common;
using Microsoft.JSInterop;
using MudBlazor;

namespace HitPan.Web.Services;

/// <summary>
/// 🔴 전표 「인쇄」 → <b>미리보기 창</b>을 여는 단 하나의 자리 (사장님 오더 2026-10-24 반려 ③).
/// </summary>
/// <remarks>
/// <para>
/// 사장님 워크플로우: <i>"인쇄버튼 클릭시 → 미리보기로 넘어가서, 어떤 양식이 적용되는지
///   보여줘야 함 → 옵션 선택 → 최종 인쇄시 미리보기 화면하고 동일하게 인쇄"</i>
/// </para>
/// <para>
/// 🔴 <b>왜 한 자리인가</b> — 전표는 7종이다. 종마다 창 띄우는 코드를 따로 두면
///   <b>일곱 벌</b>이 되고, 창이 바뀔 때마다 일곱 군데를 고쳐야 한다.
///   10/24 에 적발된 사고가 바로 그 모양이었다 —
///   <b>「인쇄」는 양식 무시 · 「PDF」는 양식 적용</b> 으로 길이 갈라져 있었다.
/// </para>
/// <para>
/// 🔴 <b>화면인쇄 갈래는 만들지 않는다</b>(사장님 결재 결-1:
///   <i>"no · 굳이 화면인쇄를 할거면 캡쳐해서 인쇄해도 됨"</i>).
/// </para>
/// <para>
/// 🔴 <b>「PDF」 단추도 같은 문</b>(2026-11-20 대표 §76 · PM 결재 §81) — <see cref="DownloadPdfAsync"/> 가
///   [인쇄] 미리보기와 같은 전표 인쇄 문에서 받아 파일로 저장한다. 한 전표 = 렌더러 하나.
/// </para>
/// </remarks>
public sealed class DocumentPrintPreview
{
    private readonly IDialogService _dialog;
    private readonly ISnackbar _snackbar;
    // 🔴 W-G1-51 갈래 「옛 /pdf」 — 「PDF 받기」 한 자리용. 둘 다 이미 DI 등록(로그인 HttpClient · JS) · Program.cs 0줄.
    private readonly RenderedPdfDownload _pdf;
    private readonly IJSRuntime _js;
    // 🔴 [고침 1 · 결재 기록 §86 ①] 「PDF 받기」 흐름(RenderedPdfDownload.DownloadPdfAsync)에 붙이는 화면 손잡이 —
    //   상자 · 스낵바 · JS 글자는 종전 DownloadPdfAsync 몸통 그대로(아래 「종전(#1)」 주석과 대조).
    private readonly PdfDownloadUi _ui;

    // ⬛ [낡은 줄 · 화면오류기록띠] public DocumentPrintPreview(IDialogService dialog, ISnackbar snackbar, HttpClient http, IJSRuntime js)
    //   화면오류기록띠 갈래 S — RenderedPdfDownload 의 기록기(자기 범주)를 DI 에서 받아 넘김(작업지시서 E6 · 1-인자 생성자는 시험용으로 남음)
    public DocumentPrintPreview(IDialogService dialog, ISnackbar snackbar, HttpClient http, IJSRuntime js, ILogger<RenderedPdfDownload> pdfLogger)
    {
        _dialog = dialog;
        _snackbar = snackbar;
        // ⬛ [낡은 줄 · 화면오류기록띠] _pdf = new RenderedPdfDownload(http);
        _pdf = new RenderedPdfDownload(http, pdfLogger);
        _js = js;
        _ui = new PdfDownloadUi
        {
            ConfirmImageOnlyAsync = async () => (await _dialog.ShowMessageBoxAsync(
                "아직 칸을 안 맞췄습니다",
                "이 양식으로 PDF 를 받으면 올린 양식 그림만 나오고 전표 내용은 찍히지 않습니다. 칸을 맞추려면 「양식 템플릿」 메뉴의 양식용지 양식에서 칸을 맞추고 그림을 올려 저장한 뒤 「예」를 눌러 주십시오(양식이 하나 더 생깁니다 · 관리자).",
                yesText: "그래도 받기",
                cancelText: "취소")) == true,
            Warn = m => _snackbar.Add(m, Severity.Warning),
            Info = m => _snackbar.Add(m, Severity.Info),
            Error = m => _snackbar.Add(m, Severity.Error),
            SaveAsync = (name, bytes) => _js.InvokeVoidAsync("downloadFileFromBytes",
                name, "application/pdf", Convert.ToBase64String(bytes)).AsTask(),
        };
    }

    /// <summary>
    /// 전표 인쇄 미리보기를 연다.
    /// </summary>
    /// <param name="docType">화면이 쓰는 문서 종류(하이픈 꼴) — 서버가 작성기 이름으로 바꾼다.</param>
    /// <param name="docId">전표 id. 🔴 비어 있으면 창을 열지 않는다 — 그릴 것이 없다.</param>
    /// <param name="title">창 제목. 고객이 무엇을 인쇄하는지 알아야 한다.</param>
    public async Task ShowAsync(string docType, string? docId, string title)
    {
        if (string.IsNullOrWhiteSpace(docId))
        {
            // 🔴 조용히 아무 일도 안 일어나면 고객은 고장으로 읽는다.
            _snackbar.Add("먼저 전표를 저장하거나 목록에서 불러오십시오.", Severity.Warning);
            return;
        }

        var parameters = new DialogParameters
        {
            { nameof(DocumentPrintPreviewDialog.DocType), docType },
            { nameof(DocumentPrintPreviewDialog.DocId), docId! },
        };

        // 종이를 눈으로 보는 창이다 — 넓게 띄운다(「스크롤 없이 한 화면」).
        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.Medium,
            FullWidth = true,
            CloseButton = true,
        };

        // 🔴 20261024작 R-7-a 봉합 — **창이 닫힐 때까지 기다린다.**
        //   세금계산서 목록은 여러 장을 골라 for 로 돈다. 기다리지 않으면 창이
        //   겹쳐 떠서 고객이 무엇을 보고 있는지 모른다.
        var dialog = await _dialog.ShowAsync<DocumentPrintPreviewDialog>($"{title} 인쇄 미리보기", parameters, options);
        await dialog.Result;
    }

    /// <summary>
    /// 🔴 [2026-11-20 W-G1-51 갈래 「옛 /pdf」 · 대표 §76 · PM 결재 §81] 전표 「PDF」 단추 — [인쇄] 미리보기와
    /// <b>같은 전표 인쇄 문</b>에서 PDF 를 받아 파일로 내려받는다.
    /// </summary>
    /// <param name="docType">같은 화면 [인쇄] 단추의 종과 같은 종(핵심 규칙).</param>
    /// <param name="docId">전표 id. 🔴 비어 있으면 받지 않는다 — 그릴 것이 없다.</param>
    /// <param name="title">[인쇄] 창 제목과 같은 이름 — 서버가 파일 이름을 안 주면 <c>{title}.pdf</c>.</param>
    /// <remarks>
    /// 🔴 「그림만」(§59 K3) 이면 받기 전에 확인 상자 1개(결재 ②) — 「그래도 받기」 로 받기를 막지 않는다(#20).
    ///   안내 여부는 서버 안내 문 하나가 판정한다 · 화면은 셈하지 않는다(결재 ③).
    /// 🔴 실패는 조용히 넘기지 않는다 — 서버 사람 말 또는 상태 코드를 스낵바로(#20).
    /// </remarks>
    public async Task DownloadPdfAsync(string docType, string? docId, string title)
    {
        // 🔴 [고침 1 · 결재 기록 §86 ①] 흐름 ①~④ 는 RenderedPdfDownload.DownloadPdfAsync 한 자리로 옮겼다(시험이 실제로 부른다) ·
        //   여기는 화면 손잡이(_ui)만 붙인다. 두 번 누름 잠금(§86 P3)도 그 자리에 있다.
        await _pdf.DownloadPdfAsync(docType, docId, title, _ui);

        // 종전(#1 · 고침 1 로 RenderedPdfDownload.DownloadPdfAsync 로 옮김 · 글자 그대로):
        // if (string.IsNullOrWhiteSpace(docId))
        // {
        //     // 🔴 ShowAsync 와 같은 글자 — 조용히 아무 일도 안 일어나면 고객은 고장으로 읽는다.
        //     _snackbar.Add("먼저 전표를 저장하거나 목록에서 불러오십시오.", Severity.Warning);
        //     return;
        // }
        //
        // // ② K3(빈 A4 + 그림 + 칸 0) 이면 받기 전에 알린다 — 받은 PDF 는 거래처로 갈 수 있다.
        // if (await _pdf.NeedsNoticeAsync(docType, CancellationToken.None))
        // {
        //     var go = await _dialog.ShowMessageBoxAsync(
        //         "아직 칸을 안 맞췄습니다",
        //         "이 양식으로 PDF 를 받으면 올린 양식 그림만 나오고 전표 내용은 찍히지 않습니다. 칸을 맞추려면 「양식 템플릿」 메뉴의 양식용지 양식에서 칸을 맞추고 그림을 올려 저장한 뒤 「예」를 눌러 주십시오(양식이 하나 더 생깁니다 · 관리자).",
        //         yesText: "그래도 받기",
        //         cancelText: "취소");
        //     if (go != true) return;
        // }
        //
        // // ③ 전표 인쇄 문 — 옵션 null = 저장된 양식 그대로.
        // var got = await _pdf.FetchAsync(docType, docId!, CancellationToken.None);
        // if (!got.Ok)
        // {
        //     var fallback = got.StatusCode > 0
        //         ? $"PDF 를 만들지 못했습니다. ({got.StatusCode})"
        //         : "PDF 를 만들지 못했습니다.";
        //     _snackbar.Add(got.Message ?? fallback, Severity.Error);
        //     return;
        // }
        //
        // // ④ 받은 바이트를 파일로 — 기존 JS 함수(wwwroot/js/storage.js downloadFileFromBytes) · 새 JS 0.
        // try
        // {
        //     await _js.InvokeVoidAsync("downloadFileFromBytes",
        //         got.FileName ?? $"{title}.pdf", "application/pdf", Convert.ToBase64String(got.Bytes));
        // }
        // catch (Exception ex)
        // {
        //     _snackbar.Add("PDF 를 저장하지 못했습니다.", Severity.Error);
        //     Console.Error.WriteLine($"[DocPdf] 내려받기 실패 — type={docType}: {ex.Message}");
        // }
    }
}
