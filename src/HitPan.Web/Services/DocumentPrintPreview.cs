using HitPan.Web.Components.Common;
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
/// </remarks>
public sealed class DocumentPrintPreview
{
    private readonly IDialogService _dialog;
    private readonly ISnackbar _snackbar;

    public DocumentPrintPreview(IDialogService dialog, ISnackbar snackbar)
    {
        _dialog = dialog;
        _snackbar = snackbar;
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
}
