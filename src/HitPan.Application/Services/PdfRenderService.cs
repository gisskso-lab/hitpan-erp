using System.Data;
using System.Data.Common;
using System.Globalization;
using Dapper;
using HitPan.Application.Interfaces;
using HitPan.Application.DTOs.Documents;
using HitPan.Application.DTOs.Settings;
using HitPan.Contracts.Forms;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HitPan.Application.Services;

/// <summary>
/// 6종 문서 PDF 렌더링 서비스 (사장님 결재 2026-04-29).
/// 동일 레이아웃(회사 헤더 / 거래처 / 라인 / 합계)에 문서타입만 다르게 표시.
/// QuestPDF Community License (회사 매출 1M$ 이하 무료) — 베타 단계 적합.
/// </summary>
public sealed class PdfRenderService : IPdfRenderService
{
    private readonly IDbConnection _db;
    private readonly ILogger<PdfRenderService> _logger;
    private readonly IFormTemplateService? _formTemplateService;

    public PdfRenderService(IDbConnection db, ILogger<PdfRenderService> logger,
        IFormTemplateService? formTemplateService = null)
    {
        _db = db; _logger = logger; _formTemplateService = formTemplateService;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType, string documentId, CancellationToken ct = default)
        => RenderDocumentAsync(tenantId, documentType, documentId, null, ct);

    /// <summary>
    /// 🔴 20261024 — 미리보기 주입 통로 (사장님 결재 2026-10-21 ㉯ · 오더 2026-10-24).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>조건 1</b> — <paramref name="overrides"/> 가 null 이면 위 4인자 호출과
    ///   <b>한 글자도 다르지 않게</b> 동작한다. 기존 호출부는 안 고쳤다(#1).
    /// 🔴 <b>두 벌 금지</b> — 미리보기 전용 작성기를 따로 만들지 않았다. 같은 통로를 타고
    ///   다른 것은 <b>먹이는 자료</b>뿐이다(20261021작 §11-3).
    /// </remarks>
    public async Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
        string documentId, PdfPreviewOverrides? overrides, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 🔴 20260826작6 — 급여명세서는 거래문서와 **구조가 아예 다르다.**
        //   DocumentSnapshot 은 거래처·품목·수량·단가·부가세 축인데, 급여명세서는
        //   지급항목·공제항목·실수령액 축이다. 억지로 끼우면 모든 칸이 뜻과 어긋난다
        //   (거래처 칸에 사원명, 단가 칸에 공제액이 들어가는 식).
        //   ⇒ **전용 경로로 가른다.** 아래 거래문서 흐름은 한 줄도 건드리지 않는다(#1).
        if (string.Equals(documentType, PayslipDocType, StringComparison.Ordinal))
        {
            return await RenderPayslipAsync(tenantId, documentId, ct).ConfigureAwait(false);
        }

        // 🔴 §11-2 ② — 양식정보설정은 6단계 중 1단계인데 전표는 3~4단계다.
        //   신규 고객이 자를 대고 mm 를 맞추는 그 시점에 전표가 **0건**이라 아무것도 못 봤다.
        var data = overrides?.UseSampleData == true
            ? BuildSampleSnapshot(documentType, overrides)
            : await LoadDocumentAsync(tenantId, documentType, documentId, ct).ConfigureAwait(false);

        // 🔴 3차검증 R-11 봉합 — **DB 에 없는 전표면 여기서 끝낸다.**
        //   저장 안 한 전표의 id 는 화면이 만든 GUID 라 DB 에 없고, 종전에는
        //   빈 스냅샷이 그대로 그려져 **백지**가 떴다. 미리보기 문만 이 값을 세운다.
        if (ShouldRejectMissing(overrides, data.Found))
        {
            throw new KeyNotFoundException(
                "저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오.");
        }

        var company = await LoadCompanyAsync(tenantId, ct).ConfigureAwait(false);

        // 작지② 양식 분기 (사장님 작업지시 2026-05-31)
        // form_templates에서 paper_mode + 여백 + 토글 로드 → plain·preprint 분기
        // 🔴 사장님 §11 *"실제 출력물 양식을 편집하는 대로 보여주는 게 중요함"* —
        //   저장 전 값이 오면 DB 행 **대신** 그것으로 그린다.
        //   🔴 음성 대조군(결재 ㉯ 조건 3): 이 세 줄을 빼면 미리보기가 저장값으로 떨어지고
        //      「편집하는 대로」가 **실패해야** 한다. 조용히 통과하면 게이트가 거짓이다.
        var template = overrides?.Template is not null
            ? ToTemplateInfo(overrides.Template)
            : await TryLoadTemplateAsync(tenantId, documentType, ct).ConfigureAwait(false);

        // 🔴 전표 인쇄 미리보기 옵션 (사장님 결재 2026-10-24 결-2 · 7종 공통).
        //   저장된 양식을 **그대로 쓰고** 그 위에 **이번 한 번만** 바꿀 값을 얹는다.
        //   🔴 저장하지 않는다 — 고객이 미리보기에서 만진 값이 양식으로 굳으면 안 된다.
        //   🔴 옵션이 null 이면 아무것도 안 바뀐다 ⇒ **지금 인쇄와 같은 종이**(#1).
        // 🔴🔴 [2026-11-19 W-G1-51 갈래 A 고침1 · 대표 §49 · 구별 기준 §1-3 C 「저장값 조건」]
        //   Y 템플릿 판정(아래 `isYTemplate`)은 **저장된** `print_background` 로 본다 — 바로 아래
        //   `ApplyPrintOptions` 는 전표 미리보기 옵션으로 이번 한 번 `PrintBackground` 를 **같은 객체에
        //   덮어쓸** 수 있다. 덮은 값으로 보면 옛 좌표가 남은 빈 A4(K4 · 저장값 0)가 그 한 번만
        //   칸 모양이 된다 ⇒ 덮기 전에 값(bool)을 떠 둔다.
        //   🔴 그림 깔기 판정(`ResolveFormBackground`)은 종전대로 덮은 값을 쓴다 — 이 줄은 그 판정을 안 바꾼다.
        //   🔴 양식정보설정 미리보기(`overrides.Template`)에서는 편집 중 값이 이 자리에 온다 — 편집창이
        //     빈 A4 좌표를 null 로 보내므로(좌표 0) 그 미리보기의 종이는 이 값과 상관없이 종전과 같다.
        var savedPrintBackground = template?.PrintBackground == true;
        template = ApplyPrintOptions(template, overrides?.Options);
        var isPreprint = template?.PaperMode == "preprint";
        // 🔴 봉합 2026-08-12 (사장님 지시 "양식정보 설정의 양식용지만 해")
        //   고객이 맞춘 좌표가 있으면 그대로 찍는다. 종전에는 이 값을 읽지도 않아
        //   코드 고정 위치로만 나갔고, 시판 양식지 칸과 안 맞았다.
        //
        // 🔴🔴 [2026-11-19 W-G1-51 갈래 A · 설계 §3 P1 · 대표 11/18 Q12] **`isPreprint &&` 제약을 뺐다.**
        //   종전 식 = `isPreprint && (template?.FieldCoords.Count ?? 0) > 0`
        //   [그대로 두면 무슨 일이 나나] 빈 A4(`plain`) 양식에 칸 좌표가 있어도 이 한 줄이
        //     좌표를 통째로 버렸다 ⇒ 값은 늘 히트판 표준 표(`ComposeBody`)로 갔다(설계 §2 C1 · C2).
        //     대표 11/18 08시② 의 Y 템플릿(흰 종이 + 양식 그림 + 칸 값)은 좌표를 타야 서는 모양이다.
        //   🔴 **좌표가 0개인 양식은 한 글자도 안 바뀐다** — 그때 이 값은 여전히 `false` 이고
        //     그 종이는 종전과 똑같다(#1). 바뀌는 것은 **좌표가 남은 빈 A4 양식**(Y 템플릿 · K4)뿐이고,
        //     K4 가 기본 폼 → 좌표 모양으로 바뀌는 것은 대표 11/18 Q12 「받아들임」 이다.
        //   🔴 양식지(`preprint`)는 종전에도 좌표를 탔다 — 그 종이는 안 바뀐다.
        //   🔴 세금계산서는 이 변화가 **안 닿는다** — `plain` 세금계산서는 아래 전용 작성기
        //     분기(`&& !isPreprint`)에서 이 값을 쓰기 전에 먼저 빠진다(법정 서식).
        //   🔴 좌표가 있으면 용지 · 방향이 A4 세로로 묶인다(`ResolvePageSize` 의 좌표 울타리) —
        //     좌표는 어느 종이에 대고 쟀는지가 전제이기 때문이다. 이제 `plain` 좌표 양식도 그 안이다.
        //   🔴 칸 맞춤 화면은 양식지 전용 그대로다(설계 §3 P2 안 가져옴 · 편집창 무변).
        // 🔴🔴 [2026-11-19 고침1 · 대표 §49] **위 「K4 가 기본 폼 → 좌표 모양으로 바뀌는 것은 Q12 받아들임」 은
        //   좁혀졌다** — 대표 §49 「옛 좌표가 남은 빈 A4 = 기본 폼 유지」. 칸 모양은 양식지와 Y 템플릿에만이다.
        //   위 주석은 이력으로 남긴다(지운 것 0). 판정 줄(`useCoords`)은 아래 그림 읽기(`bgImage`) **뒤로**
        //   내렸다 — Y 템플릿 판정에 「그 그림이 실제로 읽혔나」 가 들어가기 때문이다(구별 기준 §3).
        // 🔴 양식지 그림 (§13). 읽기만 한다 — 보관소·업로드 통로는 만들지 않는다(범위 밖).
        // 🔴🔴 [2026-10-24 반려 R-1 봉합] 양식지 그림은 **미리보기에만** 깐다.
        //   [내가 뭘 틀렸나] 결재된 §13-8 은 「종이에는 **안 찍히고**(이미 인쇄돼 있다)
        //     → 미리보기 배경으로 깔려 그 위에 필드 위치를 mm 로 맞춘다」 이고,
        //     §13-10 은 순백지에 업체 그림 찍기를 **「등재만 · 이번에 안 한다」** 로 못박았다.
        //     그런데 나는 **공용 인쇄 흐름**에 깔아 정반대를 넣었다(검증팀 반려 R-1).
        //   [그대로 두면] 양식용지 고객은 **이미 인쇄된 종이 위에 그림이 또 찍혀**
        //     이중 인쇄가 되고(§13-5 합격선 1 위반), 순백지 9종에도 업체 그림이 찍힌다.
        //   [그래서] 미리보기 경로에서만 그린다. 실인쇄 PDF 에는 **0건** —
        //     이메일·팩스·전표 인쇄는 전부 종전과 똑같다(#1).
        //
        // 🔴🔴 [2026-10-26작1] **위 두 줄은 이제 조건부다.** 사장님 오더 10/25 로
        //   「양식지 인쇄 / 순백지 인쇄」가 **고객 옵션**이 됐다(결-D·§C-3).
        //   ⇒ 실인쇄에 깔리는 경우가 생겼다: **고객이 그 양식에서 켠 경우에만**
        //     (`print_background=1`). 끈 고객은 여전히 **0건**이고 종전과 똑같다.
        //   🔴 「등재만 · 이번에 안 한다」였던 §13-10 이 **사장님 오더로 열렸다** —
        //     내가 임의로 넓힌 것이 아니다. 근거는 20261026작1 작업지시서 §C-3.
        var bgImage = ResolveFormBackground(overrides, template);

        // 🔴🔴 [2026-11-19 W-G1-51 갈래 A 고침1 · 대표 §49 「옛 좌표가 남은 빈 A4 = 기본 폼 유지」 · 구별 기준 §1-3 C · PM 결재 11/19]
        //   종전 식(갈래 A 첫 판) = `(template?.FieldCoords.Count ?? 0) > 0`  (main = `isPreprint && …`)
        //   [그대로 두면 무슨 일이 나나] 옛 좌표만 남은 빈 A4(K4)도 칸 모양으로 나가 자사 이름 · 제목 ·
        //     머리글이 빠지고, 한 쪽을 넘친 품목이 잘리고, 계산서 · 영수증도 같았다([3-V] P2-1 · P2-2 · [4] #1 · #2 · #5).
        //   [이제] 칸 모양 = 양식지(종전대로) **또는** Y 템플릿. Y 템플릿 = 아래 다섯 조건이 **모두** 참:
        //     ① `paper_mode` = "plain"(글자 그대로 · 모르는 값은 Y 아님) ② **저장된** `print_background` = 켬(위에서 떠 둔 값)
        //     ③ 그림 경로가 비지 않음 ④ 칸 좌표 1개 이상(`useCoords` 의 앞 조건) ⑤ 그 그림이 실제로 읽힘(`bgImage` != null)
        //   🔴 나머지 빈 A4(K4 · 좌표 0 · 그림을 못 읽은 Y)는 **main 과 같은 기본 폼**이다(#1 · 머리글 · 쪽번호 · 용지 · 여백 모두).
        //   🔴 ⑤ 는 이번 인쇄에 실제로 깔리는 그림이다 — 파일이 없어졌거나 못 읽으면, 또는 전표 미리보기 옵션으로
        //     그림을 끈 한 번은 기본 폼으로 나간다. 값만 흩어진 흰 종이보다 머리글 · 자사 이름 · 표가 있는 종이가 낫다(#20 · #25).
        //   🔴 한계(PM 결재 기록): 저장 문을 직접 불러 네 칸을 Y 와 똑같이 채운 행은 자료가 Y 와 글자까지 같아 못 가른다 → Y 로 본다.
        //   🔴 세금계산서 `plain` 은 아래 법정 작성기 분기(`&& !isPreprint`)에서 이 값을 쓰기 전에 먼저 빠진다 — 무변.
        var isYTemplate = string.Equals(template?.PaperMode, "plain", StringComparison.Ordinal)
            && savedPrintBackground
            && !string.IsNullOrWhiteSpace(template?.BackgroundImageUrl)
            && bgImage is not null;
        var useCoords = (template?.FieldCoords.Count ?? 0) > 0 && (isPreprint || isYTemplate);
        // 🔴🔴 [2026-11-20 W-G1-51 갈래 §59 · 대표 §59 「그림만 + 안내」 · PM 결재 ① (가) · ⑥ (나)]
        //   K3 = Y 판정 다섯 칸 중 ①②③⑤ 참(`isYTemplate`) **그리고** 칸 0 **그리고** 전표 [인쇄] 문 · 설정 미리보기 아님 · 법정 3종 아님.
        //   참이면 아래 머리글 · 본문 · 쪽번호를 건너뛰고 그림 겹(배경)만 남긴다 — 종이에 올린 양식 그림만 · 데이터 없음.
        //   🔴 위 두 문장(`isYTemplate` · `useCoords`)은 한 글자도 안 바꿨다 — 편집창 판정 게이트가 그 글자를 읽는다.
        //   🔴 판정은 `IsNoCoordsImageOnly` 한 자리다 — 인쇄 미리보기 창의 안내 문(`IsNoCoordsImageOnlyAsync`)도 그 함수만 부른다.
        //   🔴 위 :112 「좌표가 0개인 양식은 한 글자도 안 바뀐다」 는 K3(인쇄 문)에 한해 틀리게 됐다 — 그 줄은 이력으로 둔다(지운 것 0).
        //   🔴 §59 고침 1(작업지시서 ②-9 · P2-1) — 고침 1 부터 위 「칸 0」 = 저장된 칸 0(FormCoordsStored.IsEmpty) · 모두 무효 칸은 K3 아님(종전 기본 배치 + 데이터).
        var noCoordsImageOnly = IsNoCoordsImageOnly(documentType, overrides, isYTemplate, template);

        // 🔴 로고 워터마크 (20261026작1 §C-2 · DB-148 · 사장님 오더 10/25 ④).
        //   사장님: *"모든 양식지에 고객사 로고 워터마크 넣기 옵션 추가. 위치 = 정중앙 크게.
        //            문서의 내용을 해치지 않을정도의 명도채도. 대학교들 졸업증이나 재학증명서 느낌으로"*
        //
        // 🔴 수치는 **양식별 → 공통 → 제안값** 순으로 떨어진다(결-I: 공통 + 양식마다).
        //   양식별 값이 null 이면 「나는 정하지 않았다, 공통을 따르겠다」는 뜻이다.
        //   🔴 공통 설정 행이 **없는 것이 정상 경로**다 — 그때 제안값으로 떨어진다.
        //
        // 🔴 켜졌는지 판정도 같은 순서다 — 양식에서 켜면 켠다. 양식이 안 정했으면
        //   공통의 켬/끔을 따른다. 🔴 양식 행 자체가 없으면(신규 고객) **끔**이다.
        var watermark = await BuildWatermarkLayerAsync(tenantId, documentType, template, company, ct)
            .ConfigureAwait(false);

        var showHeader = template?.ShowCompanyLogo ?? true;
        var showBorder = template?.ShowBorder ?? true;
        var marginTop = (float)(template?.MarginTopMm ?? 20);
        var marginLeft = (float)(template?.MarginLeftMm ?? 20);
        var marginRight = (float)(template?.MarginRightMm ?? 20);
        var marginBottom = (float)(template?.MarginBottomMm ?? 20);

        // 🔴🔴 [2026-11-20 W-G1-51 갈래 E⑵ · 시안 §3-1 · 대표 §9 #1 「스타일 4종 = 순백지 양식에만」 · §11 #1 · 시안 PM Q-C]
        //   스타일(formal · simple · large)이 먹는 양식이면 그 값 묶음 · 아니면 null.
        //   🔴 null = basic = 옛 `ComposeHeader` · `ComposeBody` · 쪽번호 줄을 **그대로** 탄다(한 글자도 안 바뀜 · #1).
        //   🔴 양식지 · 좌표 경로(Y 템플릿 포함) · 그림 있는 양식(§59 K3 포함) · 모르는 키 = null(조건은 `ResolvePrintStyle`).
        //   🔴 세금계산서는 바로 아래 법정 작성기 분기에서 먼저 빠진다 — 이 값을 안 읽는다. 급여명세서는 다른 함수다.
        // 🔴 [2026-11-20 갈래 E⑵ 고침 1 · PM 판단 §72 E-1 · [3-V] P1-1] 문서 종류를 같이 넘긴다 —
        //   법정 서식(또는 그 후보) `LegalFormDocTypes`(세금계산서 · 면세계산서 · 입금표)는 스타일을 안 먹는다(조건 ⑥).
        // ⬛ 종전: var printStyle = ResolvePrintStyle(template, useCoords);
        var printStyle = ResolvePrintStyle(template, useCoords, documentType);
        // 🔴 [2026-11-20 갈래 E⑵ 고침 3] 격식 제목 글꼴(내장)을 그리기 전에 등록한다 —
        //   실패(리소스 없음 · 읽기 오류)면 경고 1줄 + 이 전표는 제목을 기본 글꼴로 그린다(#20 · 전표는 나간다).
        if (printStyle?.TitleFontFamily is not null)
        {
            try
            {
                // 옛 줄(W-G1-51 PDF 잠금 고침 1 전 · 헌법 #1 · PM §14-9 ⑤ (가) — 첫 등록을 PDF 생성과 같은 잠금 안으로): EnsureEmbeddedFontsRegistered();
                PdfGenerationGate.RunOneAtATime(EnsureEmbeddedFontsRegistered);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PDF] 격식 제목 글꼴을 등록하지 못했다 — 제목을 기본 글꼴로 그린다.");
                printStyle = printStyle with { TitleFontFamily = null };
            }
        }

        // 🔴 20261021작 절 B-3 — 세금계산서 전용(법정) 작성기.
        //   양식용지(preprint)·좌표 경로는 **종전 그대로** 간다(아래 공용 흐름).
        //   나머지 9종도 아래 공용 흐름 그대로 — 이 분기 안에서만 갈라진다(#1).
        // 🔴🔴 [2026-11-19 W-G1-51 갈래 F-1 · 착수 전 질문 (나) · PM 결재 11/19 20:3x · 착수준비표 P-8]
        //   종전 식 = `string.Equals(documentType, TaxInvoiceDocType, StringComparison.Ordinal) && !isPreprint`
        //   [그대로 두면 무슨 일이 나나] 「종이에도 함께 인쇄」 를 켠 세금계산서 양식지를 옮겨 만든 Y 템플릿
        //     (빈 A4 · 그림 찍음 · 칸 좌표)이 이 분기로 빠져 **히트판 법정 표준 서식**(그림 0 · 좌표 0)으로 나갔다 —
        //     대표 Q14 「그림까지 인쇄하던 모양 유지」 와 어긋난다(이름은 「(양식 그림 포함)」 인데 그림이 없음).
        //   [이제] Y 템플릿(위 `isYTemplate` 다섯 조건 = `isYTemplate && useCoords`)은 이 분기 **앞에서** 갈라
        //     아래 공용 흐름(그림 + 칸 값 · 한 쪽 · 넘치면 종이 알림 한 줄 = main 의 양식지+켬 세금계산서와 같은 종이)으로 간다.
        //   🔴 Y 아닌 `plain` 세금계산서는 한 글자도 안 바뀐다(#1) — 그림을 못 읽은 Y(⑤ 거짓)도 종전대로 법정 작성기.
        //   🔴 정정 줄: 위 :116 「세금계산서는 이 변화가 안 닿는다」 · :155 「세금계산서 `plain` … 무변」 ·
        //     아래 :219 「세금계산서 `plain` 은 위에서 이미 빠졌다」 는 **Y 세금계산서에 한해 틀리게 됐다** — 그 줄들은 이력으로 둔다(지운 것 0).
        //   🔴 법정성 판정 = 법무팀장 1줄(F-1 머지 전 · 착수 전 질문 §3 (나) 질문 글자) — 「안 됨」 이면 (다)로 대표께.
        //   🔴 워터마크는 계속 0 — 종 판정(결-K)이라 Y 세금계산서도 막힌다.
        if (string.Equals(documentType, TaxInvoiceDocType, StringComparison.Ordinal) && !isPreprint
            && !(isYTemplate && useCoords))
        {
            // 양식 행이 없으면 법정 2매가 기본이다(FormTemplateService 의 TwoCopyFormTypes).
            var copyMode = template?.PrintCopyMode ?? "both";
            // 🔴 20261021작 §10-2 ⓑ — 「한 장에 모으기」는 **고객이 켠 경우에만**.
            //   양식 행이 없으면 끔으로 본다(DB-147 기본값과 같다).
            var showTearLine = template?.ShowTearLine ?? false;
            // 🔴 20261024 사장님 결재 — 이 양식에서 직인을 찍나.
            //   🔴 **작성기 서명은 건드리지 않는다.** 처음에 인자를 하나 더했더니
            //     기존 게이트 136건이 통째로 깨졌다 — 그 시험들은 반사(reflection)로
            //     **인자 8개를 정확히** 넘긴다. 기본값이 있어도 Invoke 는 개수를 맞춘다.
            //     ⇒ 뜻은 같고 서명은 그대로인 길로 간다: **직인 주소를 비운다.**
            //   🔴 비우는 것이 왜 안전한가 — company 는 이 인쇄 한 번을 위해 DB 에서
            //     갓 읽은 객체다(LoadCompanyAsync · 캐시 0건). 고객 DB 값은 안 바뀐다.
            //   🔴 **배선만이다.** 직인 그림 보관소가 없어 seal_url 이 늘 비어 있다
            //     ⇒ 지금은 켜도 찍히지 않는다. 보관소가 생기는 날 이 칸이 켬/끔을 가른다.
            //   🔴 로고·테두리는 전달하지 않는다 — 세금계산서 머리·격자는 **법정 서식**이고
            //     끄면 서식이 깨진다(D-5 · 법무팀장 결재가 먼저다).
            if (template is not null && !template.ShowCompanySeal)
            {
                company.SealUrl = null;
            }

            var taxBytes = RenderTaxInvoiceDocument(data, company, copyMode, showTearLine,
                marginTop, marginLeft, marginRight, marginBottom);
            return (taxBytes, $"{KoLabel(documentType)}_{data.DocNo}.pdf");
        }

        // 🔴 [2026-11-19 W-G1-51 갈래 A 고침1 · 구별 기준 §2 안 (가) · PM 결재 11/19] Y 템플릿 품목 넘침 = **서버 경고 로그 1줄**.
        //   칸 모양은 한 쪽(297mm)에 고정이고 `max_rows` 를 넘친 품목은 잘린다(`ComposeBodyCoords` · 원래 양식지 main 과 같은 모양).
        //   🔴 **종이는 안 바꾼다** — 자르기 · 자리 · 종이 위 알림 모두 그대로(옛 결재 Q-4 「나머지 9종 종이 무변화」 · R-2 반려 이력).
        //     고객 알림(종이 · 화면)은 대표 재질문 거리라 이 차수 0 — 원격 진단 길만 둔다.
        //   🔴 `ComposeBodyCoords` 서명은 그대로다(시험이 반사로 부른다) — 줄 수 셈은 여기서 같은 규칙으로 한 번 더 한다.
        //   🔴 품목 이름 · 금액 등 **값은 적지 않는다** — 줄 수 · 테넌트 · 종 · 전표 id 만.
        //   🔴 양식지(`preprint`)는 이 줄을 안 탄다 — 양식지 로그도 main 과 같다. 세금계산서 `plain` 은 위에서 이미 빠졌다.
        //   🔴 「종이 안」 줄 수는 어림이다 — 가장 아래 품목 칸의 첫 줄 자리에서 줄 간격으로 297mm 앞까지 센다(글자 높이 안 셈).
        if (useCoords && isYTemplate && data.Lines.Count > 0)
        {
            var yCoords = template!.FieldCoords;
            var yLineCoords = yCoords
                .Where(fc => fc.Key.StartsWith("line.", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (yLineCoords.Count > 0)
            {
                var yMaxRows = yCoords.FirstOrDefault(fc =>
                    fc.Key.Equals("max_rows", StringComparison.OrdinalIgnoreCase))?.XMm ?? 0f;
                var yRowLimit = yMaxRows > 0 ? (int)yMaxRows : data.Lines.Count;
                var yPitch = yCoords.FirstOrDefault(fc =>
                    fc.Key.Equals("row_pitch", StringComparison.OrdinalIgnoreCase))?.YMm ?? 0f;
                if (yPitch <= 0f) yPitch = 6f;
                var yLowest = yLineCoords.Max(fc => fc.YMm);
                var yOnPaper = yLowest >= 297f ? 0 : (int)MathF.Ceiling((297f - yLowest) / yPitch);
                var yShown = Math.Min(Math.Min(data.Lines.Count, yRowLimit), yOnPaper);
                if (data.Lines.Count > yShown)
                {
                    _logger.LogWarning(
                        "[PDF] 좌표 양식(빈 A4 + 양식 그림)의 품목이 한 쪽에 다 안 들어간다 — 품목 {Total}줄 중 {Shown}줄만 종이에 나간다"
                        + "(줄 수 설정 {RowLimit} · 종이 안 어림 {OnPaper}). 종이는 그대로 그린다. tenant={Tenant} type={Type} id={Id}",
                        data.Lines.Count, yShown, yRowLimit, yOnPaper, tenantId, documentType, documentId);
                }
            }
        }

        var bytes = Document.Create(c =>
        {
            c.Page(page =>
            {
                // 🔴 20261024 사장님 결재 — 양식의 **용지 크기·방향**을 종이에 닿게 한다.
                //   종전에는 `PageSizes.A4` 가 못으로 박혀 있어 고객이 A5·가로를 골라도
                //   종이가 안 바뀌었다(저장은 되는데 아무 일도 안 일어나는 칸이었다).
                //   🔴 모르는 값이면 **A4·세로로 떨어진다** — 고객 종이가 안 나가는 일은 없다(#20).
                // 🔴 [2026-10-26작1] 값을 **변수로 받는다** — 워터마크 크기가 용지 폭의 %라
                //   폭을 다시 알아야 한다. 🔴 두 번 부르면 이 안의 경고 로그가 **두 줄** 찍혀
                //   다음 사람이 「두 번 그렸나」로 읽는다. 한 번만 부른다.
                var pageSize = ResolvePageSize(template, documentType, useCoords);
                page.Size(pageSize);
                // 🔴🔴 [2026-11-20 W-G1-51 갈래 E⑵ 고침 2 · 큰글씨 시안 정정 §4 R-4 · R-5 · PM 결재 §74] 폭 모자람 대체.
                //   큰글씨는 고정 칸 합 427pt + 품명 최소 32pt = 본문 폭 459pt 가 있어야 그린다.
                //   [그대로 두면 무슨 일이 나나] 고정 칸 합이 본문 폭을 넘으면 품명(상대 칸) 폭이 0 아래로 간다 —
                //     배치 엔진 동작 미측정 · 예외면 전표가 안 나간다(#20). 그래서 모자라면 큰글씨 대신 basic(옛 작성기 ·
                //     머리 · 본문 · 쪽번호 모두)으로 그리고 경고 1줄을 남긴다(A5 세로 = 늘 · A4 세로는 좌우 여백 24mm 언저리부터).
                //   🔴 W = 바로 위 `pageSize`(용지 · 방향 결과 · `ResolvePageSize` 1번 부름 그대로) 폭 − 좌우 여백(빈 칸 = 20mm) — 미리보기와 종이가 같은 셈(R-5).
                //   🔴 좌표 경로는 `printStyle` 이 이미 null(조건 ③)이라 여기 안 걸린다. 격식 · 간결은 문턱 0 = 검사 0.
                //   🔴 아래 머리 · 본문 · 쪽번호 줄이 이 값을 읽는다 — 이 칸 뒤에서 다시 바꾸지 않는다.
                if (printStyle is not null
                    && IsBodyTooNarrowForStyle(printStyle, pageSize.Width, marginLeft, marginRight))
                {
                    _logger.LogWarning(
                        "[PDF] 양식 스타일 {Style} 은 본문 폭 {Need}pt 이상에서만 그린다 — 이 용지 · 여백의 본문 폭은 {Width}pt 라 기본 모양으로 그린다. "
                        + "좌우 여백을 줄이거나 방향을 가로로 바꾸면 그 모양으로 나온다.",
                        printStyle.Key, printStyle.MinBodyWidth,
                        MathF.Round(BodyWidthPt(pageSize.Width, marginLeft, marginRight), 1));
                    printStyle = null;
                }
                // 🔴 스타일 칸을 **읽는다** — 지금은 늘 basic 이다(배선만 · 4종 디자인 미확정).
                //   종전에는 저장·조회만 되고 작성기가 이 칸을 아예 몰랐다. 모르는 값이 오면
                //   ResolveStyleKey 가 적어 둔다 — 조용히 넘어가지 않는다.
                // 🔴 [2026-11-20 갈래 E⑵] 위 「지금은 늘 basic」 은 **낡았다**(이력으로 둔다) — 4종 시안 승인(대표 §11 #1).
                //   이 줄은 그대로 모르는 값 경고만 맡는다. 그리는 값 묶음은 위 `printStyle`(순백지에만)이다.
                _ = ResolveStyleKey(template);
                // 좌표 인쇄는 원점이 **종이 왼쪽 위 모서리**다(고객이 자를 대는 기준).
                //   여백을 주면 좌표가 그만큼 통째로 밀린다 ⇒ 이 경로만 여백 0.
                if (useCoords)
                {
                    page.MarginTop(0); page.MarginLeft(0); page.MarginRight(0); page.MarginBottom(0);
                }
                else
                {
                    page.MarginTop(marginTop, Unit.Millimetre);
                    page.MarginLeft(marginLeft, Unit.Millimetre);
                    page.MarginRight(marginRight, Unit.Millimetre);
                    page.MarginBottom(marginBottom, Unit.Millimetre);
                }
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(10));
                page.PageColor(Colors.White);

                // 🔴 20261024 사장님 오더 — 업체 양식지 그림을 종이에 깐다 (§13).
                //   사장님: *"양식지를 추가할수도 있어야해. 이미 업체에서 쓰는 양식지가 있을수도 있으니."*
                //   🔴 종전에는 BackgroundImageUrl 이 작성기에 **0건**이었다 — 올려도 안 나왔다.
                //   🔴 그림은 **맨 밑에** 깔고 내용은 그 위에 그린다. 스캔한 양식지 칸에 값이
                //      들어가는 모양을 눈으로 맞추는 것이 미리보기의 존재 이유다.
                //   🔴 그림이 없거나 못 읽으면 **종전과 똑같이** 그린다(#1 — 고객 종이 안 바뀜).
                //   🔴 이 자리는 **공용 흐름**이다 ⇒ 양식용지(preprint) 경로의 10종 전부에 닿는다.
                //      세금계산서도 preprint 면 이 흐름으로 온다(위 전용 분기는 !isPreprint).
                // 🔴🔴 [2026-10-26작1 §C-1] 배경 자리에 **두 겹**이 들어올 수 있게 바꿨다 —
                //   양식지 그림과 워터마크다. 종전에는 그림 한 장만 깔았다.
                //   🔴 둘 다 **본문 아래**다(배경 자리) — 글자가 워터마크 위에 보인다.
                //      사장님 오더 *"문서의 내용을 해치지 않을정도"* 가 이 순서를 가리킨다.
                //   🔴🪤 **`Layers` 에는 `PrimaryLayer` 가 정확히 하나 있어야 한다** —
                //      빠뜨리면 `DocumentComposeException`:
                //      *"The Layers component needs to have exactly one primary layer. It has none."*
                //      ⇒ 그 양식의 인쇄가 **전부 500** 이 된다(2026-10-26 실측으로 확인).
                //   🔴 둘 다 없으면 **Layers 를 아예 만들지 않는다** — 끈 고객의 종이는
                //      종전과 글자 하나 다르지 않다(#1).
                if (bgImage is not null || watermark is not null)
                {
                    // 🔴🔴 [2026-10-26 재검증 V-1 봉합] 조립을 **떼어내 한 자리로** 옮겼다 —
                    //   게이트가 **제품의 이 조립을 그대로 불러** 그릴 수 있어야 하기 때문이다.
                    //   종전에는 게이트가 `Layers`·`MaxWidth`·`MaxHeight` 를 **시험 안에서 복제**하고
                    //   제품에서 가져오는 것은 길이 한 줄뿐이었다 ⇒ 재검증이 R-1 버그를 실코드에
                    //   되살려 놓고 돌렸는데 **G-W16 이 10/10 초록**이었다. 거짓 초록이다.
                    ComposeDocumentBackground(
                        page, pageSize, bgImage, watermark?.Svg, watermark?.ScalePct ?? 0);
                }

                // preprint(양식용지): 헤더·테두리 미렌더, 필드값만 저장
                // 🔴🔴 [2026-11-19 W-G1-51 갈래 A · 설계 §3 P1] **좌표 경로는 머리글을 안 그린다.**
                //   종전 식 = `!isPreprint && showHeader`
                //   [그대로 두면 무슨 일이 나나] 좌표 경로는 여백이 **0** 이다(위 `useCoords` 분기).
                //     그 종이에 표준 머리글을 또 그리면 Y 템플릿 양식 그림의 맨 윗칸 위에
                //     「견적서 / 문서번호 / 회사이름」 이 **겹쳐 찍힌다** ⇒ 「기본 폼이 나온다」 의 재발.
                //   🔴 `preprint` 는 종전에도 안 그렸다 — 그 종이는 안 바뀐다(#1).
                //   🔴 좌표가 0개인 `plain` 양식도 안 바뀐다 — `useCoords` 가 `false` 다(#1).
                // 🔴 [2026-11-20 W-G1-51 갈래 §59 · 대표 §59] K3(인쇄 문 · 빈 A4 + 그림 + 칸 0)는 머리글을 안 그린다 — 그림만.
                //   위 「좌표가 0개인 `plain` 양식도 안 바뀐다」 는 K3 에 한해 틀리게 됐다(이력으로 둔다).
                //   ⬛ 종전 식: if (!isPreprint && !useCoords && showHeader)
                if (!isPreprint && !useCoords && showHeader && !noCoordsImageOnly)
                {
                    // 🔴 [2026-11-20 갈래 E⑵] 스타일 양식일 때만 새 머리를 더했다 — basic 은 아래 옛 줄 그대로(#1).
                    if (printStyle is not null)
                        page.Header().Element(e => ComposeHeaderStyled(e, data, company, printStyle));
                    else
                        page.Header().Element(e => ComposeHeader(e, data, company));
                }
                // 🔴 [2026-11-20 W-G1-51 갈래 §59 · 대표 §59] K3 = **빈 본문**(표 · 품목 · 합계 0) — 갈래 하나를 맨 앞에 더했다.
                //   본문을 안 채운 쪽은 그림 겹(위 배경)만 남는다. 나머지 세 갈래는 그대로다(#1).
                //   🔴 안내 글자를 종이에 찍지 않는다(장치 §4 금지 · PM 결재 ③) — 안내는 인쇄 미리보기 창에만.
                if (noCoordsImageOnly)
                {
                    /* 빈 본문 */
                }
                // ⬛ 종전 줄: if (useCoords)
                else if (useCoords)
                    page.Content().Element(e => ComposeBodyCoords(e, data, template!.FieldCoords));
                // 🔴 [2026-11-20 갈래 E⑵] 스타일 양식(순백지 · 좌표 경로 아님)일 때만 새 본문 — 칸 하나 더함 · 옛 두 갈래 무변.
                //   `printStyle` 은 `preprint` 에서 늘 null 이다(`ResolvePrintStyle` ②) ⇒ 양식지는 아래 옛 줄로 간다.
                else if (printStyle is not null)
                    page.Content().Element(e => ComposeBodyStyled(e, data, showBorder, printStyle));
                else
                    page.Content().Element(e => ComposeBody(e, data, isPreprint, showBorder));
                // 🔴 [2026-11-19 W-G1-51 갈래 A · 설계 §3 P1] 쪽번호도 좌표 경로에서는 안 찍는다 —
                //   종전 식 = `!isPreprint` · 여백 0 이라 양식 그림의 맨 아랫칸을 덮는다(머리글과 같은 까닭).
                //   좌표 0개 양식 · `preprint` 는 종전과 같다(#1).
                // 🔴 [2026-11-20 W-G1-51 갈래 §59 · 대표 §59] K3 는 쪽번호도 안 찍는다(「Page 1 / 1」 0) — 그림만.
                //   위 「좌표 0개 양식 … 종전과 같다」 는 K3 에 한해 틀리게 됐다(이력으로 둔다).
                //   ⬛ 종전 식: if (!isPreprint && !useCoords)
                if (!isPreprint && !useCoords && !noCoordsImageOnly)
                {
                    // 🔴 [2026-11-20 갈래 E⑵ · 시안 §2-2 쪽 번호 8 · 8 · 8 · 9] 글자 크기만 값으로 받는다.
                    //   ⬛ 종전: t.Span("Page ").FontSize(8); t.CurrentPageNumber().FontSize(8);
                    //   ⬛ 종전: t.Span(" / ").FontSize(8); t.TotalPages().FontSize(8);
                    //   🔴 basic(`printStyle` null) = 8 그대로 — 종이 무변(#1).
                    var pageNoSize = printStyle?.PageNumberSize ?? 8f;
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("Page ").FontSize(pageNoSize); t.CurrentPageNumber().FontSize(pageNoSize);
                        t.Span(" / ").FontSize(pageNoSize); t.TotalPages().FontSize(pageNoSize);
                    });
                }
            });
        // 옛 줄(W-G1-51 PDF 잠금 전 · 헌법 #1): }).GeneratePdf();
        }).GeneratePdfOneAtATime();

        var fileName = $"{KoLabel(documentType)}_{data.DocNo}.pdf";
        return (bytes, fileName);
    }

    private static string KoLabel(string docType) => docType switch
    {
        "quotation" => "견적서",
        "sales_order" => "수주서",
        "delivery" => "거래명세서",
        "tax_invoice" => "세금계산서",
        "purchase_order" => "발주서",
        "purchase_receipt" => "매입명세서",
        // 🔴 [2026-11-26 W-G1-51 R1 회송 갈래 R-D · 대표 결재 §221 #5] 반품 2종 제목 — 종전엔 이 표에 없어 「문서」 로 찍혔다.
        //   머리(basic · 스타일) · 파일 이름이 모두 이 표를 탄다. 판매 반품 입구 셋(반품 화면 · 거래명세서 화면 · 판매반품 화면)은
        //   모두 "sales-return" → DocumentController.PrintTypeToDocumentType → "sales_return" 한 길.
        "purchase_return" => "매입반품",
        "sales_return" => "판매반품",
        _ => "문서"
    };

    private static void ComposeHeader(IContainer e, DocumentSnapshot d, CompanyInfo c)
    {
        e.Column(col =>
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Column(left =>
                {
                    left.Item().Text(KoLabel(d.DocType)).FontSize(20).Bold();
                    left.Item().PaddingTop(2).Text($"문서번호: {d.DocNo}").FontSize(9);
                    left.Item().Text($"일자: {d.DocDate:yyyy-MM-dd}").FontSize(9);
                });
                r.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text(c.CompanyName).FontSize(12).Bold();
                    if (!string.IsNullOrWhiteSpace(c.BusinessNo))
                        right.Item().Text($"사업자: {c.BusinessNo}").FontSize(9);
                    if (!string.IsNullOrWhiteSpace(c.RepresentativeName))
                        right.Item().Text($"대표: {c.RepresentativeName}").FontSize(9);
                    if (!string.IsNullOrWhiteSpace(c.Address))
                        right.Item().Text(c.Address).FontSize(8);
                    if (!string.IsNullOrWhiteSpace(c.Phone))
                        right.Item().Text($"TEL: {c.Phone}").FontSize(8);
                });
            });
            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Darken2);
        });
    }

    private static void ComposeBody(IContainer e, DocumentSnapshot d, bool isPreprint = false, bool showBorder = true)
    {
        // preprint(양식용지): 거래처·합계 박스 테두리·헤더 미렌더, 필드값만 좌표 저장
        if (isPreprint)
        {
            ComposeBodyPreprint(e, d);
            return;
        }
        var borderWidth = showBorder ? 0.5f : 0f;
        // 🔴 [2026-10-26작1 §C-4] 격자를 끄면 칸이 좁아 보인다 — 패딩으로 보정한다.
        //   🔴 showBorder=1 일 때는 4 그대로다 — 기존 고객의 종이가 한 픽셀도 안 바뀐다.
        //   🔴 지역함수(CellHead·CellBody)가 이 값을 캡처하므로 **쓰이는 자리보다 앞에서** 정한다.
        var cellPad = showBorder ? 4 : 5;
        e.PaddingTop(10).Column(col =>
        {
            // 거래처 박스
            col.Item().PaddingBottom(8).Border(borderWidth).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(p =>
            {
                p.Item().Text("[ 거래처 정보 ]").FontSize(9).SemiBold();
                p.Item().PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text($"상호: {d.PartnerName}").FontSize(10);
                    if (!string.IsNullOrWhiteSpace(d.PartnerBusinessNo))
                        r.RelativeItem().AlignRight().Text($"사업자: {d.PartnerBusinessNo}").FontSize(9);
                });
                if (!string.IsNullOrWhiteSpace(d.PartnerAddress))
                    p.Item().Text($"주소: {d.PartnerAddress}").FontSize(9);
                if (!string.IsNullOrWhiteSpace(d.PartnerContact))
                    p.Item().Text($"담당: {d.PartnerContact}").FontSize(9);
            });

            // 품목 라인 표
            col.Item().Table(tbl =>
            {
                tbl.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(30);          // 순번
                    c.RelativeColumn(3);           // 품명
                    c.RelativeColumn(2);           // 규격
                    c.ConstantColumn(60);          // 수량
                    c.ConstantColumn(80);          // 단가
                    c.ConstantColumn(90);          // 공급가
                    c.ConstantColumn(70);          // 부가세
                    c.ConstantColumn(90);          // 합계
                });
                tbl.Header(h =>
                {
                    h.Cell().Element(CellHead).Text("순");
                    h.Cell().Element(CellHead).Text("품명");
                    h.Cell().Element(CellHead).Text("규격");
                    h.Cell().Element(CellHead).AlignRight().Text("수량");
                    h.Cell().Element(CellHead).AlignRight().Text("단가");
                    h.Cell().Element(CellHead).AlignRight().Text("공급가액");
                    h.Cell().Element(CellHead).AlignRight().Text("부가세");
                    h.Cell().Element(CellHead).AlignRight().Text("합계");
                });
                int seq = 1;
                foreach (var line in d.Lines)
                {
                    tbl.Cell().Element(CellBody).AlignCenter().Text($"{seq++}");
                    tbl.Cell().Element(CellBody).Text(line.ItemName);
                    tbl.Cell().Element(CellBody).Text(line.Spec ?? "");
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.Qty.ToString("N1"));
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.UnitPrice.ToString("N0"));
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.Supply.ToString("N0"));
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.Vat.ToString("N0"));
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.Total.ToString("N0"));
                }
                if (d.Lines.Count == 0)
                {
                    tbl.Cell().ColumnSpan(8).Element(CellBody).AlignCenter().Text("(품목 정보 없음)").FontColor(Colors.Grey.Darken1);
                }
            });

            // 합계 박스
            col.Item().PaddingTop(8).AlignRight().Column(p =>
            {
                p.Item().Text($"공급가액 합계: {d.TotalSupply:N0}").FontSize(10);
                p.Item().Text($"부가세 합계  : {d.TotalVat:N0}").FontSize(10);
                p.Item().PaddingTop(4).Text($"총 합계: {d.TotalAmount:N0} 원").FontSize(13).Bold();
            });

            if (!string.IsNullOrWhiteSpace(d.Remark))
            {
                col.Item().PaddingTop(10).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(p =>
                {
                    p.Item().Text("비고").FontSize(9).SemiBold();
                    p.Item().Text(d.Remark).FontSize(9);
                });
            }
        });

        // 🔴🔴 [2026-10-26작1 §C-4 · 결-G] 품목표 격자를 **`show_border` 에 묶었다.**
        //   사장님 10/25: *"테두리, 격자, 로고 켜고 끄는것 = 양식만 가져오고,
        //                   테두리, 격자, 로고는 끄는것 동의"*
        //
        // 🔴 **왜 안 꺼졌나** — 이 두 함수가 `static` 지역함수였다. `static` 이면
        //   바깥 변수를 **캡처할 수 없어** `showBorder` 를 아예 볼 수 없었다.
        //   그래서 `0.5f` 가 고정이었고, 바로 위 `:320` 에 `borderWidth` 가
        //   **이미 계산돼 있는데 쓰이지 않았다.** 고객이 「테두리 끄기」를 켜면
        //   거래처 박스는 꺼지고 **품목표 격자만 남는** 반쪽 종이가 나왔다.
        //   ⇒ `static` 을 떼고 그 `borderWidth` 를 쓴다. 급여명세서의
        //      `PayslipCell(c, showBorder)` 가 처음부터 올바른 본이었다.
        //
        // 🔴 테두리를 0 으로 만들면 칸이 좁아 보인다 ⇒ **패딩으로 보정한다.**
        //   사장님 결재: *"묶은 뒤 실 문서 크기는 용지에 맞춰 임의 조정"*.
        //   🔴 `showBorder=1` 일 때의 종이는 **한 픽셀도 달라지지 않아야 한다**(기존 고객 보호).
        IContainer CellHead(IContainer x)
        {
            var box = showBorder ? x.Background(Colors.Grey.Lighten3) : x;
            return box.Border(borderWidth).BorderColor(Colors.Grey.Lighten1).Padding(cellPad);
        }
        IContainer CellBody(IContainer x) => x.Border(borderWidth).BorderColor(Colors.Grey.Lighten2).Padding(cellPad);
    }

    // ─── 양식 스타일 4종 (2026-11-20 W-G1-51 갈래 E⑵) ─────────────────────
    //   근거: 시안 `docs/설계/erp/20261118_WG1-51_양식스타일4종_시안.md` §2-1 · §2-2 · §3-1 · 대표 §11 #1(시안 승인)
    //   · §9 #1(순백지에만) · 시안 PM Q-C(양식지 · 좌표 양식엔 무시) · 개발명세서 `20261120작_WG1-51_갈래E2_스타일_개발명세서.md`.
    //   🔴 basic 은 이 아래를 **안 탄다** — 옛 `ComposeHeader` · `ComposeBody` 그대로(#1 · 한 글자도 안 바뀜).
    //   🔴 `ComposeBody` 서명을 안 건드린 까닭: 시험(`GridBorderRenderGateTests`)이 반사로 **4인자** 부른다 —
    //     인자를 더하면(선택 인자라도) 깨지고, 같은 이름으로 겹쳐 지으면 `GetMethod` 가 모호해진다.

    private const string StyleInk = "#000000";

    // ─── 격식 제목 글꼴 (2026-11-20 W-G1-51 갈래 E⑵ 고침 3) ─────────────────────
    //   근거: 대표 §11 #2(Noto Serif KR 내장 승인) · 착수준비표 P-5(어셈블리 내장 리소스) · 법무 1줄 ①~⑥ + 해석 덧붙임 · PM §74.
    //   🔴 원본 1개 손질 0 — notofonts/noto-cjk 태그 Serif2.003 · 커밋 9b0f1436e455d902de067a2501422e5dc71ad16b ·
    //     Serif/SubsetOTF/KR/NotoSerifKR-Bold.otf · SHA-256 = 개발명세서 §고침 3 · 시험 `FormalTitleFontEmbedGateTests` 가 저장소 · 내장 둘 다 대조.
    //   🔴 고지 = 저장소 글꼴 옆 `NotoSerifKR-OFL.txt` → api 출력 · 게시 `licenses\`(HitPan.API.csproj · 설치 · 업데이트 스크립트 0줄).
    //   🔴 이름을 바꿔 등록하지 않는다(`RegisterFontWithCustomName` 안 씀) — 파일의 가족 이름(이름 기록 ID 1)으로 등록된다.

    /// <summary>내장 리소스 이름 — <c>HitPan.Application.csproj</c> 의 <c>EmbeddedResource LogicalName</c> 과 같은 글자.</summary>
    private const string FormalTitleFontResourceName = "HitPan.Application.Resources.Fonts.NotoSerifKR-Bold.otf";

    /// <summary>글꼴 파일 이름 기록 ID 1(가족 이름) 글자 그대로 — 격식 제목에만 쓴다.</summary>
    private const string FormalTitleFontFamily = "Noto Serif KR";

    /// <summary>
    /// 🔴 내장 글꼴 등록 — 처음 쓸 때 <b>1번</b>(스레드 안전). 실패(리소스 없음 · 읽기 오류)는 값에 담겨 부를 때마다 다시 던진다.
    /// </summary>
    private static readonly Lazy<bool> EmbeddedFontRegistration =
        new(RegisterEmbeddedFonts, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>내장 글꼴이 등록돼 있게 한다 — 실패면 예외(제품 길은 <c>RenderDocumentAsync</c> 가 경고 1줄 + 기본 글꼴로 내림).</summary>
    private static void EnsureEmbeddedFontsRegistered() => _ = EmbeddedFontRegistration.Value;

    private static bool RegisterEmbeddedFonts()
    {
        using var stream = typeof(PdfRenderService).Assembly.GetManifestResourceStream(FormalTitleFontResourceName)
            ?? throw new InvalidOperationException(
                $"격식 제목 글꼴 내장 리소스({FormalTitleFontResourceName})가 없다 — 빌드 산출물을 확인해 주십시오.");
        QuestPDF.Drawing.FontManager.RegisterFont(stream);
        return true;
    }

    /// <summary>
    /// 🔴 양식 디자인 스타일 값 묶음 — 시안 §2-2 표를 <b>그대로</b> 옮겼다(formal · simple · large).
    /// </summary>
    /// <remarks>
    /// 🔴 basic 인스턴스는 없다 — basic 은 옛 작성기를 그대로 탄다(위 주석).
    /// 🔴 시안 표에 없는 값(간격 · 비고 안쪽 6 · 비고 표제 반굵게 · 열 너비)은 basic 값 그대로다.
    /// 🔴 ⬛ 정정(2026-11-20 갈래 E⑵ 고침 2 · 큰글씨 시안 정정 §2-1 · PM 결재 §74): 열 너비 · 숫자 칸 안쪽은 <b>큰글씨만</b> 따로다 — 격식 · 간결은 basic 값 그대로.
    /// 🔴 글자는 모두 검정 · 강조색 0(시안 §0 「색」 · §2 「글자는 모두 #000」).
    /// </remarks>
    private sealed record PrintStyle
    {
        public required string Key { get; init; }

        // 머리(시안 §2-1 · §2-2)
        public bool TitleCentered { get; init; }
        public float TitleSize { get; init; }
        /// <summary>QuestPDF 자간은 글자 크기 <b>비례</b> 값이다 — 시안 「+6pt」 = 6 / 제목 크기.</summary>
        public float TitleLetterSpacing { get; init; }
        /// <summary>
        /// 🔴 비움 = 본문 글꼴(맑은 고딕). 격식 = Noto Serif KR 은 <b>글꼴 파일 들이기 뒤</b>
        /// (착수준비표 P-5 「법무팀장 1줄 = 들이기 전」 · 개발명세서 §2 멈춤) — 그때 이 값 + 등록 줄만 더한다.
        /// ⬛ 정정(2026-11-20 고침 3): 들임 끝 — 격식 = <c>Noto Serif KR</c>(내장 · 처음 쓸 때 등록). 간결 · 큰글씨 = 비움 그대로.
        /// </summary>
        public string? TitleFontFamily { get; init; }
        public float DocMetaSize { get; init; }
        public float CompanyNameSize { get; init; }
        /// <summary>사업자 · 대표 줄.</summary>
        public float CompanyIdSize { get; init; }
        /// <summary>주소 · TEL 줄.</summary>
        public float CompanyContactSize { get; init; }
        public float RuleWidth { get; init; }
        public string RuleColor { get; init; } = StyleInk;
        /// <summary>0 = 한 줄. 격식 = 굵은 선 아래 띄고 가는 선 하나 더(이중선).</summary>
        public float RuleSecondGap { get; init; }
        public float RuleSecondWidth { get; init; }

        // 거래처
        public bool PartnerBoxed { get; init; }
        /// <summary>박스면 박스 선 · 박스 없음(간결)이면 표제 밑줄.</summary>
        public float PartnerLine { get; init; }
        public string PartnerLineColor { get; init; } = StyleInk;
        /// <summary>박스면 사방 안쪽 · 박스 없음(간결)이면 위아래만.</summary>
        public float PartnerPad { get; init; }
        public float PartnerLabelSize { get; init; }
        public float PartnerNameSize { get; init; }
        public float PartnerOtherSize { get; init; }

        // 품목표
        public float TableOuterLine { get; init; }
        public string TableOuterColor { get; init; } = StyleInk;
        /// <summary>간결 = 위 · 아래만(좌우 0).</summary>
        public bool TableOuterTopBottomOnly { get; init; }
        public string? HeadBackground { get; init; }
        public float HeadLine { get; init; }
        public string HeadLineColor { get; init; } = StyleInk;
        public bool HeadLineBottomOnly { get; init; }
        public float HeadTextSize { get; init; }
        public float BodyLine { get; init; }
        public string BodyLineColor { get; init; } = StyleInk;
        /// <summary>간결 = 가로선만 · 세로 0.</summary>
        public bool BodyLineHorizontalOnly { get; init; }
        public float BodyTextSize { get; init; }
        /// <summary>큰글씨 = 짝수 줄 면.</summary>
        public string? ZebraEvenBackground { get; init; }
        public float CellPadVertical { get; init; }
        public float CellPadHorizontal { get; init; }
        /// <summary>
        /// 🔴 품명 : 규격 비율 — 지금 4종 모두 basic 과 같은 3 : 2.
        /// 큰글씨 품명 칸 넓힘 폭은 <b>문서에 수치가 없어</b> 짐작하지 않았다(개발명세서 §3 멈춤 · 대표 §11 #3 「둘 다 허용」).
        /// ⬛ 정정(고침 2 · 큰글씨 시안 정정 §2-1): 큰글씨는 규격이 고정 칸(<c>SpecColumnWidth</c> 51)이라 품명이 남는 폭을 다 받는다(비율 1).
        /// </summary>
        public float ItemNameRelative { get; init; } = 3;
        public float SpecRelative { get; init; } = 2;
        // 🔴🔴 [2026-11-20 W-G1-51 갈래 E⑵ 고침 2 · 큰글씨 시안 정정 §2-1 · §4 R-4 · PM 결재 §74] 열 너비 · 숫자 칸 안쪽 · 폭 문턱.
        //   🔴 기본값 = basic 열 너비(30 · 3:2 · 60 · 80 · 90 · 70 · 90) · 지금 안쪽 그대로 ⇒ 값을 안 적는 격식 · 간결 종이는 안 바뀐다(#1).
        /// <summary>순 칸 폭(pt).</summary>
        public float SeqColumnWidth { get; init; } = 30;
        /// <summary>비움 = 규격도 상대 칸(<c>SpecRelative</c>). 값 = 고정 칸 폭(pt) — 큰글씨 51(한글 4자 · 정정 §2-2).</summary>
        public float? SpecColumnWidth { get; init; }
        public float QtyColumnWidth { get; init; } = 60;
        public float UnitPriceColumnWidth { get; init; } = 80;
        public float SupplyColumnWidth { get; init; } = 90;
        public float VatColumnWidth { get; init; } = 70;
        public float TotalColumnWidth { get; init; } = 90;
        /// <summary>
        /// 숫자 칸(수량 · 단가 · 공급가액 · 부가세 · 합계 · 머리 포함) 좌우 안쪽. 비움 = 글자 칸과 같은 <c>CellPadHorizontal</c>.
        /// </summary>
        public float? NumberCellPadHorizontal { get; init; }
        /// <summary>
        /// 이 스타일을 그릴 수 있는 가장 좁은 본문 폭(pt). 0 = 검사 없음.
        /// 큰글씨 = 459(고정 칸 27 + 51 + 61 + 72 × 4 = 427 + 품명 최소 32 · 정정 §4 R-4) — 모자라면 basic 으로 그린다(<c>IsBodyTooNarrowForStyle</c>).
        /// </summary>
        public float MinBodyWidth { get; init; }
        // 🔴 큰글씨 품명 줄바꿈(잘림 0 · 대표 §11 #3) = QuestPDF 배치 엔진 기본 동작이다 — 긴 낱말도 칸 안에서 접는다.
        //   `WrapAnywhere()` 는 2024.3 판부터 쓰지 않는 칸(CS0618 「layout engine 이 알아서 다룬다」 · 이 저장소 2026.2.4)이라 안 쓴다.
        //   🔴 그래서 품명 칸에 줄 수 제한(`ClampLines`) · 높이 고정을 **걸지 않는 것**이 잘림 0 의 조건이다.

        // 합계 · 비고 · 쪽
        public float SumSize { get; init; }
        public float TotalSize { get; init; }
        /// <summary>총 합계 위 선(0 = 없음).</summary>
        public float TotalRule { get; init; }
        public float RemarkLine { get; init; }
        public string RemarkLineColor { get; init; } = StyleInk;
        /// <summary>간결 = 위 선만.</summary>
        public bool RemarkTopOnly { get; init; }
        public float RemarkSize { get; init; }
        public float PageNumberSize { get; init; }
    }

    /// <summary>격식 <c>formal</c> — 처음 거래 · 관공서 · 큰 거래처 제출(시안 §0).</summary>
    private static readonly PrintStyle FormalStyle = new()
    {
        Key = "formal",
        TitleCentered = true, TitleSize = 22, TitleLetterSpacing = 6f / 22f,
        // 🔴 [2026-11-20 갈래 E⑵ 고침 3 · 대표 §11 #2 · 시안 §3-2] 격식 제목 글꼴 — 제목 한 줄에만(본문 · 거래처 · 합계 = 맑은 고딕 그대로).
        TitleFontFamily = FormalTitleFontFamily,
        DocMetaSize = 9, CompanyNameSize = 12, CompanyIdSize = 9, CompanyContactSize = 8,
        RuleWidth = 1.5f, RuleSecondGap = 1.5f, RuleSecondWidth = 0.5f,
        PartnerBoxed = true, PartnerLine = 0.75f, PartnerPad = 8,
        PartnerLabelSize = 9, PartnerNameSize = 10, PartnerOtherSize = 9,
        TableOuterLine = 1.2f,
        HeadBackground = "#E6E6E6", HeadLine = 0.5f, HeadTextSize = 10,
        BodyLine = 0.5f, BodyLineColor = "#424242", BodyTextSize = 10,
        CellPadVertical = 4, CellPadHorizontal = 4,
        SumSize = 10, TotalSize = 14, TotalRule = 0.75f,
        RemarkLine = 0.75f, RemarkSize = 9,
        PageNumberSize = 8,
    };

    /// <summary>간결 <c>simple</c> — 팩스 · 잉크 절약(시안 §0).</summary>
    private static readonly PrintStyle SimpleStyle = new()
    {
        Key = "simple",
        TitleSize = 18,
        DocMetaSize = 9, CompanyNameSize = 11, CompanyIdSize = 8.5f, CompanyContactSize = 8,
        RuleWidth = 0.75f,
        PartnerBoxed = false, PartnerLine = 0.5f, PartnerPad = 4,
        PartnerLabelSize = 9, PartnerNameSize = 10, PartnerOtherSize = 9,
        TableOuterLine = 1, TableOuterTopBottomOnly = true,
        HeadBackground = null, HeadLine = 0.75f, HeadLineBottomOnly = true, HeadTextSize = 9.5f,
        BodyLine = 0.4f, BodyLineColor = "#9E9E9E", BodyLineHorizontalOnly = true, BodyTextSize = 9.5f,
        CellPadVertical = 4, CellPadHorizontal = 5,
        SumSize = 9.5f, TotalSize = 12, TotalRule = 1,
        RemarkLine = 0.5f, RemarkLineColor = "#9E9E9E", RemarkTopOnly = true, RemarkSize = 9,
        PageNumberSize = 8,
    };

    /// <summary>큰글씨 <c>large</c> — 창고 · 현장 · 멀리서 읽기(시안 §0).</summary>
    private static readonly PrintStyle LargeStyle = new()
    {
        Key = "large",
        TitleSize = 24,
        DocMetaSize = 11, CompanyNameSize = 14, CompanyIdSize = 10.5f, CompanyContactSize = 10,
        RuleWidth = 1.5f,
        PartnerBoxed = true, PartnerLine = 0.75f, PartnerLineColor = "#757575", PartnerPad = 10,
        PartnerLabelSize = 11, PartnerNameSize = 12, PartnerOtherSize = 11,
        TableOuterLine = 0.75f, TableOuterColor = "#757575",
        HeadBackground = "#E0E0E0", HeadLine = 0.75f, HeadLineColor = "#757575", HeadTextSize = 11,
        // 🔴🔴 [2026-11-20 갈래 E⑵ 고침 2 · 큰글씨 시안 정정 §2-1 1안 · PM 결재 §74] 표 본문 11 → 10.5 · 안쪽 6 → 글자 칸 3 · 숫자 칸 2 · 위아래 5 ·
        //   큰글씨 전용 열 너비 · 폭 문턱 459. 까닭 = 시안 값(11 · 안쪽 6)으로는 부가세 9자리 · 수량 큰 수가 칸을 넘어 줄이 바뀌고
        //   품명 2자/줄 · 규격 1자/줄([4] F1 · F3) — 1안 = 10자리 금액 · 수량 1,000,000.0 · 규격 한글 4자 줄바꿈 0 · 품명 4자/줄(정적 셈 · 정정 §2-2).
        //   ⬛ 종전: BodyLine = 0.5f, BodyLineColor = "#9E9E9E", BodyTextSize = 11,
        BodyLine = 0.5f, BodyLineColor = "#9E9E9E", BodyTextSize = 10.5f,
        ZebraEvenBackground = "#F2F2F2",
        //   ⬛ 종전: CellPadVertical = 6, CellPadHorizontal = 6,
        CellPadVertical = 5, CellPadHorizontal = 3, NumberCellPadHorizontal = 2,
        SeqColumnWidth = 27, ItemNameRelative = 1, SpecColumnWidth = 51,
        QtyColumnWidth = 61, UnitPriceColumnWidth = 72, SupplyColumnWidth = 72, VatColumnWidth = 72, TotalColumnWidth = 72,
        MinBodyWidth = 459,
        SumSize = 12, TotalSize = 16, TotalRule = 1.5f,
        RemarkLine = 0.75f, RemarkLineColor = "#757575", RemarkSize = 11,
        PageNumberSize = 9,
    };

    /// <summary>
    /// 🔴 [2026-11-20 갈래 E⑵ 고침 2 · 큰글씨 시안 정정 §4 R-5] 본문 폭(pt) = 종이 폭 − 좌우 여백(mm · 1mm = 72/25.4pt).
    /// </summary>
    private static float BodyWidthPt(float pageWidthPt, float marginLeftMm, float marginRightMm) =>
        pageWidthPt - (marginLeftMm + marginRightMm) * 72f / 25.4f;

    /// <summary>
    /// 🔴 [고침 2 · 정정 §4 R-4] 이 스타일의 고정 칸이 본문 폭에 다 안 드나 — 참이면 그 스타일 대신 basic 으로 그린다.
    /// </summary>
    /// <remarks>문턱 0(격식 · 간결) = 늘 거짓. 큰글씨 = 459pt.</remarks>
    private static bool IsBodyTooNarrowForStyle(PrintStyle style, float pageWidthPt, float marginLeftMm, float marginRightMm) =>
        style.MinBodyWidth > 0 && BodyWidthPt(pageWidthPt, marginLeftMm, marginRightMm) < style.MinBodyWidth;

    /// <summary>
    /// 🔴 스타일 양식의 머리(매 쪽 반복). basic 은 옛 <see cref="ComposeHeader"/> 를 탄다.
    /// </summary>
    /// <remarks>
    /// 격식 = 맨 위 가운데 제목 → 이중선 → 한 줄에 왼쪽 문서번호 · 일자 / 오른쪽 공급자.
    /// 간결 · 큰글씨 = basic 과 같은 배치 · 크기와 선만 다르다(시안 §2-1).
    /// 🔴 새 간격 1곳: 격식 이중선 아래 머리 줄 위 8pt(basic 머리 선 위 간격과 같은 값 · 시안에 값 없음).
    /// </remarks>
    private static void ComposeHeaderStyled(IContainer e, DocumentSnapshot d, CompanyInfo c, PrintStyle s)
    {
        e.Column(col =>
        {
            if (s.TitleCentered)
            {
                StyledTitle(col.Item().AlignCenter(), d, s);
                col.Item().PaddingTop(8).LineHorizontal(s.RuleWidth).LineColor(s.RuleColor);
                if (s.RuleSecondWidth > 0)
                    col.Item().PaddingTop(s.RuleSecondGap).LineHorizontal(s.RuleSecondWidth).LineColor(s.RuleColor);
                col.Item().PaddingTop(8).Row(r =>
                {
                    r.RelativeItem().Column(left => StyledDocMeta(left, d, s, withTitle: false));
                    r.RelativeItem().AlignRight().Column(right => StyledSupplier(right, c, s));
                });
            }
            else
            {
                col.Item().Row(r =>
                {
                    r.RelativeItem().Column(left => StyledDocMeta(left, d, s, withTitle: true));
                    r.RelativeItem().AlignRight().Column(right => StyledSupplier(right, c, s));
                });
                col.Item().PaddingTop(8).LineHorizontal(s.RuleWidth).LineColor(s.RuleColor);
            }
        });
    }

    private static void StyledTitle(IContainer x, DocumentSnapshot d, PrintStyle s)
    {
        var title = x.Text(KoLabel(d.DocType));
        title.FontSize(s.TitleSize).Bold();
        if (s.TitleLetterSpacing > 0) title.LetterSpacing(s.TitleLetterSpacing);
        // ⬛ 종전: if (s.TitleFontFamily is not null) title.FontFamily(s.TitleFontFamily);
        // 🔴 [고침 3] 글꼴을 쓰기 전에 내장 글꼴 등록을 확인한다(처음 1번) — 시험의 반사 길도 실제 글꼴로 그린다.
        //   제품 길은 `RenderDocumentAsync` 가 먼저 확인해 실패면 이 칸을 비워 보낸다 ⇒ 여기서 던질 일 0.
        if (s.TitleFontFamily is not null)
        {
            EnsureEmbeddedFontsRegistered();
            title.FontFamily(s.TitleFontFamily);
        }
    }

    private static void StyledDocMeta(ColumnDescriptor left, DocumentSnapshot d, PrintStyle s, bool withTitle)
    {
        if (withTitle)
        {
            StyledTitle(left.Item(), d, s);
            left.Item().PaddingTop(2).Text($"문서번호: {d.DocNo}").FontSize(s.DocMetaSize);
        }
        else
        {
            left.Item().Text($"문서번호: {d.DocNo}").FontSize(s.DocMetaSize);
        }
        left.Item().Text($"일자: {d.DocDate:yyyy-MM-dd}").FontSize(s.DocMetaSize);
    }

    private static void StyledSupplier(ColumnDescriptor right, CompanyInfo c, PrintStyle s)
    {
        right.Item().Text(c.CompanyName).FontSize(s.CompanyNameSize).Bold();
        if (!string.IsNullOrWhiteSpace(c.BusinessNo))
            right.Item().Text($"사업자: {c.BusinessNo}").FontSize(s.CompanyIdSize);
        if (!string.IsNullOrWhiteSpace(c.RepresentativeName))
            right.Item().Text($"대표: {c.RepresentativeName}").FontSize(s.CompanyIdSize);
        if (!string.IsNullOrWhiteSpace(c.Address))
            right.Item().Text(c.Address).FontSize(s.CompanyContactSize);
        if (!string.IsNullOrWhiteSpace(c.Phone))
            right.Item().Text($"TEL: {c.Phone}").FontSize(s.CompanyContactSize);
    }

    /// <summary>
    /// 🔴 스타일 양식의 본문(거래처 · 품목표 · 합계 · 비고). basic 은 옛 <see cref="ComposeBody"/> 를 탄다.
    /// </summary>
    /// <remarks>
    /// 🔴 칸 순서 · 항목 · 열 너비(고정 칸) · 숫자 서식(`N1` · `N0`)은 basic 과 같다 — 바뀌는 것은 글자 크기 · 선 · 면 · 안쪽뿐(시안 §0).
    /// 🔴 ⬛ 정정(고침 2 · 큰글씨 시안 정정 §2 · §5): <b>큰글씨만</b> 열 너비 · 숫자 칸 안쪽이 따로다(묶음 값) — 칸 순서 · 항목 · 숫자 서식은 4종 공통.
    /// 🔴 <c>show_border=0</c>(시안 :109 「4종 모두 지금 규칙 그대로」) = basic 이 끄는 자리와 같은 자리만 끈다 —
    ///   거래처 선(간결 = 표제 밑줄) · 표 선(바깥 · 머리 · 본문) 0 · 면(머리 · 줄무늬) 끔 · 칸 안쪽 +1.
    ///   머리 아래 선 · 총 합계 위 선 · 비고 선은 basic 처럼 그대로 둔다.
    /// 🔴 새 간격 2곳(시안에 값 없음): 총 합계 위 선 아래 2pt · 간결 거래처 표제 밑줄 위 2pt — 글자와 선이 붙지 않게.
    /// </remarks>
    private static void ComposeBodyStyled(IContainer e, DocumentSnapshot d, bool showBorder, PrintStyle s)
    {
        var plus = showBorder ? 0f : 1f;
        var padV = s.CellPadVertical + plus;
        var padH = s.CellPadHorizontal + plus;
        // 🔴 [고침 2] 숫자 칸(오른쪽 맞춤 5칸 · 머리 포함) 좌우 안쪽 — 묶음이 비우면 글자 칸과 같다(격식 · 간결 무변).
        var padHNum = (s.NumberCellPadHorizontal ?? s.CellPadHorizontal) + plus;

        e.PaddingTop(10).Column(col =>
        {
            // 거래처
            var partner = col.Item().PaddingBottom(8);
            partner = s.PartnerBoxed
                ? partner.Border(showBorder ? s.PartnerLine : 0f).BorderColor(s.PartnerLineColor).Padding(s.PartnerPad)
                : partner.PaddingVertical(s.PartnerPad);
            partner.Column(p =>
            {
                var label = p.Item();
                if (!s.PartnerBoxed && showBorder)
                    label = label.BorderBottom(s.PartnerLine).BorderColor(s.PartnerLineColor).PaddingBottom(2);
                label.Text("[ 거래처 정보 ]").FontSize(s.PartnerLabelSize).Bold();
                p.Item().PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text($"상호: {d.PartnerName}").FontSize(s.PartnerNameSize);
                    if (!string.IsNullOrWhiteSpace(d.PartnerBusinessNo))
                        r.RelativeItem().AlignRight().Text($"사업자: {d.PartnerBusinessNo}").FontSize(s.PartnerOtherSize);
                });
                if (!string.IsNullOrWhiteSpace(d.PartnerAddress))
                    p.Item().Text($"주소: {d.PartnerAddress}").FontSize(s.PartnerOtherSize);
                if (!string.IsNullOrWhiteSpace(d.PartnerContact))
                    p.Item().Text($"담당: {d.PartnerContact}").FontSize(s.PartnerOtherSize);
            });

            // 품목표
            var tableBox = col.Item();
            if (showBorder && s.TableOuterLine > 0)
            {
                tableBox = s.TableOuterTopBottomOnly
                    ? tableBox.BorderHorizontal(s.TableOuterLine).BorderColor(s.TableOuterColor)
                    : tableBox.Border(s.TableOuterLine).BorderColor(s.TableOuterColor);
            }
            tableBox.Table(tbl =>
            {
                tbl.ColumnsDefinition(cd =>
                {
                    // 🔴 [고침 2 · 정정 §2-1] 열 너비 = 묶음 값(기본값 = 아래 옛 숫자 그대로 · 큰글씨만 따로).
                    // ⬛ 종전: cd.ConstantColumn(30);                    // 순번
                    cd.ConstantColumn(s.SeqColumnWidth);      // 순번
                    cd.RelativeColumn(s.ItemNameRelative);    // 품명
                    // ⬛ 종전: cd.RelativeColumn(s.SpecRelative);        // 규격
                    if (s.SpecColumnWidth is float specWidth)
                        cd.ConstantColumn(specWidth);         // 규격(큰글씨 = 고정 51)
                    else
                        cd.RelativeColumn(s.SpecRelative);    // 규격
                    // ⬛ 종전: cd.ConstantColumn(60);                    // 수량
                    // ⬛ 종전: cd.ConstantColumn(80);                    // 단가
                    // ⬛ 종전: cd.ConstantColumn(90);                    // 공급가
                    // ⬛ 종전: cd.ConstantColumn(70);                    // 부가세
                    // ⬛ 종전: cd.ConstantColumn(90);                    // 합계
                    cd.ConstantColumn(s.QtyColumnWidth);      // 수량
                    cd.ConstantColumn(s.UnitPriceColumnWidth); // 단가
                    cd.ConstantColumn(s.SupplyColumnWidth);   // 공급가
                    cd.ConstantColumn(s.VatColumnWidth);      // 부가세
                    cd.ConstantColumn(s.TotalColumnWidth);    // 합계
                });
                tbl.Header(h =>
                {
                    Head(h.Cell(), "순", false);
                    Head(h.Cell(), "품명", false);
                    Head(h.Cell(), "규격", false);
                    Head(h.Cell(), "수량", true);
                    Head(h.Cell(), "단가", true);
                    Head(h.Cell(), "공급가액", true);
                    Head(h.Cell(), "부가세", true);
                    Head(h.Cell(), "합계", true);
                });
                var seq = 0;
                foreach (var line in d.Lines)
                {
                    seq++;
                    var even = seq % 2 == 0;
                    Body(tbl.Cell(), even).AlignCenter().Text($"{seq}").FontSize(s.BodyTextSize);
                    Body(tbl.Cell(), even).Text(line.ItemName).FontSize(s.BodyTextSize);   // 줄 수 제한 0 = 잘림 0
                    Body(tbl.Cell(), even).Text(line.Spec ?? "").FontSize(s.BodyTextSize);
                    // 🔴 [고침 2] 숫자 5칸 = 숫자 칸 안쪽(`number: true`) · 글자 · 서식 · 맞춤 무변.
                    // ⬛ 종전: Body(tbl.Cell(), even).AlignRight().Text(line.Qty.ToString("N1")).FontSize(s.BodyTextSize);
                    // ⬛ 종전: Body(tbl.Cell(), even).AlignRight().Text(line.UnitPrice.ToString("N0")).FontSize(s.BodyTextSize);
                    // ⬛ 종전: Body(tbl.Cell(), even).AlignRight().Text(line.Supply.ToString("N0")).FontSize(s.BodyTextSize);
                    // ⬛ 종전: Body(tbl.Cell(), even).AlignRight().Text(line.Vat.ToString("N0")).FontSize(s.BodyTextSize);
                    // ⬛ 종전: Body(tbl.Cell(), even).AlignRight().Text(line.Total.ToString("N0")).FontSize(s.BodyTextSize);
                    Body(tbl.Cell(), even, number: true).AlignRight().Text(line.Qty.ToString("N1")).FontSize(s.BodyTextSize);
                    Body(tbl.Cell(), even, number: true).AlignRight().Text(line.UnitPrice.ToString("N0")).FontSize(s.BodyTextSize);
                    Body(tbl.Cell(), even, number: true).AlignRight().Text(line.Supply.ToString("N0")).FontSize(s.BodyTextSize);
                    Body(tbl.Cell(), even, number: true).AlignRight().Text(line.Vat.ToString("N0")).FontSize(s.BodyTextSize);
                    Body(tbl.Cell(), even, number: true).AlignRight().Text(line.Total.ToString("N0")).FontSize(s.BodyTextSize);
                }
                if (d.Lines.Count == 0)
                {
                    Body(tbl.Cell().ColumnSpan(8), false).AlignCenter().Text("(품목 정보 없음)").FontSize(s.BodyTextSize);
                }
            });

            // 합계
            col.Item().PaddingTop(8).AlignRight().Column(p =>
            {
                p.Item().Text($"공급가액 합계: {d.TotalSupply:N0}").FontSize(s.SumSize);
                p.Item().Text($"부가세 합계  : {d.TotalVat:N0}").FontSize(s.SumSize);
                var total = p.Item().PaddingTop(4);
                if (s.TotalRule > 0)
                    total = total.BorderTop(s.TotalRule).BorderColor(StyleInk).PaddingTop(2);
                total.Text($"총 합계: {d.TotalAmount:N0} 원").FontSize(s.TotalSize).Bold();
            });

            // 비고
            if (!string.IsNullOrWhiteSpace(d.Remark))
            {
                var remark = col.Item().PaddingTop(10);
                remark = s.RemarkTopOnly ? remark.BorderTop(s.RemarkLine) : remark.Border(s.RemarkLine);
                remark.BorderColor(s.RemarkLineColor).Padding(6).Column(p =>
                {
                    p.Item().Text("비고").FontSize(s.RemarkSize).SemiBold();
                    p.Item().Text(d.Remark).FontSize(s.RemarkSize);
                });
            }
        });

        void Head(IContainer cell, string text, bool alignRight)
        {
            var x = cell;
            if (showBorder && s.HeadBackground is not null) x = x.Background(s.HeadBackground);
            var w = showBorder ? s.HeadLine : 0f;
            x = s.HeadLineBottomOnly ? x.BorderBottom(w) : x.Border(w);
            // ⬛ 종전: x = x.BorderColor(s.HeadLineColor).PaddingVertical(padV).PaddingHorizontal(padH);
            // 🔴 [고침 2] 오른쪽 맞춤 머리 = 숫자 칸 → 숫자 칸 안쪽.
            x = x.BorderColor(s.HeadLineColor).PaddingVertical(padV).PaddingHorizontal(alignRight ? padHNum : padH);
            if (alignRight) x = x.AlignRight();
            x.Text(text).FontSize(s.HeadTextSize).Bold();
        }

        // ⬛ 종전: IContainer Body(IContainer cell, bool even)
        // 🔴 [고침 2] `number` = 숫자 칸(숫자 칸 안쪽) · 기본 거짓 = 글자 칸(순 · 품명 · 규격 · 「품목 정보 없음」).
        IContainer Body(IContainer cell, bool even, bool number = false)
        {
            var x = cell;
            if (showBorder && even && s.ZebraEvenBackground is not null) x = x.Background(s.ZebraEvenBackground);
            var w = showBorder ? s.BodyLine : 0f;
            x = s.BodyLineHorizontalOnly ? x.BorderBottom(w) : x.Border(w);
            // ⬛ 종전: return x.BorderColor(s.BodyLineColor).PaddingVertical(padV).PaddingHorizontal(padH);
            return x.BorderColor(s.BodyLineColor).PaddingVertical(padV).PaddingHorizontal(number ? padHNum : padH);
        }
    }

    // ─── DB lookup ─────────────────────────────────────

    /// <summary>
    /// 🔴 <b>이 미리보기를 막아야 하나</b> — 3차검증 R-11 의 판정 한 자리.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 함수로 떼어 놨나</b> — 이 판정은 <b>DB 없이도 재야 한다.</b> 안에 박아 두면
    ///   「DB 가 붙은 환경에서만 잴 수 있다」가 되고, 그러면 게이트가 또 비어 있게 된다.
    ///   그래서 <b>인자 두 개로 떼어</b> 네 경우를 표로 잰다.
    /// <para>
    /// 🟢 <c>RequireExisting=false</c>(「인쇄」·「이메일」·「팩스」) → <b>절대 막지 않는다.</b>
    ///    종전처럼 안내문 종이가 나간다. 여기서 막으면 전표가 안 나가 P0 다(#20).
    /// 🟢 <c>RequireExisting=true</c> + 못 찾음 → <b>막는다.</b> 백지를 보여주지 않는다.
    /// 🟢 예시 자료(<c>UseSampleData</c>)는 애초에 DB 를 안 보므로 <b>항상 통과</b>다 —
    ///    양식정보설정 미리보기가 이 문에 걸리면 안 된다.
    /// </para>
    /// </remarks>
    internal static bool ShouldRejectMissing(PdfPreviewOverrides? overrides, bool found)
    {
        if (overrides?.RequireExisting != true) return false;
        if (overrides.UseSampleData) return false;
        return !found;
    }
    private async Task<DocumentSnapshot> LoadDocumentAsync(string tenantId, string docType, string docId, CancellationToken ct)
    {
        // 🔴 문서별 SQL 이 다르다. 6종 → **8종**(결재 ㉮ — 매입·매출반품 로더 추가 20261024작).
        //   여기 없는 종은 아래 fallback 이 **빈 종이**를 돌려준다. 그래서 화면에
        //   「인쇄」 단추를 달 때는 **이 표에 있는지 먼저 본다** — 없으면 고객이 백지를 받는다.
        return docType switch
        {
            "quotation" => await LoadQuotationAsync(tenantId, docId, ct).ConfigureAwait(false),
            "sales_order" => await LoadSalesOrderAsync(tenantId, docId, ct).ConfigureAwait(false),
            "delivery" => await LoadDeliveryAsync(tenantId, docId, ct).ConfigureAwait(false),
            "tax_invoice" => await LoadTaxInvoiceAsync(tenantId, docId, ct).ConfigureAwait(false),
            "purchase_order" => await LoadPurchaseOrderAsync(tenantId, docId, ct).ConfigureAwait(false),
            "purchase_receipt" => await LoadPurchaseReceiptAsync(tenantId, docId, ct).ConfigureAwait(false),
            "purchase_return" => await LoadPurchaseReturnAsync(tenantId, docId, ct).ConfigureAwait(false),
            "sales_return" => await LoadSalesReturnAsync(tenantId, docId, ct).ConfigureAwait(false),
            // 🔴 로더가 없는 종 — 이 종이는 **백지**다. Found = false 로 올려 미리보기가 막는다(R-11).
            _ => new DocumentSnapshot { DocType = docType, DocNo = docId, Found = false }
        };
    }

    private async Task<DocumentSnapshot> LoadQuotationAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합(13차 후순위→봉합): quotations 컬럼 DDL 정합(헌법 #13·#36).
        // quotation_no→quote_no, quotation_date→quote_date, quotation_id→quote_id,
        // total_supply→total_amount(공급가), total_vat→vat_amount, total_amount=공급가+부가세, remark→memo.
        // quotation_items: item_name 없음→items JOIN, seq 없음→sort_order, supply는 amount, total=amount+vat.
        const string head = """
            SELECT q.quote_no AS DocNo, q.quote_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(q.total_amount,0) AS TotalSupply, COALESCE(q.vat_amount,0) AS TotalVat,
                   COALESCE(q.total_amount,0) + COALESCE(q.vat_amount,0) AS TotalAmount,
                   q.memo AS Remark
            FROM quotations q LEFT JOIN partners p ON p.partner_id = q.partner_id AND p.tenant_id = q.tenant_id
            WHERE q.tenant_id = @TenantId AND q.quote_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(qi.qty,0) AS Qty, COALESCE(qi.unit_price,0) AS UnitPrice,
                   COALESCE(qi.amount,0) AS Supply, COALESCE(qi.vat_amount,0) AS Vat,
                   COALESCE(qi.amount,0) + COALESCE(qi.vat_amount,0) AS Total
            FROM quotation_items qi LEFT JOIN items i ON i.item_id = qi.item_id
            WHERE qi.quote_id = @Id ORDER BY qi.sort_order
            """;
        return await LoadByQueriesAsync(tenantId, id, "quotation", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadSalesOrderAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합: sales_orders 컬럼 DDL 정합. so_no→order_no, so_date→order_date, so_id→order_id,
        // total_supply→total_amount(공급가), total_vat→vat_amount, remark→memo.
        // sales_order_items: item_name 없음→items JOIN, qty→ordered_qty, supply_amount 존재, seq 없음→정렬 PK.
        const string head = """
            SELECT s.order_no AS DocNo, s.order_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(s.total_amount,0) AS TotalSupply, COALESCE(s.vat_amount,0) AS TotalVat,
                   COALESCE(s.total_amount,0) + COALESCE(s.vat_amount,0) AS TotalAmount,
                   s.memo AS Remark
            FROM sales_orders s LEFT JOIN partners p ON p.partner_id = s.partner_id AND p.tenant_id = s.tenant_id
            WHERE s.tenant_id = @TenantId AND s.order_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(soi.ordered_qty,0) AS Qty, COALESCE(soi.unit_price,0) AS UnitPrice,
                   COALESCE(soi.supply_amount,0) AS Supply, COALESCE(soi.vat_amount,0) AS Vat,
                   COALESCE(soi.supply_amount,0) + COALESCE(soi.vat_amount,0) AS Total
            FROM sales_order_items soi LEFT JOIN items i ON i.item_id = soi.item_id
            WHERE soi.tenant_id = @TenantId AND soi.order_id = @Id ORDER BY soi.order_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "sales_order", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadDeliveryAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합: sales_deliveries total_supply→total_amount(공급가), total_vat→vat_amount, remark→memo.
        // sales_delivery_items: item_name 없음→items JOIN, qty 존재, supply_amount 존재, seq 없음→정렬 PK.
        const string head = """
            SELECT d.delivery_no AS DocNo, d.delivery_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(d.total_amount,0) AS TotalSupply, COALESCE(d.vat_amount,0) AS TotalVat,
                   COALESCE(d.total_amount,0) + COALESCE(d.vat_amount,0) AS TotalAmount,
                   d.memo AS Remark
            FROM sales_deliveries d LEFT JOIN partners p ON p.partner_id = d.partner_id AND p.tenant_id = d.tenant_id
            WHERE d.tenant_id = @TenantId AND d.delivery_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(di.qty,0) AS Qty, COALESCE(di.unit_price,0) AS UnitPrice,
                   COALESCE(di.supply_amount,0) AS Supply, COALESCE(di.vat_amount,0) AS Vat,
                   COALESCE(di.supply_amount,0) + COALESCE(di.vat_amount,0) AS Total
            FROM sales_delivery_items di LEFT JOIN items i ON i.item_id = di.item_id
            WHERE di.tenant_id = @TenantId AND di.delivery_id = @Id ORDER BY di.delivery_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "delivery", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadTaxInvoiceAsync(string tenantId, string id, CancellationToken ct)
    {
        // 🔴 20261021작 절 B-2 — 열쇠 교정.
        //   종전: WHERE t.invoice_id = @Id. 그런데 화면·모바일·이메일이 주는 값은 모두
        //   **거래명세서 번호(delivery_id)** 다(설계 §3-1) ⇒ 영영 못 찾아 「(거래처 정보 없음)」
        //   빈 종이가 메일로 나갔다. 이제 delivery_id 로 찾는다.
        //   🔴 품목 정본은 거래명세서다. tax_invoice_items 는 발행이 채우지 않는다(설계 §13 D-3).
        //
        //   컬럼명 DDL 정합(헌법 #13·#36) — 2026-10-20 information_schema 실측:
        //   tax_invoices(invoice_no·issued_at·amendment_reason·hometax_approval_no·remark1·status),
        //   sales_deliveries(delivery_no·delivery_date·total_amount·vat_amount·memo·is_deleted),
        //   partners(partner_name·biz_no·ceo_name·biz_type·biz_item·tel·address·manager_name).
        const string head = """
            SELECT d.delivery_no AS DocNo, d.delivery_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   p.ceo_name AS PartnerCeoName, p.biz_type AS PartnerBizType,
                   p.biz_item AS PartnerBizItem, p.tel AS PartnerPhone,
                   COALESCE(d.total_amount,0) AS TotalSupply, COALESCE(d.vat_amount,0) AS TotalVat,
                   COALESCE(d.total_amount,0) + COALESCE(d.vat_amount,0) AS TotalAmount,
                   d.memo AS Remark
            FROM sales_deliveries d
            LEFT JOIN partners p ON p.partner_id = d.partner_id AND p.tenant_id = d.tenant_id
            WHERE d.tenant_id = @TenantId AND d.delivery_id = @Id AND d.is_deleted = 0
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(di.qty,0) AS Qty, COALESCE(di.unit_price,0) AS UnitPrice,
                   COALESCE(di.supply_amount,0) AS Supply, COALESCE(di.vat_amount,0) AS Vat,
                   COALESCE(di.supply_amount,0) + COALESCE(di.vat_amount,0) AS Total
            FROM sales_delivery_items di LEFT JOIN items i ON i.item_id = di.item_id
            WHERE di.tenant_id = @TenantId AND di.delivery_id = @Id ORDER BY di.delivery_item_id
            """;

        var snap = await LoadByQueriesAsync(tenantId, id, "tax_invoice", head, lines, ct)
            .ConfigureAwait(false);

        // 발행된 계산서가 있으면 머리값만 덮는다.
        //   🔴 status='issued' 만 — 취소본(canceled)이 종이로 나가면 안 된다(설계 §13 D-6).
        //   🔴 미발행이면 행이 없다 ⇒ 거래명세서 값 그대로 그린다(승인번호 빈칸). 스텁으로 안 빠진다.
        const string invoiceHead = """
            SELECT t.invoice_no AS InvoiceNo, t.issued_at AS IssuedAt,
                   t.amendment_reason AS AmendmentReason,
                   t.hometax_approval_no AS HometaxApprovalNo,
                   t.remark1 AS Remark1
            FROM tax_invoices t
            WHERE t.tenant_id = @TenantId AND t.delivery_id = @Id AND t.status = 'issued'
            ORDER BY t.issued_at DESC
            LIMIT 1
            """;
        // 🔴 유일성이 없다 — delivery_id 는 UNIQUE 가 아니라 그냥 KEY 다(출하 DDL).
        //   더블클릭 한 번으로 issued 2행이 되면 QuerySingleOrDefault 는 그 전표를
        //   **영구 500** 으로 만든다 ⇒ QueryFirstOrDefault + ORDER BY ... LIMIT 1.
        try
        {
            var inv = await _db.QueryFirstOrDefaultAsync<TaxInvoiceHead>(
                new CommandDefinition(invoiceHead, new { TenantId = tenantId, Id = id }, cancellationToken: ct))
                .ConfigureAwait(false);
            if (inv is not null)
            {
                if (!string.IsNullOrWhiteSpace(inv.InvoiceNo)) snap.DocNo = inv.InvoiceNo;
                snap.DocDate = inv.IssuedAt;
                snap.AmendmentReason = inv.AmendmentReason;
                snap.HometaxApprovalNo = inv.HometaxApprovalNo;
                if (!string.IsNullOrWhiteSpace(inv.Remark1)) snap.Remark = inv.Remark1;
            }
            else
            {
                // 🔴 발행본이 없는데 취소본이 있으면, 그 전표는 **취소된 계산서**다.
                //   취소행은 목록에 남아 체크된다 ⇒ 막지 않으면 취소본이 「유효한 법정 서식」
                //   모양으로 거래처에 나간다. 🔴 인쇄 경로에서만 막는다 —
                //   발행·취소 로직(TaxInvoiceService)은 한 줄도 안 건드린다.
                const string canceledSql = """
                    SELECT 1 FROM tax_invoices
                    WHERE tenant_id = @TenantId AND delivery_id = @Id AND status = 'canceled'
                    LIMIT 1
                    """;
                var canceled = await _db.QueryFirstOrDefaultAsync<int?>(
                    new CommandDefinition(canceledSql, new { TenantId = tenantId, Id = id }, cancellationToken: ct))
                    .ConfigureAwait(false);
                snap.IsCanceledInvoice = canceled.HasValue;
            }
        }
        catch (Exception ex)
        {
            // 빈 catch 금지(#15). 계산서 머리를 못 덮어도 거래명세서 값으로는 그린다.
            _logger.LogWarning(ex, "[PDF] 세금계산서 머리 조회 실패 — 거래명세서 값으로 그린다. id={Id}", id);
        }

        return snap;
    }

    /// <summary>발행된 세금계산서의 머리값 — 거래명세서 위에 덮는다(20261021작 절 B-2).</summary>
    private sealed class TaxInvoiceHead
    {
        public string? InvoiceNo { get; set; }
        public DateTime IssuedAt { get; set; }
        public string? AmendmentReason { get; set; }
        public string? HometaxApprovalNo { get; set; }
        public string? Remark1 { get; set; }
    }

    private async Task<DocumentSnapshot> LoadPurchaseOrderAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합: purchase_orders total_supply→total_amount(공급가), total_vat→vat_amount, remark→memo.
        // purchase_order_items: item_name 없음→items JOIN, qty→ordered_qty, supply_amount 존재, seq 없음→정렬 PK.
        const string head = """
            SELECT po.po_no AS DocNo, po.po_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(po.total_amount,0) AS TotalSupply, COALESCE(po.vat_amount,0) AS TotalVat,
                   COALESCE(po.total_amount,0) + COALESCE(po.vat_amount,0) AS TotalAmount,
                   po.memo AS Remark
            FROM purchase_orders po LEFT JOIN partners p ON p.partner_id = po.partner_id AND p.tenant_id = po.tenant_id
            WHERE po.tenant_id = @TenantId AND po.po_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(poi.ordered_qty,0) AS Qty, COALESCE(poi.unit_price,0) AS UnitPrice,
                   COALESCE(poi.supply_amount,0) AS Supply, COALESCE(poi.vat_amount,0) AS Vat,
                   COALESCE(poi.supply_amount,0) + COALESCE(poi.vat_amount,0) AS Total
            FROM purchase_order_items poi LEFT JOIN items i ON i.item_id = poi.item_id
            WHERE poi.tenant_id = @TenantId AND poi.po_id = @Id ORDER BY poi.po_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "purchase_order", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadPurchaseReceiptAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합: purchase_receipts total_supply→total_amount(공급가), total_vat→vat_amount, remark→memo.
        // purchase_receipt_items: item_name 없음→items JOIN, qty 존재, supply_amount 존재, seq 없음→정렬 PK.
        const string head = """
            SELECT pr.receipt_no AS DocNo, pr.receipt_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(pr.total_amount,0) AS TotalSupply, COALESCE(pr.vat_amount,0) AS TotalVat,
                   COALESCE(pr.total_amount,0) + COALESCE(pr.vat_amount,0) AS TotalAmount,
                   pr.memo AS Remark
            FROM purchase_receipts pr LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.receipt_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(pri.qty,0) AS Qty, COALESCE(pri.unit_price,0) AS UnitPrice,
                   COALESCE(pri.supply_amount,0) AS Supply, COALESCE(pri.vat_amount,0) AS Vat,
                   COALESCE(pri.supply_amount,0) + COALESCE(pri.vat_amount,0) AS Total
            FROM purchase_receipt_items pri LEFT JOIN items i ON i.item_id = pri.item_id
            WHERE pri.tenant_id = @TenantId AND pri.receipt_id = @Id ORDER BY pri.receipt_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "purchase_receipt", head, lines, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 🔴 매입반품 자료 로더 — 결재 ㉮(20261021작 §19-1 · 백지 4종을 로더까지 이번 차수).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 이번에 붙이나</b> — 10/24 차수가 <b>반품처리 화면에 「인쇄」 단추를 달았다.</b>
    ///   로더가 없으면 그 단추가 <b>빈 종이</b>를 띄운다(미리보기가 곧 인쇄물이므로 종이도 백지).
    ///   「없는 종」이 아니라 <b>고객이 누를 수 있는 단추</b>가 되는 순간 백지는 사고다.
    /// 🔴 칸 이름은 출하 DDL <c>installer/hitpan_db_clean.sql</c> 을 보고 썼다(#13·#36).
    ///   <c>purchase_returns.total_amount</c> 는 <b>공급가</b>다 — 다른 5종과 같은 규약.
    ///   품목 이름 칸이 없어 <c>items</c> 를 JOIN 한다(나머지 로더와 동일).
    /// 🔴 2026-11-26 HQA ⑥ — 품목 조인에 상세행 회사 조건 더함 · 옛 글자 = <c>... LEFT JOIN items i ON i.item_id = pri.item_id</c>
    /// </remarks>
    private async Task<DocumentSnapshot> LoadPurchaseReturnAsync(string tenantId, string id, CancellationToken ct)
    {
        const string head = """
            SELECT pr.return_no AS DocNo, pr.return_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(pr.total_amount,0) AS TotalSupply, COALESCE(pr.vat_amount,0) AS TotalVat,
                   COALESCE(pr.total_amount,0) + COALESCE(pr.vat_amount,0) AS TotalAmount,
                   pr.memo AS Remark
            FROM purchase_returns pr LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.return_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(pri.qty,0) AS Qty, COALESCE(pri.unit_price,0) AS UnitPrice,
                   COALESCE(pri.supply_amount,0) AS Supply, COALESCE(pri.vat_amount,0) AS Vat,
                   COALESCE(pri.supply_amount,0) + COALESCE(pri.vat_amount,0) AS Total
            FROM purchase_return_items pri LEFT JOIN items i ON i.item_id = pri.item_id
            WHERE pri.tenant_id = @TenantId AND pri.return_id = @Id ORDER BY pri.return_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "purchase_return", head, lines, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 🔴 매출반품 자료 로더 — 결재 ㉮. 사장님 반려 ③ 의 「반품처리」는 매입·매출 <b>둘 다</b>다.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>sales_return_items.unit_price</c> 는 <b>반품 당시 환불 단가</b>이고
    ///   <c>original_unit_price</c> 는 원거래 단가 복사본이다(DDL 주석). 종이에는
    ///   <b>환불 단가</b>를 찍는다 — 고객에게 돌려주는 돈이 그것이다.
    /// 🔴 <c>is_loss</c>(파손 로스)는 재고 반영 축이라 종이에 안 찍는다. 등재만.
    /// 🔴 2026-11-26 HQA ⑥ — 품목 조인에 상세행 회사 조건 더함 · 옛 글자 = <c>... LEFT JOIN items i ON i.item_id = sri.item_id</c>
    /// </remarks>
    private async Task<DocumentSnapshot> LoadSalesReturnAsync(string tenantId, string id, CancellationToken ct)
    {
        const string head = """
            SELECT sr.return_no AS DocNo, sr.return_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(sr.total_amount,0) AS TotalSupply, COALESCE(sr.vat_amount,0) AS TotalVat,
                   COALESCE(sr.total_amount,0) + COALESCE(sr.vat_amount,0) AS TotalAmount,
                   sr.memo AS Remark
            FROM sales_returns sr LEFT JOIN partners p ON p.partner_id = sr.partner_id AND p.tenant_id = sr.tenant_id
            WHERE sr.tenant_id = @TenantId AND sr.return_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(sri.qty,0) AS Qty, COALESCE(sri.unit_price,0) AS UnitPrice,
                   COALESCE(sri.supply_amount,0) AS Supply, COALESCE(sri.vat_amount,0) AS Vat,
                   COALESCE(sri.supply_amount,0) + COALESCE(sri.vat_amount,0) AS Total
            FROM sales_return_items sri LEFT JOIN items i ON i.item_id = sri.item_id
            WHERE sri.tenant_id = @TenantId AND sri.return_id = @Id ORDER BY sri.return_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "sales_return", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadByQueriesAsync(string tenantId, string id, string docType, string headSql, string lineSql, CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        DocumentSnapshot? h = null;
        try
        {
            h = await _db.QuerySingleOrDefaultAsync<DocumentSnapshot>(
                new CommandDefinition(headSql, new { TenantId = tenantId, Id = id }, cancellationToken: ct))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 신규 ERP 테이블/컬럼명이 가정과 다르면 스키마 차이일 수 있음 — 안내문서 fallback
            _logger.LogWarning(ex, "[PDF] {Type} head 조회 실패 — fallback. id={Id}", docType, id);
        }
        if (h is null)
        {
            // 🔴 Found = false — 미리보기는 이걸 보고 **백지를 띄우지 않는다**(3차검증 R-11).
            //   「인쇄」·「이메일」의 안내문 종이 동작은 **한 글자도 안 바뀐다**(#1).
            return new DocumentSnapshot { DocType = docType, DocNo = id, DocDate = DateTime.Today, PartnerName = "(원본 문서를 찾을 수 없습니다)", Found = false };
        }
        h.DocType = docType;
        try
        {
            var ls = await _db.QueryAsync<DocumentLine>(
                new CommandDefinition(lineSql, new { TenantId = tenantId, Id = id }, cancellationToken: ct))
                .ConfigureAwait(false);
            h.Lines = ls.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] {Type} 라인 조회 실패 — 빈 라인. id={Id}", docType, id);
            h.Lines = new();
        }
        return h;
    }

    private async Task<CompanyInfo> LoadCompanyAsync(string tenantId, CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = """
            SELECT company_name AS CompanyName, biz_no AS BusinessNo,
                   ceo_name AS RepresentativeName, address AS Address, tel AS Phone,
                   -- 🔴 20261021작 절 B-2 — 세금계산서 법정 서식 칸. 칸 추가만, 삭제 0.
                   biz_type AS BizType, biz_item AS BizItem, seal_url AS SealUrl,
                   -- 🔴 20261026작1 (DB-148) — 워터마크의 재료다. 안 읽으면 고객이 로고를
                   --   올리고 워터마크를 켜도 **종이에 아무 일이 안 일어난다.**
                   logo_url AS LogoUrl
            FROM local_company WHERE tenant_id = @TenantId
            """;
        try
        {
            var c = await _db.QuerySingleOrDefaultAsync<CompanyInfo>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
                .ConfigureAwait(false);
            return c ?? new CompanyInfo { CompanyName = "(회사 정보 없음)" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] local_company 조회 실패");
            return new CompanyInfo { CompanyName = "(회사 정보 없음)" };
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State != ConnectionState.Open && _db is DbConnection dc)
            await dc.OpenAsync(ct).ConfigureAwait(false);
    }

    // ─── 내부 모델 ─────────────────────────────────────
    private sealed class DocumentSnapshot
    {

        /// <summary>
        /// 🔴 <b>DB 에서 실제로 찾았나</b> — 못 찾았으면 <c>false</c>(2026-10-24 3차검증 R-11).
        /// </summary>
        /// <remarks>
        /// 🔴 <b>왜 칸을 더하나</b> — 못 찾으면 종전에도 <b>빈 스냅샷</b>을 돌려줬다.
        ///   그 종이는 「인쇄」·「이메일」 쪽에서는 <b>안내문 종이</b>로 쓰여 왔으므로 그대로 둔다(#1).
        ///   그런데 <b>미리보기</b>에서는 그게 <b>백지</b>로 보인다. 저장 안 한 전표의
        ///   <c>_draft.Id</c> 는 화면이 만든 GUID 라 DB 에 없고, 창문은 「비었나」만 봤다
        ///   ⇒ GUID 는 통과하고 <b>백지가 떴다</b>. 「찾았나」를 <b>글자로 추측하지 않고</b>
        ///   이 칸으로 들고 올라간다.
        /// </remarks>
        public bool Found { get; set; } = true;
        public string DocType { get; set; } = "";
        public string DocNo { get; set; } = "";
        public DateTime DocDate { get; set; } = DateTime.Today;
        public string PartnerName { get; set; } = "";
        public string? PartnerBusinessNo { get; set; }
        public string? PartnerAddress { get; set; }
        public string? PartnerContact { get; set; }
        public decimal TotalSupply { get; set; }
        public decimal TotalVat { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Remark { get; set; }
        public List<DocumentLine> Lines { get; set; } = new();

        // ── 20261021작 절 B-2 — 세금계산서 법정 서식이 요구하는 칸. **더하기만**(#1).
        //    다른 9종은 이 칸들을 안 읽는다 ⇒ 종이가 안 바뀐다.
        /// <summary>공급받는 자 대표자명.</summary>
        public string? PartnerCeoName { get; set; }
        /// <summary>공급받는 자 업태.</summary>
        public string? PartnerBizType { get; set; }
        /// <summary>공급받는 자 종목.</summary>
        public string? PartnerBizItem { get; set; }
        /// <summary>공급받는 자 전화번호.</summary>
        public string? PartnerPhone { get; set; }
        /// <summary>수정사유 — 수정세금계산서일 때만 값이 있다.</summary>
        public string? AmendmentReason { get; set; }
        /// <summary>국세청 승인번호 — 전자발행이 끝난 건에만 값이 있다.</summary>
        public string? HometaxApprovalNo { get; set; }
        /// <summary>취소된 계산서인가 — 참이면 법정 서식 대신 안내문 한 장을 낸다.</summary>
        public bool IsCanceledInvoice { get; set; }
    }

    private sealed class DocumentLine
    {
        public string ItemName { get; set; } = "";
        public string? Spec { get; set; }
        public decimal Qty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Supply { get; set; }
        public decimal Vat { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class CompanyInfo
    {
        public string CompanyName { get; set; } = "";
        public string? BusinessNo { get; set; }
        public string? RepresentativeName { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }

        // ── 20261021작 절 B-2 — 공급자 업태·종목. 법정 서식의 칸이다. **더하기만**(#1).
        /// <summary>공급자 업태.</summary>
        public string? BizType { get; set; }
        /// <summary>공급자 종목.</summary>
        public string? BizItem { get; set; }
        /// <summary>
        /// 인장 경로. 🔴 값이 있으면 이미지를 찍고, <b>비어 있으면 지금의 글자 도장</b>을 그린다.
        /// 지금은 보관소가 없어 항상 비어 있다(설계 §8-2) ⇒ 합격선 ④ 는 이번 차수 <b>미달</b>이다.
        /// 보관소가 생기는 날 <b>코드 수정 0</b> 으로 켜진다.
        /// </summary>
        public string? SealUrl { get; set; }

        /// <summary>
        /// 🔴 회사 로고 경로 — <b>워터마크의 재료</b>다 (DB-148 · 20261026작1).
        /// </summary>
        /// <remarks>
        /// 🔴 비어 있으면 워터마크는 <b>아무 일도 하지 않는다</b> — 켜도 종이가 안 바뀐다.
        ///   고객에게 「로고를 먼저 올려 주십시오」를 화면에서 알려 줘야 한다(갈래 D).
        /// </remarks>
        public string? LogoUrl { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════
    // 세금계산서 전용 작성기 (20261021작 절 B-3)
    //   배치는 PdfExportService.GenerateTaxInvoicePdf 의 법정 배치를 **바탕으로 옮겨 적고**
    //   6요소(수정사유·서명란·비고란·절취선·결제구분·영수/청구)를 보탰다.
    //   🔴 PdfExportService 는 한 줄도 안 지웠다(#1). 나머지 9종은 이 코드를 안 탄다.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>세금계산서 문서타입 키.</summary>
    private const string TaxInvoiceDocType = "tax_invoice";

    /// <summary>격자 머리칸 바탕색.</summary>
    private const string TaxGridBg = "#F0F0F0";

    /// <summary>
    /// 세금계산서 한 벌(또는 두 벌)을 그린다.
    /// </summary>
    /// <remarks>
    /// 🔴 <paramref name="copyMode"/> 는 <c>form_templates.print_copy_mode</c> 다 —
    /// <c>supplier</c>=공급자 보관용 1벌 · <c>recipient</c>=공급받는 자 보관용 1벌 ·
    /// <c>both</c>=두 벌 + 사이 절취선. 🔴 <b>두 벌은 같은 자료 객체 하나로 그린다</b> —
    /// 벌마다 따로 계산하면 양쪽 값이 어긋난다.
    /// 🔴 <b>줄을 자르는 코드는 0건</b>이다. 품목이 넘치면 QuestPDF 가 다음 장으로 이어 찍고,
    /// 열 제목은 <c>table.Header(</c> 로 2쪽에도 다시 나온다.
    /// </remarks>
    private byte[] RenderTaxInvoiceDocument(DocumentSnapshot d, CompanyInfo c, string copyMode,
        bool showTearLine,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        // 🔴🔴 20261021작 §17 T-7 / §20 — 지원 범위를 넘는 여백을 먼저 조인다.
        //   까닭·실측표는 TaxInvoiceMaxMarginMm 주석에 있다. 요지: 0~39mm 는 전건 정상,
        //   첫 고장은 40mm(3모드 전부 `[33,0,7]`), 80mm 이상은 종이가 아예 안 나온다.
        //   🔴 §20: 「40mm 이상에서 깨진다」는 맞았고, 상한 30mm 는 과했다(1mm 실측 = 39mm 까지 정상).
        //   🔴 고객이 저장한 값은 안 건드린다 — 여기서만 조인다.
        marginTop = ClampTaxInvoiceMargin(marginTop, "상", d.DocNo);
        marginLeft = ClampTaxInvoiceMargin(marginLeft, "좌", d.DocNo);
        marginRight = ClampTaxInvoiceMargin(marginRight, "우", d.DocNo);
        marginBottom = ClampTaxInvoiceMargin(marginBottom, "하", d.DocNo);

        var sealImage = TryLoadSealImage(c.SealUrl);
        var drawSupplier = !string.Equals(copyMode, "recipient", StringComparison.Ordinal);
        var drawRecipient = !string.Equals(copyMode, "supplier", StringComparison.Ordinal);

        // 🔴 20261021작 §9-1 봉합(R-1) — 쪽 나눔 판정.
        //   반려 사유: 두 벌을 한 Column 에 그냥 쌓아 두면 QuestPDF 가 **벌 한가운데에서** 쪽을
        //   끊었다. 1쪽 한가운데 절취선 + 그 아래 미완성 종이 ⇒ 자르면 반 토막 계산서.
        //   집행: **두 벌이 한 장에 실제로 들어가는지 먼저 재고**(MeasureBothCopiesFitOnePage),
        //     · 들어가면 → 한 장에 두 벌 + 절취선 (기본 · 사장님 결재)
        //     · 안 들어가면 → 절취선을 빼고 벌마다 제 쪽에서 시작한다(PageBreak)
        //   🔴 품목을 줄이는 코드는 여전히 0건이다.
        //
        // 🔴 20261021작 §10-2 재결재 — 위 §9-1 의 「재서 저절로 정한다」를 사장님이
        //   **고객 설정**으로 바꾸셨다. 한 벌을 11mm 줄이고 나니 두 길이 다 열렸다.
        //     · 켬(ShowTearLine=1) → 한 장에 두 벌 + 자르는 선.
        //         품목이 다 안 들어가면 **마지막 칸에 「외 N건」**(§10-2 ⓓ · 국세청 서식 관행).
        //     · 끔(기본)          → 벌마다 제 쪽에서 시작 · **품목 전부**(한 줄도 안 사라진다).
        //   🔴 재는 코드(MeasureBothCopiesFitOnePage)는 그대로 쓴다 — 줄 수로 눈대중하지 않는다.
        //
        var bothCopies = drawSupplier && drawRecipient;
        var visibleLines = d.Lines.Count;   // 기본은 전부 찍는다.
        var useTearLine = false;

        if (bothCopies && showTearLine)
        {
            var fit = ResolveVisibleLineCount(
                d, c, sealImage, marginTop, marginLeft, marginRight, marginBottom);
            if (fit >= 0)
            {
                useTearLine = true;
                visibleLines = fit;
            }
            else
            {
                // 🔴 품목을 한 줄도 못 넣을 만큼 여백이 크면 **한 장에 모으지 않는다.**
                //   「외 N건」만 덩그러니 남은 계산서를 내느니 쪽을 나누는 편이 낫다.
                _logger.LogWarning(
                    "세금계산서를 한 장에 모으지 못했다 — 쪽을 나눠 품목을 전부 낸다. " +
                    "여백이 너무 크지 않은지 양식정보설정을 확인하시면 됩니다. 문서번호 {DocNo}", d.DocNo);
            }
        }

        // 🔴 안 찍히는 줄 수 — 「외 N건」의 N. 0 이면 「외」 칸 자체가 안 나온다.
        //   게이트가 이 숫자를 그대로 받아 **종이에 안 찍힌 줄 수와 같은지** 검산한다.
        var hiddenLines = Math.Max(0, d.Lines.Count - visibleLines);
        _logger.LogDebug(
            "세금계산서 품목 담기 — 전체 {Total}줄 중 {Visible}줄 찍고 {Hidden}줄은 「외 N건」 " +
            "(한장모으기 {Tear}) 문서번호 {DocNo}",
            d.Lines.Count, visibleLines, hiddenLines, useTearLine, d.DocNo);

        // 🔴🔴 20261021작 §15-6 T-2 — 자르는 선을 안 쓰는 모든 길(끔 · 단일 벌 ·
        //   켰지만 재서 안 들어간 경우)은 벌마다 **이어쓰기**로 그린다.
        //   장마다 머리를 다시 붙이고 꼬리는 마지막 장에만 붙인다
        //   ⇒ 🔴 품목 0줄짜리 꼬리 종이 0건 · 벌 머리 없는 쪽 0건([4] 반려 사유).
        //   🔴 품목은 한 줄도 안 사라진다 — 이 길에 「외 N건」은 없다(§15-5).
        //   🔴 두 벌은 자료가 같으므로 **한 번만 재서** 양쪽에 같은 쪽 나눔을 쓴다.
        //     벌마다 따로 재면 두 벌의 쪽 수가 어긋나 고객이 비교를 못 한다.
        //     재는 라벨은 둘 중 **긴 쪽**(공급받는 자 보관용)으로 잡아 안전하게 본다.
        List<(int Skip, int Take, bool IsLast)>? copyChunks = null;
        if (!useTearLine && !d.IsCanceledInvoice)
        {
            copyChunks = ResolveCopyPageChunks(
                d, c, sealImage, "공급받는 자 보관용",
                marginTop, marginLeft, marginRight, marginBottom);
            _logger.LogDebug(
                "세금계산서 이어쓰기 — 벌 하나를 {Pages}장에 나눠 담는다(품목 {Total}줄) 문서번호 {DocNo}",
                copyChunks?.Count ?? 0, d.Lines.Count, d.DocNo);
        }

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(marginTop, Unit.Millimetre);
                page.MarginLeft(marginLeft, Unit.Millimetre);
                page.MarginRight(marginRight, Unit.Millimetre);
                page.MarginBottom(marginBottom, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(9));
                page.PageColor(Colors.White);

                page.Content().Column(col =>
                {
                    // 🔴 취소된 계산서는 법정 서식으로 내지 않는다 — 안내문 한 장만 낸다.
                    if (d.IsCanceledInvoice)
                    {
                        col.Item().Element(ComposeCanceledNotice);
                        return;
                    }

                    // 🔴 §15-6 T-2 — 이어쓰기 길. 쪽 나눔을 잰 경우에만 탄다.
                    if (copyChunks is not null)
                    {
                        var labels = new List<string>();
                        if (drawSupplier) labels.Add("공급자 보관용");
                        if (drawRecipient) labels.Add("공급받는 자 보관용");

                        var printed = 0;
                        foreach (var label in labels)
                        {
                            foreach (var chunk in copyChunks)
                            {
                                // 첫 장 앞에는 쪽 나눔을 넣지 않는다 — 빈 종이가 한 장 는다.
                                if (printed > 0) col.Item().PageBreak();

                                var copyLabel = label;
                                var part = chunk;
                                col.Item().Element(e => ComposeTaxInvoiceCopyPage(
                                    e, d, c, copyLabel, sealImage, part.Skip, part.Take, part.IsLast));
                                printed++;
                            }
                        }
                        return;
                    }

                    if (drawSupplier)
                    {
                        col.Item().Element(e => ComposeTaxInvoiceCopy(
                            e, d, c, "공급자 보관용", sealImage, visibleLines));
                    }
                    if (bothCopies)
                    {
                        // 🔴 고객이 켠 경우에만 자르는 선. 끄면 자르는 선 0건 + 쪽을 나눈다.
                        if (useTearLine) col.Item().Element(ComposeTearLine);
                        else col.Item().PageBreak();
                    }
                    if (drawRecipient)
                    {
                        col.Item().Element(e => ComposeTaxInvoiceCopy(
                            e, d, c, "공급받는 자 보관용", sealImage, visibleLines));
                    }
                });

                // 한글 꼬리 — 한 장이면 「1 / 1 쪽」.
                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(8);
                    t.Span(" / ").FontSize(8);
                    t.TotalPages().FontSize(8);
                    t.Span(" 쪽").FontSize(8);
                });
            });
        // 옛 줄(W-G1-51 PDF 잠금 전 · 헌법 #1): }).GeneratePdf();
        }).GeneratePdfOneAtATime();
    }

    /// <summary>취소된 계산서 안내문 — 고객이 읽는 말로만 적는다.</summary>
    private static void ComposeCanceledNotice(IContainer e)
    {
        e.PaddingTop(40).AlignCenter().Column(col =>
        {
            col.Item().AlignCenter().Text("취소된 세금계산서입니다").FontSize(16).Bold();
            col.Item().PaddingTop(10).AlignCenter()
                .Text("취소된 계산서는 인쇄하거나 보낼 수 없습니다.").FontSize(11);
            col.Item().PaddingTop(3).AlignCenter()
                .Text("필요하시면 계산서를 다시 발행한 뒤 인쇄해 주세요.").FontSize(11);
        });
    }

    /// <summary>두 벌 사이 절취선.</summary>
    private static void ComposeTearLine(IContainer e)
    {
        e.PaddingVertical(4).Column(col =>
        {
            col.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(2).AlignCenter().Text("절취선").FontSize(8);
        });
    }

    /// <summary>
    /// 🔴 20261021작 §10-4 S-3 — 한 장에 모을 때 <b>품목을 몇 줄까지 찍을 수 있나</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 전부 들어가면 전부 찍는다(이때 「외 N건」 칸은 아예 안 나온다). 안 들어가면
    /// <b>「외 N건」 칸 한 줄까지 포함해서</b> 들어가는 가장 큰 줄 수를 찾는다 —
    /// 그래야 그 칸 때문에 또 넘치는 일이 없다.
    /// </para>
    /// <para>
    /// 🔴 줄 수로 눈대중하지 않는다. 후보마다 <b>실제로 깔아 보고 높이를 받는다</b>.
    /// 들어가는지 여부는 줄 수에 대해 단조라(줄이 늘면 높이도 는다) 반으로 갈라 찾는다.
    /// </para>
    /// <para>🔴 한 줄도 못 넣으면 <b>-1</b> — 부르는 쪽이 쪽을 나눈다.</para>
    /// </remarks>
    private int ResolveVisibleLineCount(DocumentSnapshot d, CompanyInfo c, byte[]? sealImage,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var total = d.Lines.Count;
        if (MeasureBothCopiesFitOnePage(
                d, c, sealImage, marginTop, marginLeft, marginRight, marginBottom, total))
        {
            return total;
        }

        var best = -1;
        var lo = 0;
        var hi = total - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (MeasureBothCopiesFitOnePage(
                    d, c, sealImage, marginTop, marginLeft, marginRight, marginBottom, mid))
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }

    /// <summary>
    /// 🔴 20261021작 §9-1(R-1) — 두 벌 + 절취선이 A4 한 장에 <b>실제로</b> 들어가는지 잰다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>품목 줄 수로 눈대중하지 않는다.</b> 같은 종이·같은 여백·같은 글꼴로 한 장을 깔게 한 뒤
    /// QuestPDF 가 <b>그때 잰 높이</b>를 받는다 — 공급자 벌 높이 · 절취선 높이 · 공급받는 자 벌 높이,
    /// 그리고 머리·꼬리를 뺀 <b>실제 남은 높이</b>. 넷을 견주어 판정한다.
    /// </para>
    /// <para>
    /// 설계 §7 의 「136mm + 7mm + 136mm = 279mm ≤ 297mm」는 설계 당시의 손계산이다.
    /// 여기서는 그 숫자를 <b>쓰지 않는다</b> — 여백 설정·거래처 주소 길이·품목 줄 수에 따라
    /// 벌 높이가 달라지므로, 고정 숫자로 판정하면 또 틀린다.
    /// </para>
    /// <para>
    /// 🔴 재다가 실패하면 <b>「안 들어간다」로 본다.</b> 쪽을 나누면 종이가 두 장 나올 뿐이지만,
    /// 잘못 「들어간다」로 보면 <b>반 토막 계산서</b>가 고객에게 나간다.
    /// </para>
    /// </remarks>
    /// <param name="maxLines">
    /// 한 벌에 찍을 품목 줄 수 상한. 전체보다 작으면 <b>「외 N건」 칸까지 포함해</b> 잰다.
    /// 안 주면 전부 찍는 것으로 본다(§9-1 때와 같은 뜻).
    /// </param>
    private bool MeasureBothCopiesFitOnePage(DocumentSnapshot d, CompanyInfo c, byte[]? sealImage,
        float marginTop, float marginLeft, float marginRight, float marginBottom,
        int maxLines = int.MaxValue)
    {
        var probe = new TaxInvoiceFitProbe(d, c, sealImage, maxLines);
        try
        {
            Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginTop(marginTop, Unit.Millimetre);
                    page.MarginLeft(marginLeft, Unit.Millimetre);
                    page.MarginRight(marginRight, Unit.Millimetre);
                    page.MarginBottom(marginBottom, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(9));
                    page.PageColor(Colors.White);
                    page.Content().Dynamic(probe);

                    // 꼬리를 똑같이 달아야 「남은 높이」가 실제와 같아진다.
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.CurrentPageNumber().FontSize(8);
                        t.Span(" / ").FontSize(8);
                        t.TotalPages().FontSize(8);
                        t.Span(" 쪽").FontSize(8);
                    });
                });
            // 옛 줄(W-G1-51 PDF 잠금 전 · 헌법 #1): }).GeneratePdf();
            }).GeneratePdfOneAtATime();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "세금계산서 쪽 나눔을 재지 못했다 — 안전하게 두 쪽으로 나눠 낸다. 문서번호 {DocNo}", d.DocNo);
            return false;
        }

        if (!probe.Measured)
        {
            _logger.LogWarning(
                "세금계산서 쪽 나눔 높이를 받지 못했다 — 안전하게 두 쪽으로 나눠 낸다. 문서번호 {DocNo}", d.DocNo);
            return false;
        }

        var needed = probe.SupplierHeight + probe.TearHeight + probe.RecipientHeight;

        // 판정 근거를 남긴다 — 「왜 두 쪽이 나왔냐」는 문의가 오면 이 줄이 답이다.
        _logger.LogDebug(
            "세금계산서 쪽 나눔 판정 — 공급자 {Supplier}pt · 절취선 {Tear}pt · 공급받는자 {Recipient}pt " +
            "= {Needed}pt / 남은 높이 {Available}pt (문서번호 {DocNo})",
            probe.SupplierHeight, probe.TearHeight, probe.RecipientHeight,
            needed, probe.AvailableHeight, d.DocNo);

        return needed <= probe.AvailableHeight;
    }

    /// <summary>
    /// 🔴 쪽이 딱 맞는 자리에서 반올림 때문에 한 줄이 다음 장으로 밀리는 것을 막는 여유(pt).
    /// 밀리면 <b>품목 0줄짜리 꼬리 종이</b>가 다시 난다 — T-2 가 없애려는 바로 그 모양이다.
    /// </summary>
    private const float TaxInvoicePageSlackPt = 2f;

    /// <summary>
    /// 🔴🔴 20261021작 §17 T-7 / §20 — 세금계산서 이어쓰기가 <b>실측으로 버티는 여백 상한(mm)</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>실측 — 여백 24~63mm 를 1mm 간격으로, 모드 셋(<c>both</c>·<c>recipient</c>·<c>supplier</c>) ×
    /// 품목 40·80·100줄.</b>
    /// <list type="bullet">
    /// <item>🟢 <b>0~39mm</b> — 전건 정상. 머리 없는 쪽 0 · 품목 0줄 쪽 0 · 품목 합계 전건 일치.</item>
    /// <item>🔴 <b>첫 고장은 40mm</b> — <b>세 모드 전부</b> <c>[33,0,7]</c>(가운데 쪽이 머리도 품목도 없다).
    ///   품목 합계는 맞다 ⇒ <b>줄이 사라지는 것이 아니라 빈 쪽이 끼는</b> 고장이다.
    ///   재는 쪽은 「33줄 들어간다」는데 깔면 넘쳐 다음 장으로 밀린다.</item>
    /// <item>🔴 <b>80mm 이상</b> — <c>DocumentLayoutException</c> ⇒ <b>종이가 아예 안 나온다</b>.</item>
    /// <item>🔴 <b>100mm 이상</b> — 이어쓰기 자체가 폴백(<c>chunks == null</c>)으로 떨어진다.</item>
    /// </list>
    /// <para>
    /// 🔴 <b>상한을 39 가 아니라 35 로 두는 사유</b>: 고장의 뿌리는 <b>재는 쪽과 깔리는 쪽의 어긋남</b>이고,
    /// 그 어긋남은 <b>내용 높이에 따라</b> 움직인다. 위 실측은 <b>한 가지 자료 모양</b>으로만 쟀으므로
    /// 경계값(39)에 딱 붙이지 않고 <b>4mm 여유</b>를 둔다. 🔴 이 숫자를 바꿀 일이 생기면
    /// <b>눈대중으로 바꾸지 말고 1mm 실측을 다시 돌린다.</b>
    /// </para>
    /// <para>
    /// 🔴🔴 <b>§20 — PM 이 이 자리에서 두 번 틀렸다.</b> ① 첫 봉합은 상한을 <b>30mm</b> 로 뒀다.
    /// 「40mm 이상에서 깨진다」는 맞았지만 <b>31~39mm 는 고장이 없던 구간</b>이라 그 고객의 종이를
    /// 바꿀 근거가 없었다. ② [4] 3차가 「31~50mm 전건 정상 · 첫 고장 60mm」라 보고해 상한을 50 으로
    /// 올렸더니 <b>40·50mm 가 전부 빨개졌다</b> ⇒ 그 보고도 틀렸다. 🟢 <b>둘 다 1mm 실측으로 갈랐다.</b>
    /// </para>
    /// <para>
    /// 🔴 왜 고객 값을 조이기까지 하나 — 편집창 여백 4칸에 상·하한이 <b>없었고</b> DTO 가
    /// <c>Range(0, 100)</c> 을 받았다. 화면·DTO 만 조이면 <b>이미 큰 값으로 저장한 고객의 종이는
    /// 계속 깨진다</b>(고객이 손을 대야 풀린다). 그래서 그리는 쪽에서도 조인다.
    /// 🔴 <b>고객이 저장한 값은 지우지 않는다</b> — 그릴 때만 조인다. 35mm 이하면 아무 일도 안 한다.
    /// </para>
    /// </remarks>
    internal const float TaxInvoiceMaxMarginMm = 35f;

    /// <summary>🔴 §17 T-7 — 지원 범위를 넘는 여백을 조인다. 조였으면 글로 남긴다.</summary>
    private float ClampTaxInvoiceMargin(float mm, string which, string? docNo)
    {
        if (mm >= 0f && mm <= TaxInvoiceMaxMarginMm)
        {
            return mm;
        }

        var clamped = mm < 0f ? 0f : TaxInvoiceMaxMarginMm;
        _logger.LogWarning(
            "세금계산서 여백이 지원 범위를 벗어났다 — {Which} {Given}mm 를 {Clamped}mm 로 보고 그린다. " +
            "양식정보설정에서 여백을 0~{Max}mm 로 맞추시면 됩니다. 문서번호 {DocNo}",
            which, mm, clamped, TaxInvoiceMaxMarginMm, docNo);
        return clamped;
    }

    /// <summary>
    /// 🔴🔴 20261021작 §15-6 T-2 — <b>벌 하나를 몇 장에 어떻게 나눠 담나</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 앞에서부터 한 장씩 채운다. 장마다 먼저 <b>「남은 줄을 꼬리까지 붙여 이 한 장에 담을 수
    /// 있나」</b>를 재고, 담을 수 있으면 그 장을 마지막 장으로 삼아 끝낸다. 못 담으면
    /// <b>꼬리 없이</b> 담을 수 있는 가장 큰 줄 수를 찾아 그만큼만 담고 다음 장으로 넘긴다.
    /// </para>
    /// <para>
    /// 🔴 <b>담을 줄 수는 늘 1 이상</b>이다 — 이어 쓰는 장의 상한을 <c>남은 줄 - 1</c> 로 두어
    /// 마지막 장에도 품목이 반드시 한 줄 남게 한다. ⇒ <b>품목 0줄짜리 쪽이 날 수 없다.</b>
    /// </para>
    /// <para>
    /// 🔴 줄 수로 눈대중하지 않는다 — 후보마다 <b>실제로 깔아 보고 높이를 받는다</b>
    /// (<c>MeasureCopyPageFits</c>). 들어가는지는 줄 수에 대해 단조라 반으로 갈라 찾는다.
    /// </para>
    /// <para>
    /// 🔴 한 줄도 못 담거나 품목이 없으면 <b><c>null</c></b> — 부르는 쪽이 종전 길로 간다.
    /// </para>
    /// </remarks>
    /// <returns>장마다 (건너뛸 줄 수 · 담을 줄 수 · 마지막 장이냐). 못 재면 <c>null</c>.</returns>
    private List<(int Skip, int Take, bool IsLast)>? ResolveCopyPageChunks(
        DocumentSnapshot d, CompanyInfo c, byte[]? sealImage, string copyLabel,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var total = d.Lines.Count;
        if (total <= 0)
        {
            return null;   // 품목이 없으면 나눌 것도 없다 — 종전 길 그대로.
        }

        var chunks = new List<(int Skip, int Take, bool IsLast)>();
        var skip = 0;
        while (skip < total)
        {
            var remaining = total - skip;

            // ① 남은 줄을 꼬리까지 붙여 한 장에 담을 수 있나 → 되면 여기서 끝난다.
            if (MeasureCopyPageFits(d, c, sealImage, copyLabel, skip, remaining, true,
                    marginTop, marginLeft, marginRight, marginBottom))
            {
                chunks.Add((skip, remaining, true));
                return chunks;
            }

            // ② 못 담는다 → 꼬리 없는 「이어 쓰는 장」으로 담을 수 있는 가장 큰 줄 수.
            //    상한을 remaining - 1 로 둔다 ⇒ 마지막 장에 품목이 반드시 남는다.
            var take = ResolveMaxLinesOnOnePage(d, c, sealImage, copyLabel, skip, remaining - 1,
                marginTop, marginLeft, marginRight, marginBottom);
            if (take <= 0)
            {
                // 🔴 한 줄도 못 담는다 = 여백이 종이를 거의 다 먹었다. 억지로 나누지 않고
                //   종전 길로 돌려보낸다 — 고객이 여백을 줄이면 바로 풀린다.
                _logger.LogWarning(
                    "세금계산서를 이어쓰기로 나누지 못했다 — 한 장에 품목이 한 줄도 안 들어간다. " +
                    "여백이 너무 크지 않은지 양식정보설정을 확인하시면 됩니다. 문서번호 {DocNo}", d.DocNo);
                return null;
            }

            chunks.Add((skip, take, false));
            skip += take;
        }

        return chunks.Count > 0 ? chunks : null;
    }

    /// <summary>
    /// 🔴 §15-6 T-2 — 이어 쓰는 장 하나에 <b>품목을 몇 줄까지</b> 담을 수 있나(꼬리 없음).
    /// </summary>
    /// <param name="maxTake">찾을 줄 수의 상한. 0 이하이면 찾을 것이 없다.</param>
    /// <returns>담을 수 있는 가장 큰 줄 수. 한 줄도 못 담으면 <b>0</b>.</returns>
    private int ResolveMaxLinesOnOnePage(DocumentSnapshot d, CompanyInfo c, byte[]? sealImage,
        string copyLabel, int skip, int maxTake,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var best = 0;
        var lo = 1;
        var hi = maxTake;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (MeasureCopyPageFits(d, c, sealImage, copyLabel, skip, mid, false,
                    marginTop, marginLeft, marginRight, marginBottom))
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }

    /// <summary>
    /// 🔴 §15-6 T-2 — 이어 쓰는 장 하나가 A4 한 장에 <b>실제로</b> 들어가는지 잰다.
    /// </summary>
    /// <remarks>
    /// 그릴 때와 <b>같은 종이·같은 여백·같은 글꼴·같은 꼬리</b>로 깔아 보고 높이를 받는다.
    /// 🔴 재다가 실패하면 <b>「안 들어간다」로 본다</b> — 잘못 「들어간다」로 보면 종이가 밀려
    /// 다시 꼬리 종이가 난다.
    /// </remarks>
    private bool MeasureCopyPageFits(DocumentSnapshot d, CompanyInfo c, byte[]? sealImage,
        string copyLabel, int skip, int take, bool isLast,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var probe = new TaxInvoicePageProbe(
            x => ComposeTaxInvoiceCopyPage(x, d, c, copyLabel, sealImage, skip, take, isLast));
        try
        {
            Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginTop(marginTop, Unit.Millimetre);
                    page.MarginLeft(marginLeft, Unit.Millimetre);
                    page.MarginRight(marginRight, Unit.Millimetre);
                    page.MarginBottom(marginBottom, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(9));
                    page.PageColor(Colors.White);
                    page.Content().Dynamic(probe);

                    // 꼬리를 똑같이 달아야 「남은 높이」가 실제와 같아진다.
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.CurrentPageNumber().FontSize(8);
                        t.Span(" / ").FontSize(8);
                        t.TotalPages().FontSize(8);
                        t.Span(" 쪽").FontSize(8);
                    });
                });
            // 옛 줄(W-G1-51 PDF 잠금 전 · 헌법 #1): }).GeneratePdf();
            }).GeneratePdfOneAtATime();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "세금계산서 이어쓰기 쪽을 재지 못했다 — 그 줄 수는 안 들어가는 것으로 본다. " +
                "문서번호 {DocNo}", d.DocNo);
            return false;
        }

        if (!probe.Measured)
        {
            _logger.LogWarning(
                "세금계산서 이어쓰기 쪽 높이를 받지 못했다 — 안 들어가는 것으로 본다. 문서번호 {DocNo}",
                d.DocNo);
            return false;
        }

        return probe.Height <= probe.AvailableHeight - TaxInvoicePageSlackPt;
    }

    /// <summary>
    /// 🔴 §15-6 T-2 — 이어 쓰는 장 하나의 <b>높이만</b> 받아 오는 자. 종이는 버린다.
    /// </summary>
    private sealed class TaxInvoicePageProbe : IDynamicComponent
    {
        private readonly Action<IContainer> _compose;

        public TaxInvoicePageProbe(Action<IContainer> compose) => _compose = compose;

        /// <summary>높이를 한 번이라도 받았나.</summary>
        public bool Measured { get; private set; }
        public float Height { get; private set; }
        public float AvailableHeight { get; private set; }

        public DynamicComponentComposeResult Compose(QuestPDF.Elements.DynamicContext context)
        {
            if (!Measured)
            {
                var element = context.CreateElement(_compose);
                Height = element.Size.Height;
                AvailableHeight = context.AvailableSize.Height;
                Measured = true;
            }

            return new DynamicComponentComposeResult
            {
                Content = context.CreateElement(x => x.Height(0)),
                HasMoreContent = false
            };
        }
    }

    /// <summary>
    /// 쪽 나눔 판정을 위한 자 — 종이는 버리고 <b>높이만</b> 가져온다(20261021작 §9-1).
    /// </summary>
    private sealed class TaxInvoiceFitProbe : IDynamicComponent
    {
        private readonly DocumentSnapshot _d;
        private readonly CompanyInfo _c;
        private readonly byte[]? _seal;
        private readonly int _maxLines;

        public TaxInvoiceFitProbe(DocumentSnapshot d, CompanyInfo c, byte[]? seal, int maxLines)
        {
            _d = d;
            _c = c;
            _seal = seal;
            _maxLines = maxLines;
        }

        /// <summary>높이를 한 번이라도 받았나.</summary>
        public bool Measured { get; private set; }
        public float SupplierHeight { get; private set; }
        public float TearHeight { get; private set; }
        public float RecipientHeight { get; private set; }
        public float AvailableHeight { get; private set; }

        public DynamicComponentComposeResult Compose(QuestPDF.Elements.DynamicContext context)
        {
            if (!Measured)
            {
                var supplier = context.CreateElement(
                    x => ComposeTaxInvoiceCopy(x, _d, _c, "공급자 보관용", _seal, _maxLines));
                var tear = context.CreateElement(ComposeTearLine);
                var recipient = context.CreateElement(
                    x => ComposeTaxInvoiceCopy(x, _d, _c, "공급받는 자 보관용", _seal, _maxLines));

                SupplierHeight = supplier.Size.Height;
                TearHeight = tear.Size.Height;
                RecipientHeight = recipient.Size.Height;
                AvailableHeight = context.AvailableSize.Height;
                Measured = true;
            }

            // 잰 뒤에는 빈 자리만 돌려준다 — 이 종이는 쓰지 않고 버린다.
            return new DynamicComponentComposeResult
            {
                Content = context.CreateElement(x => x.Height(0)),
                HasMoreContent = false
            };
        }
    }

    /// <summary>세금계산서 한 벌.</summary>
    /// <param name="maxLines">
    /// 찍을 품목 줄 수 상한(20261021작 §10-2 ⓓ). 전체보다 작으면 그만큼만 찍고
    /// <b>마지막 칸에 「외 N건」</b>을 적는다 — 고객이 「한 장에 모으기」를 켠 경우에만
    /// 부르는 쪽이 이 값을 줄인다. 합계는 <b>늘 전체 기준</b>이라 금액이 어긋나지 않는다.
    /// </param>
    private static void ComposeTaxInvoiceCopy(IContainer e, DocumentSnapshot d, CompanyInfo c,
        string copyLabel, byte[]? sealImage, int maxLines = int.MaxValue)
    {
        // 🔴 20261021작 §15-6 T-2 — 속을 셋(머리·품목표·꼬리)으로 나눴다.
        //   그려 내는 모양은 한 글자도 안 바뀐다. 나눈 까닭은 「자르는 선 끔 + 품목 넘침」에서
        //   장마다 머리를 다시 붙이고 꼬리는 마지막 장에만 붙이기 위해서다
        //   (ComposeTaxInvoiceCopyPage).
        var shownLines = Math.Min(maxLines, d.Lines.Count);
        e.Column(col =>
        {
            ComposeTaxInvoiceCopyHead(col, d, c, copyLabel);
            ComposeTaxInvoiceLineTable(col, d, 0, shownLines, d.Lines.Count - shownLines, true);
            ComposeTaxInvoiceCopyTail(col, d, c, sealImage);
        });
    }

    /// <summary>
    /// 🔴 20261021작 §15-6 T-2 — <b>벌 하나를 여러 장에 이어 쓸 때의 한 장</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 장마다 <b>머리</b>(보관용 라벨·작성일자·승인번호·공급자/공급받는 자·합계금액)를 다시
    /// 붙인다 — 어느 쪽을 집어도 누구 것인지 안다. 품목은 <paramref name="skip"/> 번째부터
    /// <paramref name="take"/> 줄만 담는다.
    /// </para>
    /// <para>
    /// <b>꼬리</b>(결제구분·영수/청구·비고·직인·서명란)와 품목 <b>합계줄</b>은
    /// <paramref name="isLast"/> 인 장에만 붙는다 ⇒ 🔴 <b>품목이 한 줄도 없는 꼬리 종이가
    /// 날 수 없다.</b> 마지막이 아닌 장에는 「다음 쪽에 이어집니다」를 적는다.
    /// </para>
    /// <para>
    /// 🔴 <b>줄을 버리는 코드는 0건</b>이다 — 「외 N건」은 이 경로에 <b>없다</b>.
    /// 그건 자르는 선 <b>켬</b> 전용이다(§15-5 · <c>ResolveVisibleLineCount</c>).
    /// </para>
    /// </remarks>
    private static void ComposeTaxInvoiceCopyPage(IContainer e, DocumentSnapshot d, CompanyInfo c,
        string copyLabel, byte[]? sealImage, int skip, int take, bool isLast)
    {
        e.Column(col =>
        {
            ComposeTaxInvoiceCopyHead(col, d, c, copyLabel);
            ComposeTaxInvoiceLineTable(col, d, skip, take, 0, isLast);
            if (isLast)
            {
                ComposeTaxInvoiceCopyTail(col, d, c, sealImage);
            }
            else
            {
                col.Item().PaddingTop(2).AlignRight()
                    .Text("품목이 많아 다음 쪽에 이어집니다").FontSize(8);
            }
        });
    }

    /// <summary>세금계산서 한 벌의 <b>머리</b> — 이어 쓰는 장마다 다시 붙는다(§15-6 T-2).</summary>
    private static void ComposeTaxInvoiceCopyHead(ColumnDescriptor col,
        DocumentSnapshot d, CompanyInfo c, string copyLabel)
    {
        // 제목줄 — 왼쪽 보관용 라벨 · 가운데 제목 · 오른쪽 전자 표시
        col.Item().Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(copyLabel).FontSize(9).Bold();
            r.RelativeItem(2).AlignCenter().Text("세 금 계 산 서").FontSize(14).Bold();
            r.RelativeItem().AlignMiddle().AlignRight().Text("전자").FontSize(9).Bold();
        });

        // 작성일자 · 승인번호
        col.Item().PaddingTop(1).Row(r =>
        {
            r.RelativeItem().Text($"작성일자: {d.DocDate:yyyy-MM-dd}").FontSize(9);
            r.RelativeItem().AlignRight().Text($"승인번호: {d.DocNo}").FontSize(9);
        });
        if (!string.IsNullOrWhiteSpace(d.HometaxApprovalNo))
        {
            col.Item().AlignRight().Text($"국세청승인번호: {d.HometaxApprovalNo}").FontSize(8);
        }
        // 수정사유 — 칸은 늘 그리고, 값이 있으면 채운다.
        col.Item().PaddingTop(1).Text($"수정사유: {d.AmendmentReason ?? ""}").FontSize(9);

        // 공급자 / 공급받는 자 — 2단 대칭 격자
        col.Item().PaddingTop(2).Border(1.2f).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(18);
                columns.RelativeColumn();
                columns.ConstantColumn(18);
                columns.RelativeColumn();
            });

            table.Cell().Border(0.5f).Background(TaxGridBg).AlignCenter().AlignMiddle()
                .Text("공\n급\n자").FontSize(8).Bold();
            table.Cell().Border(0.5f).Padding(2).Column(x =>
            {
                TaxInfoRow(x, "등록번호", c.BusinessNo);
                TaxInfoRow(x, "상    호", c.CompanyName);
                TaxInfoRow(x, "대 표 자", c.RepresentativeName);
                TaxInfoRow(x, "주    소", c.Address);
                TaxInfoRow(x, "업    태", c.BizType);
                TaxInfoRow(x, "종    목", c.BizItem);
                TaxInfoRow(x, "전    화", c.Phone);
            });

            table.Cell().Border(0.5f).Background(TaxGridBg).AlignCenter().AlignMiddle()
                .Text("공\n급\n받\n는\n자").FontSize(8).Bold();
            table.Cell().Border(0.5f).Padding(2).Column(x =>
            {
                TaxInfoRow(x, "등록번호", d.PartnerBusinessNo);
                TaxInfoRow(x, "상    호", d.PartnerName);
                TaxInfoRow(x, "대 표 자", d.PartnerCeoName);
                TaxInfoRow(x, "주    소", d.PartnerAddress);
                TaxInfoRow(x, "업    태", d.PartnerBizType);
                TaxInfoRow(x, "종    목", d.PartnerBizItem);
                TaxInfoRow(x, "전    화", d.PartnerPhone);
            });
        });

        // 합계금액 · 공급가액 · 세액
        col.Item().PaddingTop(2).Border(1.2f).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            table.Cell().Element(TaxHeaderCell).AlignCenter().Text("합계금액").FontSize(9).Bold();
            table.Cell().Element(TaxHeaderCell).AlignCenter().Text("공급가액").FontSize(9).Bold();
            table.Cell().Element(TaxHeaderCell).AlignCenter().Text("세액").FontSize(9).Bold();
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalAmount:N0}").FontSize(9).Bold();
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalSupply:N0}").FontSize(9);
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalVat:N0}").FontSize(9);
        });
    }

    /// <summary>세금계산서 한 벌의 <b>품목표</b>(§15-6 T-2 로 떼어 냈다).</summary>
    /// <param name="skip">몇 번째 줄부터 담나 — 이어 쓰는 장의 시작점.</param>
    /// <param name="take">이 장에 담을 줄 수.</param>
    /// <param name="hiddenCount">「외 N건」의 N — 자르는 선 <b>켬</b> 경로에서만 0 보다 크다.</param>
    /// <param name="includeTotalRow">품목 합계줄을 붙이나 — <b>마지막 장에만</b> 붙인다.</param>
    private static void ComposeTaxInvoiceLineTable(ColumnDescriptor col, DocumentSnapshot d,
        int skip, int take, int hiddenCount, bool includeTotalRow)
    {
        // 품목 — 🔴 자르는 코드 0건. 넘치면 다음 장으로 이어 찍는다.
        col.Item().PaddingTop(2).Border(1.2f).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(24);     // 월
                columns.ConstantColumn(24);     // 일
                columns.RelativeColumn(3);      // 품목
                columns.RelativeColumn(1.2f);   // 규격
                columns.RelativeColumn(1);      // 수량
                columns.RelativeColumn(1.3f);   // 단가
                columns.RelativeColumn(1.5f);   // 공급가액
                columns.RelativeColumn(1.3f);   // 세액
            });

            // 🔴 table.Header( — 2쪽에도 열 제목이 다시 나온다(합격선 ⑤).
            table.Header(header =>
            {
                foreach (var h in new[] { "월", "일", "품목", "규격", "수량", "단가", "공급가액", "세액" })
                {
                    header.Cell().Element(TaxHeaderCell).AlignCenter().Text(h).FontSize(8).Bold();
                }
            });

            // 🔴 20261021작 §10-2 ⓓ — 「한 장에 모으기」를 켠 경우에만 상한이 걸린다.
            //   기본(끔)에서는 부르는 쪽이 줄 수를 전부 넘기므로 **전부** 찍힌다(§9-1 그대로).
            // 🔴 §15-6 T-2 — 이어 쓰는 장은 skip 번째부터 take 줄만 담는다.
            //   skip·take 는 부르는 쪽이 **실제로 재서** 정한다(ResolveCopyPageChunks).
            foreach (var line in d.Lines.Skip(skip).Take(take))
            {
                table.Cell().Element(TaxDataCell).AlignCenter().Text($"{d.DocDate.Month}").FontSize(8);
                table.Cell().Element(TaxDataCell).AlignCenter().Text($"{d.DocDate.Day}").FontSize(8);
                table.Cell().Element(TaxDataCell).Text(line.ItemName).FontSize(8);
                table.Cell().Element(TaxDataCell).Text(line.Spec ?? "").FontSize(8);
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{line.Qty:N0}").FontSize(8);
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{line.UnitPrice:N0}").FontSize(8);
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{line.Supply:N0}").FontSize(8);
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{line.Vat:N0}").FontSize(8);
            }

            // 🔴 안 찍힌 줄은 **숫자로 알려 준다** — 조용히 사라지지 않는다.
            //   국세청 세금계산서 서식의 품목란이 원래 몇 칸이고, 넘는 분은 「외 ○건」으로
            //   적는 것이 실제 관행이다(사장님 재결재 §10-2 ⓓ).
            //   합계 금액·수량은 **전체 기준** 그대로라 세액이 어긋나지 않는다.
            if (hiddenCount > 0)
            {
                table.Cell().ColumnSpan(2).Element(TaxDataCell).Text("").FontSize(8);
                table.Cell().ColumnSpan(6).Element(TaxDataCell)
                    .Text($"외 {hiddenCount}건").FontSize(8).Bold();
            }

            // 🔴 §15-6 T-2 — 합계줄은 **마지막 장에만** 붙는다. 이어 쓰는 장마다 합계를 찍으면
            //   한 벌에 합계가 여러 번 나와 고객이 금액을 두 번 읽는다.
            //   합계 금액·수량은 늘 **전체 기준**이라 쪽을 나눠도 세액이 안 어긋난다.
            if (!includeTotalRow) return;

            table.Cell().ColumnSpan(4).Element(TaxHeaderCell).AlignCenter().Text("합  계").FontSize(9).Bold();
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.Lines.Sum(x => x.Qty):N0}").FontSize(8).Bold();
            table.Cell().Element(TaxDataCell).Text("").FontSize(8);
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalSupply:N0}").FontSize(8).Bold();
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalVat:N0}").FontSize(8).Bold();
        });
    }

    /// <summary>세금계산서 한 벌의 <b>꼬리</b> — 마지막 장에만 붙는다(§15-6 T-2).</summary>
    private static void ComposeTaxInvoiceCopyTail(ColumnDescriptor col,
        DocumentSnapshot d, CompanyInfo c, byte[]? sealImage)
    {
        // 결제구분 — 🔴 칸만 그린다. 값을 넣을 원천이 어디에도 없다(설계 §8 5번).
        //   입력 자리 신설은 이번 범위 밖이다 — 고객이 종이에 적는다.
        col.Item().PaddingTop(2).Border(1.2f).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            foreach (var h in new[] { "현금", "수표", "어음", "외상미수금" })
            {
                table.Cell().Element(TaxHeaderCell).AlignCenter().Text(h).FontSize(8).Bold();
            }
            for (var i = 0; i < 4; i++)
            {
                table.Cell().Element(TaxDataCell).MinHeight(11).Text("").FontSize(8);
            }
        });

        // 영수 / 청구 — 🔴 원천이 없다. 두 낱말을 칸으로 그려 고객이 종이에 동그라미를 친다
        //   (결재 Q-1 ⓒ). 외상미수금으로 추론하지 않는다.
        col.Item().PaddingTop(3).Row(r =>
        {
            r.RelativeItem().Column(left =>
            {
                left.Item().Row(rr =>
                {
                    rr.AutoItem().AlignMiddle().Text("이 금액을 ").FontSize(9);
                    rr.AutoItem().Border(0.8f).PaddingHorizontal(6).PaddingVertical(1)
                        .Text("영수").FontSize(9).Bold();
                    rr.AutoItem().AlignMiddle().PaddingHorizontal(3).Text("/").FontSize(9);
                    rr.AutoItem().Border(0.8f).PaddingHorizontal(6).PaddingVertical(1)
                        .Text("청구").FontSize(9).Bold();
                    rr.AutoItem().AlignMiddle().Text(" 함").FontSize(9);
                });
                left.Item().PaddingTop(2).Text("이 금액을 영수(청구)함").FontSize(9).Bold();

                // 비고란
                left.Item().PaddingTop(2).Text("비고").FontSize(8).Bold();
                left.Item().PaddingTop(1).Border(0.5f).MinHeight(16).Padding(2)
                    .Text(d.Remark ?? "").FontSize(8);
            });
            r.ConstantItem(78).PaddingLeft(6).AlignBottom()
                .Element(x => ComposeTaxSeal(x, c.CompanyName, sealImage));
        });

        // 서명란
        col.Item().PaddingTop(3).Border(1.2f).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            foreach (var h in new[] { "담당자", "확인자", "인수자" })
            {
                table.Cell().Element(TaxHeaderCell).AlignCenter().Text(h).FontSize(8).Bold();
            }
            for (var i = 0; i < 3; i++)
            {
                table.Cell().Element(TaxDataCell).MinHeight(14).Text("").FontSize(8);
            }
        });
    }

    /// <summary>공급자·공급받는 자 칸 한 줄.</summary>
    private static void TaxInfoRow(ColumnDescriptor col, string label, string? value)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(52).Text(label).FontSize(8);
            r.RelativeItem().Text(value ?? "").FontSize(8);
        });
    }

    private static IContainer TaxHeaderCell(IContainer container)
        => container.Border(0.5f).Background(TaxGridBg).PaddingVertical(1.5f).PaddingHorizontal(3);

    private static IContainer TaxDataCell(IContainer container)
        => container.Border(0.5f).PaddingVertical(1).PaddingHorizontal(3);

    /// <summary>
    /// 인장 — 🔴 <c>seal_url</c> 에 값이 있으면 그 이미지를, 비어 있으면 지금의 글자 도장을 그린다.
    /// </summary>
    private static void ComposeTaxSeal(IContainer e, string companyName, byte[]? sealImage)
    {
        if (sealImage is not null)
        {
            e.AlignRight().AlignBottom().Width(56).Height(56).Image(sealImage).FitArea();
            return;
        }

        e.AlignRight().AlignBottom().Width(56).Height(56)
            .Border(1.5f).BorderColor(Colors.Red.Medium)
            .AlignCenter().AlignMiddle().Padding(3)
            .Text(text =>
            {
                var displayName = companyName.Length > 4
                    ? companyName[..4] + "\n" + (companyName.Length > 8 ? companyName[4..8] : companyName[4..])
                    : companyName;
                text.AlignCenter();
                text.Span(displayName).FontSize(8).FontColor(Colors.Red.Medium).Bold();
            });
    }
    /// <summary>
    /// 🔴 <b>양식지 그림을 깔지 말지 정하는 유일한 자리</b> (반려 R-1·R-3 봉합).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 함수로 뽑았나</b> — 종전에는 이 판단이 <c>RenderDocumentAsync</c> 본문에
    ///   한 줄로 묻혀 있어 <b>시험이 물 수가 없었다.</b> 검증팀 실측: 그리는 줄과 로더를
    ///   <b>통째로 빼도 게이트 13/13 이 초록</b>이었다(반려 R-3 · 결재 ㉯ 조건 3 미충족).
    ///   ⇒ 판단을 <b>부를 수 있는 한 자리</b>로 모아 시험이 직접 재게 한다.
    /// 🔴 <b>규칙은 하나다</b> — <b>미리보기일 때만</b> 깐다(§13-8).
    ///   실인쇄 PDF 에는 <b>0건</b>이다. 양식용지는 종이에 이미 인쇄돼 있어 또 찍으면
    ///   <b>이중 인쇄</b>가 되고, 순백지는 <b>히트판 양식</b>이라 업체 그림이 들어갈 자리가 아니다
    ///   (§13-7·§13-10 — 그건 <b>등재만</b>).
    /// </remarks>
    /// <summary>
    /// 🔴 저장된 양식 위에 <b>이번 인쇄에만</b> 쓸 옵션을 얹는다 (결-2 · 7종 공통).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>옵션이 없으면 받은 그대로 돌려준다</b> — 기존 인쇄 경로는 한 글자도 안 바뀐다(#1).
    /// 🔴 <b>양식 행이 아예 없을 때도 옵션은 먹어야 한다</b>. 고객이 「기본 양식 만들기」를
    ///   안 눌렀어도 인쇄 매수는 고를 수 있어야 하기 때문이다([1-V] E-7).
    /// 🔴 <b>저장하지 않는다</b> — 돌려주는 것은 이번 한 장을 그릴 입력일 뿐이다.
    /// </remarks>
    private static FormTemplateInfo? ApplyPrintOptions(FormTemplateInfo? template, PrintOptionOverrides? opt)
    {
        if (opt is null) return template;

        // 🔴🔴 [2026-10-26작1 §C-6 · 결-J] 이 조기 반환을 **넓혔다.**
        //   [그대로 두면 무슨 일이 나나] 종전에는 `PrintCopyMode`·`ShowTearLine` 둘만 봤다.
        //     ⇒ 워터마크 칸을 덮개에 더해도 **여기서 조용히 버려진다.** 칸은 있고
        //     화면은 값을 보내는데 미리보기만 안 바뀐다 — 「갔다 ≠ 보인다」의 또 다른 모양이다.
        //   🔴 그래서 칸을 더할 때 **이 줄을 같이 고친다.** 다음 칸을 더하는 사람도 여기를 보라.
        if (opt.PrintCopyMode is null && opt.ShowTearLine is null
            && opt.PrintBackground is null && opt.ShowWatermark is null
            && opt.WatermarkOpacity is null && opt.WatermarkSaturate is null
            && opt.WatermarkScalePct is null)
        {
            return template;
        }

        // 양식 행이 없으면 기본값 한 벌을 만들어 거기에 얹는다(E-7).
        var t = template ?? new FormTemplateInfo();

        if (!string.IsNullOrWhiteSpace(opt.PrintCopyMode)) t.PrintCopyMode = opt.PrintCopyMode!;
        if (opt.ShowTearLine is not null) t.ShowTearLine = opt.ShowTearLine.Value;

        // 🔴 DB-148 — 미리보기에서 고른 값을 **이번 한 번만** 얹는다. 저장하지 않는다.
        //   🔴 수치는 null 을 **덮지 않는다** — null 로 보내 온 칸은 「안 건드렸다」는 뜻이고,
        //   그러면 저장된 양식값(또는 공통값)이 그대로 쓰여야 한다.
        if (opt.PrintBackground is not null) t.PrintBackground = opt.PrintBackground.Value;
        if (opt.ShowWatermark is not null) t.ShowWatermark = opt.ShowWatermark;
        if (opt.WatermarkOpacity is not null) t.WatermarkOpacity = opt.WatermarkOpacity;
        if (opt.WatermarkSaturate is not null) t.WatermarkSaturate = opt.WatermarkSaturate;
        if (opt.WatermarkScalePct is not null) t.WatermarkScalePct = opt.WatermarkScalePct;
        return t;
    }

    private byte[]? ResolveFormBackground(PdfPreviewOverrides? overrides, FormTemplateInfo? template)
    {
        // 🔴 이 한 줄이 실인쇄를 지킨다. 빼면 **끈 고객의** 종이에 그림이 또 찍힌다.
        // 🔴 [재검증 C-3 봉합] 종전에는 `overrides is null` 로 **대리 판정**했다.
        //   전표 인쇄 미리보기도 overrides 를 타므로, 그 종이에 배경이 깔리면
        //   **미리보기엔 그림이 있고 인쇄물엔 없다** ⇒ 합격선 ⑤ 가 거짓이 된다.
        //
        // 🔴🔴 [2026-10-26작1 §C-3] 사장님 오더로 **조건을 넓혔다 — 지운 것이 아니다.**
        //   사장님 10/25: *"양식지 인쇄 / 순백지 인쇄 — 고객사의 옵션이 필요함"*
        //   ⇒ 종전: 양식 설계 미리보기일 때만 깐다.
        //     이제: **그것 + 고객이 「종이에도 찍기」를 켠 양식**(print_background=1).
        //   🔴 **끈 고객에게는 종전과 글자 하나 다르지 않다** — 기본값이 0 이다.
        //     끈 고객의 종이에 그림이 새면 그게 사고다(#1 · #20).
        //   🔴 양식용지를 **사다 쓰는** 고객(preprint)이 이걸 켜면 이중 인쇄가 된다 —
        //     그 판정은 화면이 먼저 말려 주고(갈래 D), 여기서는 고객이 고른 대로 그린다.
        //     「우리가 몰래 끈다」는 길은 안 만든다(반자동 원칙 · 고객이 정한다).
        if (overrides?.IsFormDesignPreview != true && template?.PrintBackground != true) return null;
        // 🔴🔴 [2026-11-19 W-G1-51 갈래 A · 설계 §5 A 줄 · 대표 11/18 Q11 · Q14 · 08시②] **양식지 안전망 — 한 줄 더했다.**
        //   위 줄은 그대로 두고(#1 · 지운 것 0) 이 줄을 **더해** 판정을 좁혔다. 두 줄을 합친 판정:
        //     깐다 ⇔ 설계 미리보기 **또는** (`print_background`=1 **그리고** 양식지 아님)
        //   [그대로 두면 무슨 일이 나나] 대표 결정 「양식지 = 늘 데이터만」(08시②) 인데
        //     `print_background`=1 로 남은 양식지 행은 전표 인쇄에 그림이 또 찍힌다(이중 인쇄).
        //     그 행은 업데이트 때 Y 템플릿으로 복제되고(대표 Q14 · 갈래 F) 원래 양식지는
        //     데이터만으로 남아야 한다 — **옮기기가 실패한 행도** 여기서 이중 인쇄 0 이 된다.
        //   🔴 칸 좌표 유무와 **상관없이** 본다 — 양식지는 칸이 있든 없든 데이터만이다.
        //   🔴 양식정보설정 미리보기(`IsFormDesignPreview`)는 종전대로 깐다 — 겹침 미리보기는
        //     그 화면에만 있다(대표 Q11 ⑴). 전표 [인쇄] 미리보기 창 = 데이터만 = 인쇄 바이트.
        //   🔴 빈 A4(`plain`) + 켬(Y 템플릿)은 종전대로 깐다 — 미리보기 바이트 = 인쇄 바이트.
        //   🔴 바뀌는 행 = 양식지 + `print_background`=1 행의 **전표 쪽** 종이뿐이다(기본값 0 행 무변).
        //   🔴 위 주석의 「preprint 가 켜면 고객이 고른 대로 그린다 · 몰래 끄는 길은 안 만든다」(작1 결-D)는
        //     대표 11/18 08시② 「종이에도 함께 인쇄」 선택 없앰 · Y 템플릿으로 대신 으로 바뀌었다 —
        //     옛 주석은 이력으로 남기고(지운 것 0) 판정은 이 줄이 정한다.
        if (overrides?.IsFormDesignPreview != true && template?.PaperMode == "preprint") return null;
        return TryLoadFormBackground(template?.BackgroundImageUrl);
    }

    /// <summary>
    /// 🔴 업체 양식지 그림을 읽는다 — <b>미리보기 배경 전용</b> (§13-8).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>[2026-10-24 반려 R-2·R-4 봉합]</b> 종전 판은 두 가지가 틀렸다.
    /// <para>
    /// <b>R-2 — 「못 읽으면 종전과 똑같이」가 거짓이었다.</b> try/catch 가 <b>파일 읽기만</b>
    ///   감쌌고, 그림을 실제로 <b>그리는</b> <c>GeneratePdf()</c> 는 try 밖이었다.
    ///   검증팀 실측: 디코드 안 되는 바이트에 QuestPDF 가 <b>3/3 예외</b>를 던졌다.
    ///   ⇒ <c>.pdf</c>·<c>.svg</c>·0바이트를 가리키면 그 양식의 <b>인쇄·이메일·팩스가 전부 500</b>
    ///   이 된다(#20 — 워크플로우 흐름은 절대 안 끊긴다).
    ///   🔴 그래서 <b>여기서 미리 디코드해 본다.</b> 여기서 터지면 <c>null</c> 을 돌려주고,
    ///   종이는 <b>그림 없이</b> 나간다. 그리는 자리까지 끌고 가지 않는다.
    /// </para>
    /// <para>
    /// <b>R-4 — 경로가 안 갇혀 있었다.</b> 고객이 본문에 적은 문자열로 서버 파일을
    ///   아무거나 읽을 수 있었다(UNC·<c>file://</c> 가 http 차단을 비켜 갔다).
    ///   ⇒ <b>정해진 폴더 밑</b>만 · <b>그림 확장자</b>만 · <b>크기 상한</b>까지 건다
    ///   (§13-5 합격선 5 — 형식·크기 제한).
    /// </para>
    /// 🔴 <b>읽기만 한다</b> — 보관소·업로드 통로는 여전히 만들지 않는다(범위 밖).
    /// </remarks>
    private byte[]? TryLoadFormBackground(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // ① 바깥 그물 금지 — 인쇄가 남의 서버 응답 시간에 묶이면 고객 장사가 멈춘다.
        //    UNC(\호스트\…) 도 같은 이유로 막는다(R-4 — http 차단을 비켜 가던 길).
        if (url.Contains("://", StringComparison.Ordinal) || url.StartsWith(@"\\", StringComparison.Ordinal))
        {
            _logger.LogWarning("[PDF] 양식지 그림 주소가 바깥·공유 경로다 — 깔지 않는다.");
            return null;
        }

        // ② 그림 확장자만. .pdf·.svg·.exe 따위는 여기서 끝낸다.
        var ext = Path.GetExtension(url);
        if (!AllowedBackgroundExts.Contains(ext))
        {
            _logger.LogWarning("[PDF] 양식지 그림 형식이 아니다({Ext}) — 깔지 않는다.", ext);
            return null;
        }

        try
        {
            // ③ 경로 가둠 — 정해진 폴더 밑이 아니면 안 읽는다(R-4).
            //    `..\..\` 같은 거슬러 오르기는 FullPath 로 펴서 비교하므로 통하지 않는다.
            var full = Path.GetFullPath(url);
            // 🔴 [2026-10-24 재검증 R-4 봉합] 종전에는 **구분자 없이** 앞글자만 비교해
            //   **형제 폴더가 통째로 통과**했다(검증팀 실측: `…\form-backgrounds-probe\a.png`
            //   가 70바이트 읽혔다). 뒤에 구분자를 붙여 **그 폴더 안인지**를 본다.
            var root = Path.GetFullPath(FormBackgroundRoot).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[PDF] 양식지 그림이 허용 폴더 밖이다 — 깔지 않는다.");
                return null;
            }

            if (!File.Exists(full))
            {
                _logger.LogWarning("[PDF] 양식지 그림 파일이 없다 — 그림 없이 그린다.");
                return null;
            }

            // ③-2 🔴 [2026-10-24 재검증 R-10 봉합] 링크로 울타리를 넘는 길을 막는다.
            //   `Path.GetFullPath` 는 **링크를 풀지 않는다.** 폴더 안에 junction 하나를
            //   만들어 두면 ③ 의 울타리는 통과하고 **실제 파일은 바깥**이 된다.
            if (PassesThroughLink(full, root))
            {
                _logger.LogWarning("[PDF] 양식지 그림 경로가 링크를 지난다 — 깔지 않는다.");
                return null;
            }

            // ④ 크기 상한 — 거대 그림은 종이 한 장 때문에 PC 메모리를 먹는다.
            var len = new FileInfo(full).Length;
            if (len <= 0 || len > MaxBackgroundBytes)
            {
                _logger.LogWarning("[PDF] 양식지 그림 크기가 범위 밖이다({Len} bytes) — 깔지 않는다.", len);
                return null;
            }

            var bytes = File.ReadAllBytes(full);

            // ⑤ 🔴 여기서 **미리 그려 본다**(R-2). 작성기가 받아들이지 못하는 바이트면
            //    지금 터뜨리고 null 로 돌려준다 — 인쇄 한복판에서 터지면 전표가 안 나간다.
            try
            {
                _ = QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PDF] 양식지 그림을 작성기가 못 읽는다 — 그림 없이 그린다.");
                return null;
            }

            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] 양식지 그림 읽기 실패 — 그림 없이 그린다.");
            return null;
        }
    }

    /// <summary>
    /// 🔴 경로가 <b>링크(symlink·junction)</b> 를 지나는가 — 지나면 읽지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 필요한가</b> — 검증팀 2차 재검증 R-10: <c>Path.GetFullPath</c> 는 링크를
    ///   풀지 않는다. 그래서 허용 폴더 <b>안에</b> junction 을 하나 만들어 두면
    ///   앞글자 비교는 통과하고 실제 파일은 디스크 어디든 될 수 있다.
    /// 🔴 <b>울타리 자체(root)는 보지 않는다</b> — 그 자리는 관리자가 정한 곳이고,
    ///   거기가 링크여도 그것은 관리자의 뜻이다. 보는 것은 <b>울타리 안쪽</b>뿐이다.
    /// 🔴 못 재면 <b>막는 쪽</b>으로 떨어진다 — 종이는 그림 없이 나가고 전표는 산다(#20).
    /// </remarks>
    private static bool PassesThroughLink(string fullPath, string root)
    {
        var rootNoSep = root.TrimEnd(Path.DirectorySeparatorChar);

        var file = new FileInfo(fullPath);
        if (file.LinkTarget is not null) return true;

        for (var dir = file.Directory;
             dir is not null && dir.FullName.Length > rootNoSep.Length;
             dir = dir.Parent)
        {
            if (!dir.FullName.StartsWith(rootNoSep, StringComparison.OrdinalIgnoreCase)) break;
            if (dir.LinkTarget is not null) return true;
        }

        // 🔴🔴 [2026-10-25 3차 재검증 T-1 봉합] **뿌리 자신도 본다** — 위 주석의 면제를 거둔다.
        //   [틀린 전제] *"울타리 자체는 관리자가 정한 곳이니 링크여도 관리자의 뜻"* 이라고 적었다.
        //     🔴 이 뿌리는 **코드 고정값이고 앱이 스스로 만든다.** 관리자가 고른 자리가 아니다.
        //   🔴 검증팀 실측 ACL: `C:\ProgramData\HitPan` 에 `BUILTIN\Users:(I)(CI)(WD,AD,…)`
        //     ⇒ **일반 사용자가 하위 폴더를 만들 수 있다** ⇒ 갓 설치본에서 직원이
        //     `form-backgrounds` 를 먼저 junction 으로 만들어 두면 울타리가 통째로 비켜진다.
        //   🔴 올리기·지우기 쪽(FormBackgroundStore)과 **같이** 닫는다 — 한쪽만 고치면 또 비대칭이다.
        var rootDir = new DirectoryInfo(rootNoSep);
        if (rootDir.LinkTarget is not null) return true;

        return false;
    }

    /// <summary>
    /// 양식지 그림으로 받아 주는 형식. 🔴 이 밖은 작성기까지 가지 않는다(R-2).
    /// 🔴 [2026-10-24 업로드 통로 신설] 목록을 <b>여기서 적지 않는다</b> —
    ///   올리는 쪽과 읽는 쪽이 어긋나면 고객은 「올라갔는데 안 나온다」를 본다.
    /// </summary>
    private static HashSet<string> AllowedBackgroundExts => FormBackgroundPaths.AllowedExts;

    /// <summary>
    /// 양식지 그림 크기 상한. 🔴 올리는 쪽과 <b>같은 값</b>을 쓴다(FormBackgroundPaths).
    /// </summary>
    private const long MaxBackgroundBytes = FormBackgroundPaths.MaxBytes;

    /// <summary>
    /// 🔴 양식의 <b>용지 크기·방향</b>을 종이에 옮긴다 (20261024 사장님 결재).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 생겼나</b> — 작성기에 <c>PageSizes.A4</c> 가 못으로 박혀 있었다.
    ///   고객이 편집기에서 A5·가로를 골라 <b>저장까지 되는데</b> 종이는 늘 A4 세로였다.
    ///   「저장은 되는데 아무 일도 안 일어나는 칸」이 네 개 있었고 그중 둘이 이것이다.
    ///
    /// 🔴 <b>모르는 값은 A4·세로로 떨어진다</b> — 오타나 새 값이 와도 종이는 나간다(#20).
    ///   막는 쪽으로 떨어지면 전표가 안 나가고, 그건 고객 장사가 멈추는 것이다.
    ///
    /// 🔴 <b>세금계산서 전용 작성기는 여기를 타지 않는다</b> — 법정 서식이라 A4 고정이다
    ///   (부가가치세법 시행규칙 · D-5 교훈). 그 쪽을 바꾸려면 법무팀장 결재가 먼저다.
    /// </remarks>
    private PageSize ResolvePageSize(FormTemplateInfo? template,
        string? documentType = null, bool useCoords = false)
    {
        // 🔴🔴 [2026-10-25 [4] 반려 R-5 봉합] **법정 서식은 용지를 바꾸지 않는다.**
        //   [내가 뭘 틀렸나] 세금계산서 전용 작성기로 가는 분기는 `&& !isPreprint` 다.
        //     그래서 **양식용지 모드의 세금계산서는 이 공용 흐름으로 온다** — 전용 작성기를
        //     A4 로 묶어 둔 것만 보고 "세금계산서는 A4 고정 그대로" 라고 적었는데, 절반만 참이었다.
        //     고객이 양식용지 + A5 를 고르면 **법정 서식이 A5 로 나간다**(부가가치세법 시행규칙).
        //   🔴 그래서 종류로 막는다 — 모드와 무관하게 세금계산서는 A4 세로다.
        //     바꾸려면 법무팀장 결재가 먼저다(D-5 와 같은 자리).
        //   🔴 [2026-10-25 재검증 S-3 봉합] 1차 봉합은 `tax_invoice` **하나만** 막았다.
        //     변환표에는 `invoice_exempt`(면세계산서)·`payment_receipt`(입금표)도 있고
        //     이것들도 계산서류다 ⇒ **법정성 판정은 법무팀장 몫**이고, 그 전까지는
        //     **막는 쪽**에 둔다. 모르고 풀어 두면 법정 서식이 A5 로 나가는데,
        //     모르고 막아 두면 고객이 용지를 못 바꾸는 것뿐이다 — 되돌릴 수 있는 쪽을 고른다.
        if (LegalFormDocTypes.Contains(documentType ?? string.Empty))
        {
            if (!IsA4Portrait(template))
            {
                _logger.LogWarning(
                    "[PDF] {DocType} 은 법정 서식이라 용지·방향을 바꾸지 않는다 — A4 세로로 그린다.",
                    documentType);
            }

            return PageSizes.A4;
        }

        // 🔴🔴 [2026-10-25 [4] 반려 R-4 봉합] **좌표를 맞춰 둔 양식용지는 종이를 안 바꾼다.**
        //   [무엇이 일어났나] 좌표 인쇄는 종이 왼쪽 위가 원점이고 여백이 0 이다. 고객이 A4
        //     양식용지에 자를 대고 맞춘 mm 좌표가 있는데 용지를 A5 로 줄이면, 종이 밖으로
        //     나간 값은 **그냥 안 그려진다.** 오류도 안 나고 경고도 없다 ⇒ **조용히 빈 종이.**
        //     이번 차수에서 가장 위험한 자리였다(고객은 전표가 안 나간 줄도 모른다).
        //   🔴 좌표는 **어느 종이에 대고 쟀는지**가 전제다. 그 전제를 코드가 혼자 바꾸지 않는다.
        //     용지를 바꾸려면 고객이 좌표를 다시 맞춰야 하고, 그건 화면이 안내할 일이다(등재).
        //   🔴 [2026-11-19 W-G1-51 갈래 A 고침1 · [4] #6] 로그 문구만 「양식용지 좌표」 → 「좌표 양식」 —
        //     이제 빈 A4 Y 템플릿(그림 있는 빈 A4)도 이 길을 탄다. 판정 · 반환 값 무변 · 고객 노출 0.
        if (useCoords && !IsA4Portrait(template))
        {
            _logger.LogWarning(
                "[PDF] 좌표 양식(양식지 · 빈 A4 + 양식 그림)은 칸 좌표가 맞춰져 있어 용지·방향을 바꾸지 않는다 — A4 세로로 그린다. "
                + "용지를 바꾸려면 좌표를 다시 맞춰 주십시오.");
            return PageSizes.A4;
        }

        var size = (template?.PaperSize ?? "A4").Trim();

        var baseSize = size.ToUpperInvariant() switch
        {
            "A4" => PageSizes.A4,
            "A5" => PageSizes.A5,
            "LETTER" => PageSizes.Letter,
            _ => FallbackPageSize(size),
        };

        // 가로는 **같은 종이를 눕히는 것**이다 — 다른 종이를 고르는 것이 아니다.
        var landscape = string.Equals(
            (template?.Orientation ?? "portrait").Trim(), "landscape",
            StringComparison.OrdinalIgnoreCase);

        return landscape ? baseSize.Landscape() : baseSize;
    }

    /// <summary>
    /// 🔴 <b>용지·방향을 바꾸지 않는 종류</b> — 법정 서식(또는 그 후보).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 목록인가</b> — 2026-10-25 재검증 S-3. 1차 봉합은 세금계산서 하나만 막았는데
    ///   변환표에는 <c>invoice_exempt</c>(면세계산서)·<c>payment_receipt</c>(입금표)도 있다.
    /// 🔴 <b>법정성 판정은 법무팀장 몫이다</b>(등재). 판정이 날 때까지는 <b>막는 쪽</b>에 둔다 —
    ///   틀리게 막으면 고객이 용지를 못 바꾸는 것뿐이고, 틀리게 풀면 <b>법정 서식이 A5 로 나간다.</b>
    ///   되돌릴 수 있는 쪽을 고른다.
    /// 🔴 <b>여기서 종류 이름을 손으로 적는다</b> — 긁을 수 있는 자리가 아니다(법적 판단이라서).
    ///   그래서 게이트가 <b>변환표 전건</b>을 돌며 「막히나 / 안 막히나」를 표로 굳힌다.
    /// </remarks>
    private static readonly HashSet<string> LegalFormDocTypes =
        new(StringComparer.Ordinal) { TaxInvoiceDocType, "invoice_exempt", "payment_receipt" };

    /// <summary>
    /// 이 양식이 <b>A4 세로</b>를 그대로 쓰고 있나 — 🔴 법정 서식·좌표 양식지 보호 판정에 쓴다.
    /// </summary>
    /// <remarks>
    /// 🔴 「기본값 그대로」와 「A4 를 골라 둠」을 <b>같게 본다</b> — 둘 다 종이가 안 바뀐다.
    ///   그래서 바꾸지 않았을 때는 경고도 남기지 않는다(쓸데없는 경고는 다음 사람이 끈다).
    /// </remarks>
    private static bool IsA4Portrait(FormTemplateInfo? template)
    {
        var size = (template?.PaperSize ?? "A4").Trim();
        var orientation = (template?.Orientation ?? "portrait").Trim();

        return size.Equals("A4", StringComparison.OrdinalIgnoreCase)
            && !orientation.Equals("landscape", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>모르는 용지값이 왔을 때 — 🔴 조용히 넘기지 않고 적어 둔 뒤 A4 로 간다.</summary>
    private PageSize FallbackPageSize(string size)
    {
        _logger.LogWarning(
            "[PDF] 모르는 용지 크기({Size}) — A4 로 그린다. 양식 설정을 확인해 주십시오.", size);
        return PageSizes.A4;
    }

    /// <summary>
    /// 🔴 디자인 스타일 칸을 <b>읽는다</b> (DB-90 · 20261024 <b>배선만</b>).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>지금은 무엇이 와도 <c>basic</c> 으로 그린다.</b> 출하 DDL 주석이 그렇게 적혀 있다
    ///   — <i>"4종 확정 전까지 basic 단일"</i>. 스타일 4종은 <b>디자인 미확정</b>이고,
    ///   그 그림을 여기서 발명하면 결재 없이 디자인을 정하는 것이 된다.
    /// 🔴 그래서 이 함수가 하는 일은 하나다 — <b>종전에는 아무도 안 읽던 칸을 읽고,
    ///   모르는 값이면 적어 둔다.</b> 4종이 확정되는 날 이 자리가 갈림길이 된다.
    /// 🔴🔴 [2026-11-20 W-G1-51 갈래 E⑵] <b>위 「지금은 무엇이 와도 basic」 은 낡았다</b>(이력으로 둔다) —
    ///   4종 시안 승인(대표 §11 #1). 이제 허용 4개(<c>basic</c> · <c>formal</c> · <c>simple</c> · <c>large</c> · 대소문자 무시)는
    ///   소문자 키로 돌려준다. 모르는 값 · 빈 값은 <b>지금 그대로</b>(모르는 값 = 경고 한 줄 + basic · 빈 값 = basic).
    ///   🔴 이 함수는 경고만 맡는다 — 그리는 값 묶음은 <see cref="ResolvePrintStyle"/>(순백지에만)이 정한다.
    /// 🔴 [2026-11-20 갈래 E⑵ 고침 2 · E⑷ 작업지시서 E4-6 · §7] 위에 옮겨 적은 출하 DDL 주석 <i>"4종 확정 전까지 basic 단일"</i> 도 낡았다(이력으로 둔다) —
    ///   그 주석 새 글자 = <i>"디자인 스타일 (DB-90). basic·formal·simple·large — 빈 A4 순백지이고 양식 그림이 없는 양식에만 쓰임(그 밖은 basic 모양)"</i>
    ///   (E⑷ §7 · DDL 주석만 · 마이그 0 · DDL 손질은 E⑷ 몫).
    /// </remarks>
    private string ResolveStyleKey(FormTemplateInfo? template)
    {
        // ⬛ 종전 몸통(2026-11-20 갈래 E⑵ 전) — basic 외 값은 모두 경고 뒤 basic:
        // ⬛ var key = (template?.StyleKey ?? "basic").Trim();
        // ⬛ if (key.Length == 0) return "basic";
        // ⬛ if (!string.Equals(key, "basic", StringComparison.OrdinalIgnoreCase))
        // ⬛ {
        // ⬛     _logger.LogWarning(
        // ⬛         "[PDF] 아직 준비되지 않은 양식 스타일({Key}) — 기본 모양으로 그린다.", key);
        // ⬛     return "basic";
        // ⬛ }
        // ⬛ return "basic";
        var known = NormalizeStyleKey(template?.StyleKey);
        if (known is null)
        {
            _logger.LogWarning(
                "[PDF] 아직 준비되지 않은 양식 스타일({Key}) — 기본 모양으로 그린다.",
                (template?.StyleKey ?? "").Trim());
            return "basic";
        }

        return known;
    }

    /// <summary>스타일 키 허용 목록(시안 §3-1 · 소문자).</summary>
    private static readonly string[] KnownStyleKeys = { "basic", "formal", "simple", "large" };

    /// <summary>
    /// 🔴 스타일 키를 허용 목록 소문자로 고른다 — 빈 값 = <c>basic</c> · 모르는 값 = <c>null</c>. <b>로그 0</b>
    /// (경고는 <see cref="ResolveStyleKey"/> 한 자리에서만 — 두 번 부르면 경고가 두 줄 찍힌다).
    /// </summary>
    private static string? NormalizeStyleKey(string? raw)
    {
        var key = (raw ?? "basic").Trim();
        if (key.Length == 0) return "basic";
        foreach (var k in KnownStyleKeys)
        {
            if (string.Equals(key, k, StringComparison.OrdinalIgnoreCase)) return k;
        }
        return null;
    }

    /// <summary>
    /// 🔴 이 인쇄에 스타일 값 묶음을 쓰나 — 쓰면 그 묶음 · 아니면 <c>null</c>(= basic = 옛 작성기 그대로).
    /// </summary>
    /// <remarks>
    /// 🔴 [2026-11-20 W-G1-51 갈래 E⑵] 다섯 조건이 <b>모두</b> 참일 때만 묶음을 준다:
    ///   ① 양식 행 있음 ② <c>paper_mode</c> = <c>"plain"</c>(순백지 · 글자 그대로 · 대표 §9 #1 · 출하 DDL 「plain=A4 순백지」)
    ///   ③ 이번 인쇄가 좌표 경로가 아님(<paramref name="useCoords"/> 거짓 — 양식지 · Y 템플릿 = 무시 · 시안 PM Q-C)
    ///   ④ 양식 그림 경로가 빈 칸(설계 「빈 A4 · 그림 0 · 좌표 0」 · 대표 §59 K3 「그림 + 칸 0」 과 겹침 0)
    ///   ⑤ 스타일 키 = <c>formal</c> · <c>simple</c> · <c>large</c>.
    /// 🔴 ③ 을 저장된 칸 수가 아니라 <paramref name="useCoords"/> 로 보는 까닭: 설정 미리보기에서 편집창이
    ///   빈 A4 좌표를 null 로 보낸다 — 저장 칸 수로 가르면 옛 좌표만 남은 빈 A4(K4)의 미리보기 ≠ 종이가 된다.
    /// 🔴 <b>로그 0</b> — 모르는 키 경고는 <see cref="ResolveStyleKey"/> 가 이미 적는다.
    /// 🔴🔴 [2026-11-20 갈래 E⑵ 고침 1 · PM 판단 §72 E-1 · [3-V] P1-1] 조건 ⑥ 을 더했다 —
    ///   ⑥ 문서 종류가 <see cref="LegalFormDocTypes"/>(세금계산서 · 면세계산서 · 입금표)가 <b>아님</b>.
    ///   위 「다섯 조건」 은 이제 여섯이다(이력으로 둔다). 용지 판정(<see cref="ResolvePageSize"/>)과 <b>같은 목록</b>을 그대로 쓴다
    ///   — 새 목록 0. 법정성 판정은 법무팀장 몫이고 그 전까지는 <b>막는 쪽</b>(2026-10-25 재검증 S-3 과 같은 까닭).
    ///   법무 판정이 나오면 이 한 줄로 다시 연다.
    /// </remarks>
    // ⬛ 종전 서명(갈래 E⑵ 고침 1 전): private static PrintStyle? ResolvePrintStyle(FormTemplateInfo? template, bool useCoords)
    private static PrintStyle? ResolvePrintStyle(FormTemplateInfo? template, bool useCoords, string? documentType)
    {
        if (template is null || useCoords) return null;
        if (LegalFormDocTypes.Contains(documentType ?? string.Empty)) return null;
        if (!string.Equals(template.PaperMode, "plain", StringComparison.Ordinal)) return null;
        if (!string.IsNullOrWhiteSpace(template.BackgroundImageUrl)) return null;
        return NormalizeStyleKey(template.StyleKey) switch
        {
            "formal" => FormalStyle,
            "simple" => SimpleStyle,
            "large" => LargeStyle,
            _ => null,
        };
    }

    /// <summary>
    /// 🔴🔴 이 인쇄가 대표 §59 K3 라 <b>그림만</b> 찍나 — 참이면 머리글 · 본문 · 쪽번호를 건너뛴다(그림 겹은 그대로).
    /// </summary>
    /// <remarks>
    /// 🔴 [2026-11-20 W-G1-51 갈래 §59 · 대표 §59 「그림만 + 안내」 · 작업지시서 §1 · PM 결재 ① (가) · ⑥ (나) · ⑦]
    ///   K3 = 아래가 <b>모두</b> 참:
    ///   ① <c>paper_mode</c> = <c>"plain"</c> ② <b>저장된</b> 그림 찍기 = 켬 ③ 그림 경로 있음 ④ 그 그림이 이번 인쇄에 실제로 읽힘
    ///   — ①~④ 는 렌더의 <paramref name="isYTemplate"/> 그대로 받는다(새 판정을 따로 짓지 않음 · 어긋남 0)
    ///   ⑤ 칸 좌표 0개(<c>useCoords</c> 앞 조건과 같은 셈 — K3 = <c>isYTemplate &amp;&amp; 칸 0</c> 은 Y 좌표 경로의 정확한 여집합)
    ///   🔴 §59 고침 1(작업지시서 ②-9 · P2-1) — 고침 1 부터 ⑤ = <b>저장된 칸 0</b>(<see cref="FormCoordsStored.IsEmpty"/>) · 모두 무효 칸은 K3 아님(종전 기본 배치 + 데이터). 위 줄 「정확한 여집합」 은 이력으로 둔다 — 무효 칸 양식은 Y 좌표 경로도 K3 도 아니다.
    ///   ⑥ 법정 서식(또는 그 후보) <see cref="LegalFormDocTypes"/>(세금계산서 · 면세계산서 · 입금표)가 아님 — PM ⑥ (나) · 법무 판정 전까지 막는 쪽
    ///   ⑦ 전표 [인쇄] 문에서 부름(<see cref="PdfPreviewOverrides.NoCoordsImageOnly"/>) · 설정 미리보기(<c>IsFormDesignPreview</c>) 아님.
    /// 🔴 <b>판정 유일 자리</b> — 렌더(<c>RenderDocumentAsync</c>)와 인쇄 미리보기 창의 안내 문(<see cref="IsNoCoordsImageOnlyAsync"/>)이
    ///   이 함수만 부른다. 화면이 양식 행을 읽어 스스로 셈하는 두 번째 판정은 없다(PM 결재 ②).
    /// 🔴 메일 · 팩스 · 설정 미리보기 · 4인자 호출은 ⑦ 이 거짓 ⇒ 지금 그대로(#1).
    /// 🔴 스타일(<see cref="ResolvePrintStyle"/>)과 겹침 0 — K3 는 ③ 참이라 스타일이 이미 null 이다. 스타일 키는 이 판정에 없다.
    /// 🔴 <b>로그 0</b>.
    /// 🔴 작업지시서 §3-1 글자는 <c>internal static</c> 이다 — <see cref="FormTemplateInfo"/> 가 <c>private</c> 중첩형이라
    ///   <c>internal</c> 메서드 인자로 쓰면 빌드가 안 된다(CS0051) ⇒ <c>private static</c>(시험은 반사로 부른다 · 개발명세서 §2-4 다-1).
    /// </remarks>
    private static bool IsNoCoordsImageOnly(string documentType, PdfPreviewOverrides? overrides, bool isYTemplate,
        FormTemplateInfo? template)
        => overrides?.NoCoordsImageOnly == true
           && overrides.IsFormDesignPreview != true
           && isYTemplate
           && (template?.FieldCoords.Count ?? 0) == 0
           && FormCoordsStored.IsEmpty(template?.FieldCoordsJson)   // §59 고침 1(②-7) — 저장된 칸 0 · 위 줄은 그대로 둠
           && !LegalFormDocTypes.Contains(documentType);

    /// <summary>
    /// 🔴 양식지 그림을 읽어 올 수 있는 <b>유일한 폴더</b> (R-4 경로 가둠).
    /// 보관소·업로드 통로가 생기면 그 통로가 여기에 넣는다 — 그때 <b>이 코드는 안 바뀐다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 [2026-10-24] 업로드 통로가 생겼다 — 그 통로가 <b>이 폴더 밑 테넌트 칸</b>에 넣는다.
    ///   약속대로 <b>이 울타리 코드는 바뀌지 않았다</b>(테넌트 폴더는 뿌리 안이라 그대로 통과한다).
    /// </remarks>
    private static string FormBackgroundRoot => FormBackgroundPaths.Root;



    /// <summary>
    /// 인장 그림을 읽는다. 🔴 지금은 보관소가 없어 <c>seal_url</c> 이 늘 비어 있다(설계 §8-2)
    /// ⇒ 합격선 ④ 는 이번 차수 <b>미달</b>이다. 보관소가 생기는 날 <b>코드 수정 0</b> 으로 켜진다.
    /// 🔴 이 함수는 <b>읽기만</b> 한다 — 파일 보관소·업로드 통로를 만들지 않는다(범위 밖).
    /// </summary>
    /// <summary>
    /// 🔴🔴 <b>[2026-10-26작1 §B-3 P0 봉합]</b> 인장 그림을 읽는다 — 울타리를 지나서.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>종전 판은 <c>File.Exists</c> → <c>File.ReadAllBytes</c> 가 전부였다.</b>
    ///   울타리 0개 · 사전 디코드 0건. 그때는 <c>seal_url</c> 이 <b>늘 비어</b> 있어
    ///   한 번도 안 터졌다 — <b>보관소가 없다는 것이 유일한 방어였다.</b>
    ///   🔴 그런데 오늘 보관소를 만들었다(결-H). 그 순간 10/24 의 P0 가
    ///   <b>글자 하나 안 다르게 재현</b>된다: 못 읽는 바이트를 작성기까지 끌고 가면
    ///   <c>GeneratePdf()</c> 가 예외를 던지고 <b>그 양식의 인쇄·이메일·팩스가 전부 500</b> 이 된다
    ///   (#20 — 워크플로우 흐름은 절대 안 끊긴다).
    /// 🔴 <b>시그니처는 건드리지 않았다</b> — 인자를 더하면 반사로 부르는 시험들이 깨진다
    ///   (10/25 작2 가 기본값 있는 인자 하나로 회귀를 157→265 로 깼다).
    /// 🔴 못 읽으면 <c>null</c> 을 돌려주고 종이는 <b>글자 도장</b>으로 나간다 — 종전과 같다.
    /// </remarks>
    private byte[]? TryLoadSealImage(string? sealUrl)
        => TryLoadCompanyImage(sealUrl, "인장", "글자 도장으로 그린다");

    /// <summary>
    /// 🔴 회사 로고 그림을 읽는다 — <b>워터마크의 재료</b> (20261026작1 · DB-148).
    /// </summary>
    /// <remarks>못 읽으면 <c>null</c> 이고, 워터마크는 <b>그냥 안 깔린다</b>. 종이는 나간다(#20).</remarks>
    private byte[]? TryLoadLogoImage(string? logoUrl)
        => TryLoadCompanyImage(logoUrl, "로고", "워터마크를 깔지 않는다");

    /// <summary>
    /// 🔴 워터마크 한 겹을 <b>SVG 글자</b>로 만든다 — 20261026작1 §C-2 (DB-148).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 SVG 인가</b> — QuestPDF 2026.2.4 에 <c>.Opacity()</c> 가 <b>없다</b>
    ///   (2026-10-26 실측: 컴파일 CS1061 · <c>QuestPDF.xml</c> 에 심볼 0건).
    ///   SVG 를 거치면 <b>새 꾸러미 0건</b>으로 명도·채도를 둘 다 줄 수 있다
    ///   (<c>opacity</c> + <c>feColorMatrix saturate</c>).
    ///
    /// 🔴🪤 <b><c>opacity</c> 는 반드시 <c>&lt;g&gt;</c> 에 건다.</b> 2026-10-26 실측:
    ///   <c>&lt;image opacity='0.12'&gt;</c> 는 <b>아무 경고 없이 무시된다</b>
    ///   (중앙픽셀 <c>R=255 G=0 B=0</c> — 대조군과 똑같았다).
    ///   <c>&lt;g opacity='0.12'&gt;</c> 로 감싸면 <c>R=255 G=223 B=223</c> 로 연해진다.
    ///   ⇒ 이 자리를 고치는 사람은 <b>낱말이 아니라 픽셀로</b> 다시 재라.
    ///
    /// 🔴 <b>SVG 글자가 깨지면 작성기가 예외를 던진다</b>(실측: <i>Cannot decode the provided
    ///   SVG image.</i>) ⇒ 글자는 <b>전부 우리가 만들고</b>, 고객 문자열은 <b>base64 말고는
    ///   아무것도 넣지 않는다</b>(파일 이름·경로 0건). 숫자는 범위로 조인 뒤
    ///   <b><c>InvariantCulture</c> 로 찍는다</b> — 서버 로캘이 유럽으로 잡힌 PC 에서
    ///   소수점이 <c>,</c> 로 찍히면 <b>모든 양식이 500</b> 이 된다.
    ///   (한국 로캘은 소수점이 <c>.</c> 라 ko-KR 에서는 안 터진다 — 실측 확인.
    ///    그래도 막아 둔다. 「우리 PC 에서는 된다」는 검증이 아니다.)
    ///
    /// 🔴 못 만들면 <c>null</c> — 워터마크만 빠지고 <b>종이는 나간다</b>(#20).
    /// </remarks>
    private string? TryBuildWatermarkSvg(byte[]? logoBytes, string? logoUrl, decimal opacity, decimal saturate)
    {
        if (logoBytes is null || logoBytes.Length == 0) return null;

        try
        {
            var mime = ResolveImageMime(logoUrl);
            if (mime is null)
            {
                _logger.LogWarning("[PDF] 로고 형식을 알 수 없다 — 워터마크를 깔지 않는다.");
                return null;
            }

            // 🔴 범위로 조인다 — 여기까지 온 값은 거절하지 않는다(#20).
            var op = WatermarkDefaults.ClampRatio(opacity);
            var sat = WatermarkDefaults.ClampRatio(saturate);

            // 🔴 InvariantCulture — 로캘이 바뀌어도 소수점은 '.' 다.
            var opText = op.ToString("0.##", CultureInfo.InvariantCulture);
            var satText = sat.ToString("0.##", CultureInfo.InvariantCulture);
            var b64 = Convert.ToBase64String(logoBytes);

            // 🔴 viewBox 를 1000x1000 으로 두고 그림을 그 안에 꽉 채운다 —
            //   실제 크기는 그리는 쪽에서 Width(mm) 로 정한다(용지 폭 대비 %).
            //   preserveAspectRatio 로 비율을 지킨다 — 로고가 찌그러지면 안 된다.
            return
                "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' "
                + "width='1000' height='1000' viewBox='0 0 1000 1000'>"
                + $"<defs><filter id='hpwm'><feColorMatrix type='saturate' values='{satText}'/></filter></defs>"
                // 🔴 opacity 는 <g> 에. <image> 에 걸면 무시된다(위 주석의 실측).
                + $"<g opacity='{opText}' filter='url(#hpwm)'>"
                + $"<image x='0' y='0' width='1000' height='1000' preserveAspectRatio='xMidYMid meet' "
                + $"xlink:href='data:{mime};base64,{b64}'/>"
                + "</g></svg>";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] 워터마크를 만들지 못했다 — 워터마크 없이 그린다.");
            return null;
        }
    }

    /// <summary>
    /// 그림 경로에서 <c>data:</c> 주소에 적을 형식을 고른다 — 🔴 <b>허용 목록 안에서만</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 확장자를 그대로 <c>data:image/{ext}</c> 로 이어 붙이지 않는다 — 그러면 고객이 준
    ///   글자가 SVG 안으로 들어간다. <b>우리가 아는 값으로만</b> 바꿔 적는다.
    /// </remarks>
    private static string? ResolveImageMime(string? url) =>
        Path.GetExtension(url ?? string.Empty).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => null
        };

    /// <summary>그릴 워터마크 한 겹 — <b>이미 열어 본</b> SVG 와 용지 짧은 변 대비 크기(%).</summary>
    /// <remarks>
    /// 🔴🔴 [2026-10-26 [3-V] P1-1 봉합] 종전에는 <b>SVG 글자</b>를 담아 그리는 자리로 넘겼다.
    ///   그리는 자리(<c>page.Background()</c> 안의 람다)는 <c>GeneratePdf()</c> 가 돌 때
    ///   실행되고, 그 호출에는 <c>try/catch</c> 가 <b>없다</b> ⇒ SVG 를 그때 못 열면
    ///   그 양식의 인쇄가 <b>전부 500</b> 이 된다(#20 — 흐름은 절대 안 끊긴다).
    ///   ⇒ 글자가 아니라 <see cref="SvgImage"/> 를 담는다. <b>여는 일을 미리</b> 해 두고,
    ///   못 열면 워터마크만 빠진 채 <b>종이는 나간다</b>.
    /// </remarks>
    private sealed record WatermarkLayer(SvgImage Svg, int ScalePct);

    /// <summary>
    /// 🔴 SVG 글자를 <b>미리 열어 본다</b> — 못 열면 <c>null</c>(워터마크만 빠진다).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 미리 여나</b> — 그리는 자리는 <c>GeneratePdf()</c> 안이고 그 자리에는
    ///   울타리가 없다. 보관소가 바이트를 미리 열어 보는 것(<c>Image.FromBinaryData</c>)과
    ///   <b>같은 까닭</b>이다. 10/24 P0 가 그 모양이었다.
    /// 🔴 2026-10-26 실측 — <c>.png</c>·<c>.jpg</c>·<c>.webp</c>·<c>.bmp</c> 네 형식 모두
    ///   중앙픽셀 <c>R=242 G=237 B=237</c> 로 <b>그려졌고</b>, 바이트가 그림이 아니면
    ///   <b>예외 없이 빈 자리</b>로 그려졌다(흰 종이). ⇒ 형식 때문에 터지는 길은 못 찾았다.
    ///   그래도 울타리를 둔다 — <b>「우리 PC 에서는 된다」는 검증이 아니다.</b>
    /// </remarks>
    private SvgImage? TryDecodeWatermarkSvg(string svg)
    {
        try
        {
            return SvgImage.FromText(svg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] 워터마크 SVG 를 열지 못했다 — 워터마크 없이 그린다.");
            return null;
        }
    }

    /// <summary>
    /// 🔴 이 종이에 워터마크를 깔 것인가, 깔면 어떤 모양인가 — 20261026작1 §C-2.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>판정 순서는 양식별 → 공통 → 제안값</b>이다(결-I: <i>공통 + 양식마다 — 단 고객 선택 옵션</i>).
    ///   켬/끔도 <b>세 값</b>이다 — <c>null</c>(공통을 따른다) · <c>1</c>(이 양식만 켠다) ·
    ///   <c>0</c>(이 양식만 끈다). 수치도 같은 뜻으로 <c>null</c> 이면 공통으로 떨어진다.
    ///
    /// 🔴🔴 [2026-10-26 [4] 반려 R-2 · [3-V] P1-2 봉합] 종전에는 이 칸이 <b>비널러블 bool</b>
    ///   이고 DDL 이 <c>NOT NULL DEFAULT 0</c> 이었다 ⇒ 「나는 정하지 않았다」를 적을 칸이
    ///   없었다. 그래서 <b>양식 행이 있는 고객에게 공통 켜기가 영영 안 닿았고</b>,
    ///   공통만 켠 고객이 양식에서 <b>여백만 고쳐 저장해도 워터마크가 조용히 사라졌다.</b>
    ///   ⇒ 칸을 지우지 않고 <c>NULL</c> 허용으로 <b>넓혔다</b>(#1·#37).
    ///
    /// 🔴 <b>이 양식이 명시적으로 껐으면</b> 공통 설정을 읽지도 않는다 — 그만큼 조회가 준다.
    ///   🔴 다만 <b>정하지 않은(<c>null</c>) 양식과 양식 행이 없는 문서종은 공통을 읽는다</b> —
    ///   전표 한 장마다 조회 1회다(테넌트 한 행 · PK 조회). 「조회를 아예 안 한다」가 아니다.
    ///
    /// 🔴🔴 <b>세금계산서는 이번 차수에서 깔지 않는다</b> — 사장님 결재 2026-10-26(결-K):
    ///   <i>"응 그렇게 해"</i>. 법무팀장 물음 6건(전자세금계산서에 공급자 로고를 정중앙에
    ///   허용? · 가독성 법적 기준? 등)이 **답을 못 받았다.** 법정 서식에 우리가 임의로
    ///   그림을 얹지 않는다. 🔴 <c>plain</c> 세금계산서는 전용 작성기로 가서 이 코드를
    ///   아예 안 타지만, <c>preprint</c> 세금계산서는 <b>공용 흐름으로 온다</b> —
    ///   그래서 여기서 한 번 더 막는다. 울타리는 「그럴 리 없다」로 세우지 않는다.
    ///
    /// 🔴 <b>로고가 없으면 아무 일도 하지 않는다</b> — 고객이 워터마크를 켜 두고 로고를
    ///   안 올렸을 수 있다. 그때 터지지 않고, 종이는 그냥 워터마크 없이 나간다(#20).
    ///   화면이 「로고를 먼저 올려 주십시오」를 말해 줘야 한다(갈래 D).
    /// </remarks>
    private async Task<WatermarkLayer?> BuildWatermarkLayerAsync(
        string tenantId, string documentType,
        FormTemplateInfo? template, CompanyInfo company, CancellationToken ct)
    {
        // 🔴 세 값이다 — null(공통 따름) · true(이 양식만 켬) · false(이 양식만 끔).
        var formSays = template?.ShowWatermark;

        // 🔴🔴 판정은 <see cref="ShouldLayWatermark"/> **한 자리**에서만 한다.
        //   여기서 같은 조건을 한 번 더 적으면 두 자리가 언젠가 어긋나고,
        //   어긋난 쪽은 늘 「고객이 겪는 쪽」이다.
        //   🔴 공통이 켜졌든 꺼졌든 안 깔릴 경우(세금계산서 · 이 양식만 끔)는
        //     **DB 를 읽지 않고** 끝낸다 — 전표 한 장마다 쓸데없는 조회를 안 한다.
        if (!ShouldLayWatermark(documentType, formSays, commonEnabled: false)
            && !ShouldLayWatermark(documentType, formSays, commonEnabled: true))
            return null;

        var common = await LoadPrintCommonAsync(tenantId, ct).ConfigureAwait(false);
        if (!ShouldLayWatermark(documentType, formSays, common.WatermarkEnabled)) return null;

        var logo = TryLoadLogoImage(company.LogoUrl);
        if (logo is null) return null;

        var opacity = template?.WatermarkOpacity ?? common.WatermarkOpacity;
        var saturate = template?.WatermarkSaturate ?? common.WatermarkSaturate;
        var scalePct = WatermarkDefaults.ClampScalePct(
            template?.WatermarkScalePct ?? common.WatermarkScalePct);

        var svg = TryBuildWatermarkSvg(logo, company.LogoUrl, opacity, saturate);
        if (svg is null) return null;

        // 🔴 [P1-1 봉합] **여기서 열어 본다** — 그리는 자리에는 울타리가 없다.
        var image = TryDecodeWatermarkSvg(svg);
        return image is null ? null : new WatermarkLayer(image, scalePct);
    }

    /// <summary>
    /// 🔴🔴 종이의 <b>배경 세 겹</b>을 조립한다 — 양식지 그림 · 주 겹 · 워터마크.
    /// </summary>
    /// <remarks>
    /// 🔴🔴 [2026-10-26 재검증 V-1 봉합] <b>왜 떼어냈나</b> — 게이트가 이 조립을
    ///   <b>시험 안에서 복제</b>하고 있었다. 그래서 제품의 조립이 R-1 버그(폭만 보기)로
    ///   되돌아가도 게이트는 <b>초록이었다</b>(재검증이 실제로 되살려 확인했다: 10/10 초록).
    ///   ⇒ 게이트가 <b>이 함수를 불러서</b> 그린다. 여기가 바뀌면 게이트가 바뀐 것을 잰다.
    ///
    /// 🔴🪤 <b><c>PrimaryLayer</c> 가 정확히 하나 있어야 한다</b> — 빠지면
    ///   <c>DocumentComposeException</c> 으로 그 양식의 인쇄가 <b>전부 500</b> 이다(G-W15 가 실증).
    /// 🔴 <b>짧은 변</b>이 기준이다(<see cref="ResolveWatermarkSideMm"/>) — 까닭은 그 함수에 적었다.
    ///   넘치면 줄어들도록 상한을 <b>가로·세로 둘 다</b> 건다.
    /// 🔴 둘 다 없으면 이 함수를 <b>부르지 않는다</b> — 끈 고객의 종이는 종전과 글자 하나 다르지 않다(#1).
    /// </remarks>
    internal static void ComposeDocumentBackground(
        PageDescriptor page, PageSize pageSize,
        byte[]? bgImage, SvgImage? watermarkSvg, int watermarkScalePct)
    {
        // 🔴 QuestPDF 의 크기는 point 다(1pt = 1/72 inch) — mm 로 바꾼다.
        var pageWidthMm = pageSize.Width * 25.4f / 72f;
        var pageHeightMm = pageSize.Height * 25.4f / 72f;

        page.Background().Layers(layers =>
        {
            // 1겹 — 양식지 그림. 종이 전체에 맞춘다.
            if (bgImage is not null)
            {
                layers.Layer().Image(bgImage).FitArea();
            }

            // 🔴 주 겹 — 배경 자리는 그릴 내용이 없으니 **비워서 늘린다.**
            layers.PrimaryLayer().Extend();

            // 2겹 — 워터마크. **정중앙** · 용지 짧은 변의 N%(사장님: "정중앙 크게").
            if (watermarkSvg is not null)
            {
                var sideMm = ResolveWatermarkSideMm(pageWidthMm, pageHeightMm, watermarkScalePct);
                layers.Layer()
                      .AlignCenter().AlignMiddle()
                      .MaxWidth(pageWidthMm, Unit.Millimetre)
                      .MaxHeight(pageHeightMm, Unit.Millimetre)
                      .Width(sideMm, Unit.Millimetre)
                      .Svg(watermarkSvg);
            }
        });
    }

    /// <summary>
    /// 🔴🔴 워터마크 정사각 한 변의 길이(mm) — <b>계산이 사는 한 자리</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 [2026-10-26 [4] 반려 R-1 봉합] 종전에는 그리는 자리에서 <c>폭 × %</c> 로 바로 곱했다.
    ///   워터마크 SVG 는 <b>1000×1000 정사각</b>이라 폭을 정하면 높이도 그만큼이 된다 ⇒
    ///   가로 용지(297×210)에서 80% 는 237mm 가 되어 종이 높이를 넘고, QuestPDF 는
    ///   <b>예외도 로그도 없이 그림을 아예 뺀다</b>(검증팀 실측: PDF 7.3KB · 래스터 백지).
    ///   ⇒ <b>짧은 변</b>을 기준으로 잡는다. 100% = 「종이에 꽉 차는 가장 큰 정사각」.
    /// 🔴 떼어 놓은 까닭은 <b>재기 위해서</b>다 — 그리는 자리는 DB 없이 못 부르지만
    ///   이 함수는 부를 수 있다(게이트 G-W16 이 가로·세로 × 60/80/100 을 전수로 잰다).
    /// </remarks>
    internal static float ResolveWatermarkSideMm(float pageWidthMm, float pageHeightMm, int scalePct)
    {
        var shorter = Math.Min(pageWidthMm, pageHeightMm);
        var side = shorter * WatermarkDefaults.ClampScalePct(scalePct) / 100f;

        // 🔴 마지막 그물 — 어떤 값이 와도 종이를 넘기지 않는다(넘으면 그림이 사라진다).
        return Math.Min(side, shorter);
    }

    /// <summary>
    /// 🔴🔴 이 종이에 워터마크를 <b>깔 것인가</b> — 판정이 사는 <b>한 자리</b>.
    /// </summary>
    /// <param name="documentType">문서종. 🔴 <c>tax_invoice</c> 는 결-K 로 <b>무조건 안 깐다</b>.</param>
    /// <param name="formSays"><c>null</c> 공통을 따른다 · <c>true</c> 이 양식만 켬 · <c>false</c> 이 양식만 끔.</param>
    /// <param name="commonEnabled">공통 설정의 켬/끔.</param>
    /// <remarks>
    /// 🔴 <b>왜 떼어 놨나</b> — 판정이 DB 조회와 뒤섞여 있어 <b>재 볼 수가 없었다</b>.
    ///   [4] 반려 R-2 와 [3-V] P1-2 가 모두 이 판정의 구멍이었는데, 그 구멍을 재는
    ///   시험이 0건이었다(「공통 켬 + 양식 행 있음」 조합을 아무도 안 그려 봤다).
    ///   ⇒ 순수 함수로 내려 <b>아홉 조합을 전수로</b> 잰다(게이트 G-W17).
    /// 🔴 조회를 아끼는 쪽(위 호출부의 두 번 호출)도 <b>이 함수로</b> 판정한다 —
    ///   조건을 두 번 적지 않는다.
    /// 🔴 [2026-10-26 재검증 V-8 봉합] 이 설명이 <b>옆 메서드에 붙어 있었다</b> —
    ///   새 메서드를 설명과 메서드 **사이에** 끼워 넣어 <c>&lt;summary&gt;</c> 가 둘이 되고
    ///   <c>&lt;param&gt;</c> 셋이 없는 인자를 가리켰다. 내가 같은 날 봉합한
    ///   「주석이 코드와 다르다」와 <b>같은 사고</b>다.
    /// </remarks>
    internal static bool ShouldLayWatermark(string documentType, bool? formSays, bool commonEnabled)
    {
        // 🔴 결-K — 법무 물음 6건이 안 닫혔다. 법정 서식에 우리가 임의로 그림을 얹지 않는다.
        if (string.Equals(documentType, TaxInvoiceDocType, StringComparison.Ordinal)) return false;

        // 🔴 양식이 **정했으면** 그 말을 따른다 — 공통보다 양식이 가깝다.
        if (formSays is not null) return formSays.Value;

        // 🔴 안 정했으면(기본) 공통을 따른다 — 이 한 줄이 R-2 의 봉합이다.
        return commonEnabled;
    }

    /// <summary>테넌트 공통 인쇄 설정 (<c>print_common_settings</c> · DB-148).</summary>
    private sealed class PrintCommonSettings
    {
        public bool WatermarkEnabled { get; set; }
        public decimal WatermarkOpacity { get; set; } = WatermarkDefaults.Opacity;
        public decimal WatermarkSaturate { get; set; } = WatermarkDefaults.Saturate;
        public int WatermarkScalePct { get; set; } = WatermarkDefaults.ScalePct;

        // 🔴 [2026-10-26 [3-V] P1-4 · [4] R-7 봉합] `print_background_default` 는 **안 읽는다.**
        //   이번 차수에 쓰는 자리가 없는 칸을 담아 두면, 다음 사람이 「되는 기능」으로 읽는다.
        //   칸은 DDL 에 **예약으로 남겨 두되**(#37 — 칸은 지우지 않는다) 코드는 안 건드린다.
    }

    /// <summary>
    /// 🔴 공통 인쇄 설정을 읽는다 — 행이 <b>없으면 제안값</b>으로 떨어진다 (DB-148 · 결-I).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>행이 없는 것이 정상 경로다</b> — 고객이 공통 설정을 한 번도 안 건드리면 행이 없다.
    ///   그때 터지거나 0 으로 떨어지면 「켰는데 안 보인다」가 된다.
    ///   ⇒ 없으면 <see cref="WatermarkDefaults"/> 를 쓴다(그 값은 DDL DEFAULT 와 같다).
    /// 🔴 못 읽어도 종이는 나간다 — 제안값으로 그린다(#20 · #15 까닭은 남긴다).
    /// </remarks>
    private async Task<PrintCommonSettings> LoadPrintCommonAsync(string tenantId, CancellationToken ct)
    {
        try
        {
            await EnsureOpenAsync(ct).ConfigureAwait(false);
            const string sql = """
                SELECT watermark_enabled AS WatermarkEnabled,
                       watermark_opacity AS WatermarkOpacity,
                       watermark_saturate AS WatermarkSaturate,
                       watermark_scale_pct AS WatermarkScalePct
                FROM print_common_settings WHERE tenant_id = @TenantId
                """;
            var s = await _db.QuerySingleOrDefaultAsync<PrintCommonSettings>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
                .ConfigureAwait(false);
            return s ?? new PrintCommonSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] 공통 인쇄 설정을 못 읽었다 — 제안값으로 그린다.");
            return new PrintCommonSettings();
        }
    }

    /// <summary>
    /// 🔴 회사 그림(로고·인장)을 <b>울타리를 지나</b> 읽는다 — 20261026작1 §B-3.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>울타리를 양식지 쪽(<c>TryLoadFormBackground</c>)과 같은 모양으로 세운다.</b>
    ///   그 다섯 겹은 검증팀이 2026-10-24~25 에 <b>실제로 뚫어 본</b> 자리들이다.
    ///   <b>새 보관소에서 같은 구멍을 다시 열지 않는다.</b>
    /// 🔴 <b>어느 겹에서 막혀도 종이는 나간다</b> — 그림만 빠지고 전표는 산다(#20).
    /// </remarks>
    private byte[]? TryLoadCompanyImage(string? url, string what, string fallback)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // ① 바깥 그물 금지 — 인쇄가 남의 서버 응답 시간에 묶이면 고객 장사가 멈춘다.
        //    UNC(\\호스트\…) 도 같은 이유로 막는다(http 차단을 비켜 가던 길이었다).
        if (url.Contains("://", StringComparison.Ordinal) || url.StartsWith(@"\\", StringComparison.Ordinal))
        {
            _logger.LogWarning("[PDF] {What} 주소가 바깥·공유 경로다 — {Fallback}.", what, fallback);
            return null;
        }

        // ② 그림 확장자만. .pdf·.svg·.exe 따위는 여기서 끝낸다.
        var ext = Path.GetExtension(url);
        if (!CompanyImagePaths.AllowedExts.Contains(ext))
        {
            _logger.LogWarning("[PDF] {What} 형식이 그림이 아니다({Ext}) — {Fallback}.", what, ext, fallback);
            return null;
        }

        try
        {
            // ③ 경로 가둠 — 정해진 폴더 밑이 아니면 안 읽는다.
            //    🔴 뒤에 구분자를 붙여 **그 폴더 안인지**를 본다 — 구분자 없이 앞글자만
            //    비교하면 **형제 폴더가 통째로 통과**한다(10/24 R-4 실측).
            var full = Path.GetFullPath(url);
            var root = Path.GetFullPath(CompanyImagePaths.Root).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[PDF] {What} 그림이 허용 폴더 밖이다 — {Fallback}.", what, fallback);
                return null;
            }

            if (!File.Exists(full))
            {
                _logger.LogWarning("[PDF] {What} 파일이 없다 — {Fallback}.", what, fallback);
                return null;
            }

            // ③-2 🔴 링크로 울타리를 넘는 길을 막는다. `Path.GetFullPath` 는 링크를 풀지 않는다.
            if (PassesThroughLink(full, root))
            {
                _logger.LogWarning("[PDF] {What} 경로가 링크를 지난다 — {Fallback}.", what, fallback);
                return null;
            }

            // ④ 크기 상한 — 🔴 로고는 워터마크로 base64 로 1.33배 늘어 모든 장에 들어간다.
            var len = new FileInfo(full).Length;
            if (len <= 0 || len > CompanyImagePaths.MaxBytes)
            {
                _logger.LogWarning("[PDF] {What} 크기가 범위 밖이다({Len} bytes) — {Fallback}.", what, len, fallback);
                return null;
            }

            var bytes = File.ReadAllBytes(full);

            // ⑤ 🔴 여기서 **미리 그려 본다.** 작성기가 못 받는 바이트면 지금 끝낸다 —
            //    인쇄 한복판에서 터지면 전표가 안 나간다(10/24 P0).
            try
            {
                _ = QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PDF] {What} 그림을 작성기가 못 읽는다 — {Fallback}.", what, fallback);
                return null;
            }

            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] {What} 읽기 실패 — {Fallback}.", what, fallback);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // 급여명세서 (20260826작6)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>급여명세서 문서타입. 거래문서 8종과 <b>구조가 달라</b> 전용 경로로 간다.</summary>
    public const string PayslipDocType = "payslip";

    private sealed class PayslipSnapshot
    {
        public string SlipId { get; set; } = "";
        public string EmpName { get; set; } = "";
        public string? EmpNo { get; set; }
        public string? DeptName { get; set; }
        public int PayYear { get; set; }
        public int PayMonth { get; set; }
        public DateTime? PayDate { get; set; }
        public decimal TotalPayment { get; set; }
        public decimal TotalDeduct { get; set; }
        public decimal NetPayment { get; set; }
        public string? Memo { get; set; }
        public List<PayslipLine> Payments { get; set; } = new();
        public List<PayslipLine> Deducts { get; set; } = new();
    }

    private sealed class PayslipLine
    {
        public string ItemName { get; set; } = "";
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 급여명세서 PDF. <b>금액은 저장된 값을 그대로 찍는다 — 계산하지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 사장님 수동입력 원칙(2026-08-13): <i>"급여는 자동계산하지 말고 수동으로 int값 직접
    /// 받아서 입력하는게 가장 깔끔함"</i>. 여기서도 <b>합계를 다시 구하지 않는다</b> —
    /// 화면에서 본 숫자와 종이의 숫자가 다르면 그 자체로 사고다.
    /// </remarks>
    private async Task<(byte[] Bytes, string FileName)> RenderPayslipAsync(
        string tenantId, string slipId, CancellationToken ct)
    {
        var data = await LoadPayslipAsync(tenantId, slipId, ct).ConfigureAwait(false);
        var company = await LoadCompanyAsync(tenantId, ct).ConfigureAwait(false);

        // ① 결재 — 양식을 자유롭게 붙일 수 있게 한다. 미설정 고객은 기본 모양으로 나온다.
        //   ⚠️ TryLoadTemplateAsync 의 documentType→form_type 표에 payslip 을 넣어야
        //     고객이 설정한 양식이 실제로 먹는다(안 넣으면 조용히 기본 모양 — 8/12 적발 사례).
        var template = await TryLoadTemplateAsync(tenantId, PayslipDocType, ct).ConfigureAwait(false);
        var showHeader = template?.ShowCompanyLogo ?? true;
        var showBorder = template?.ShowBorder ?? true;
        var marginTop = (float)(template?.MarginTopMm ?? 20);
        var marginLeft = (float)(template?.MarginLeftMm ?? 20);
        var marginRight = (float)(template?.MarginRightMm ?? 20);
        var marginBottom = (float)(template?.MarginBottomMm ?? 20);

        var bytes = Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(marginTop, Unit.Millimetre);
                page.MarginLeft(marginLeft, Unit.Millimetre);
                page.MarginRight(marginRight, Unit.Millimetre);
                page.MarginBottom(marginBottom, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(10));
                page.PageColor(Colors.White);

                if (showHeader)
                {
                    page.Header().Element(e => ComposePayslipHeader(e, data, company));
                }
                page.Content().Element(e => ComposePayslipBody(e, data, showBorder));
            });
        // 옛 줄(W-G1-51 PDF 잠금 전 · 헌법 #1): }).GeneratePdf();
        }).GeneratePdfOneAtATime();

        var fileName = $"급여명세서_{data.EmpName}_{data.PayYear}-{data.PayMonth:00}.pdf";
        return (bytes, fileName);
    }

    private async Task<PayslipSnapshot> LoadPayslipAsync(string tenantId, string slipId, CancellationToken ct)
    {
        var head = await _db.QueryFirstOrDefaultAsync<PayslipSnapshot>(new CommandDefinition(
            """
            SELECT s.slip_id       AS SlipId,
                   e.emp_name      AS EmpName,
                   e.emp_no        AS EmpNo,
                   d.dept_name     AS DeptName,
                   s.pay_year      AS PayYear,
                   s.pay_month     AS PayMonth,
                   s.pay_date      AS PayDate,
                   s.total_payment AS TotalPayment,
                   s.total_deduct  AS TotalDeduct,
                   s.net_payment   AS NetPayment,
                   s.memo          AS Memo
            FROM payroll_slips s
            LEFT JOIN employees   e ON e.employee_id = s.employee_id AND e.tenant_id = s.tenant_id
            LEFT JOIN departments d ON d.dept_id     = e.dept_id     AND d.tenant_id = s.tenant_id
            WHERE s.tenant_id = @TenantId AND s.slip_id = @SlipId
            """,
            new { TenantId = tenantId, SlipId = slipId }, cancellationToken: ct)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("급여명세서를 찾을 수 없습니다.");

        var lines = (await _db.QueryAsync<(string LineType, string ItemName, decimal Amount)>(new CommandDefinition(
            """
            SELECT line_type AS LineType, item_name AS ItemName, amount AS Amount
            FROM payroll_slip_lines
            WHERE tenant_id = @TenantId AND slip_id = @SlipId
            ORDER BY sort_order, item_name
            """,
            new { TenantId = tenantId, SlipId = slipId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        foreach (var (lineType, itemName, amount) in lines)
        {
            var row = new PayslipLine { ItemName = itemName, Amount = amount };
            // 공제(deduct) 외에는 전부 지급으로 본다 — 새 line_type 이 생겨도 항목이 사라지지 않게.
            if (string.Equals(lineType, "deduct", StringComparison.OrdinalIgnoreCase))
                head.Deducts.Add(row);
            else
                head.Payments.Add(row);
        }

        return head;
    }

    private static void ComposePayslipHeader(IContainer e, PayslipSnapshot d, CompanyInfo c)
    {
        e.Column(col =>
        {
            col.Item().AlignCenter().Text($"{d.PayYear}년 {d.PayMonth}월 급여명세서")
                .FontSize(16).Bold();
            col.Item().PaddingTop(4).AlignCenter().Text(c.CompanyName).FontSize(10);
            col.Item().PaddingBottom(6);
        });
    }

    private static void ComposePayslipBody(IContainer e, PayslipSnapshot d, bool showBorder)
    {
        e.Column(col =>
        {
            // ── 사원 정보 ──
            col.Item().PaddingBottom(8).Row(r =>
            {
                r.RelativeItem().Text($"성명: {d.EmpName}");
                r.RelativeItem().Text($"사번: {d.EmpNo ?? "-"}");
                r.RelativeItem().Text($"부서: {d.DeptName ?? "-"}");
                r.RelativeItem().Text($"지급일: {(d.PayDate.HasValue ? d.PayDate.Value.ToString("yyyy-MM-dd") : "-")}");
            });

            // ── 지급 / 공제 2단 ──
            col.Item().Row(r =>
            {
                r.RelativeItem().Element(x => ComposePayslipSection(x, "지급 내역", d.Payments, d.TotalPayment, showBorder));
                r.ConstantItem(12);
                r.RelativeItem().Element(x => ComposePayslipSection(x, "공제 내역", d.Deducts, d.TotalDeduct, showBorder));
            });

            // ── 실수령액 ──
            col.Item().PaddingTop(12).Background(Colors.Grey.Lighten3).Padding(8).Row(r =>
            {
                r.RelativeItem().Text("실수령액").FontSize(12).Bold();
                r.ConstantItem(160).AlignRight().Text($"{d.NetPayment:N0} 원").FontSize(14).Bold();
            });

            if (!string.IsNullOrWhiteSpace(d.Memo))
            {
                col.Item().PaddingTop(8).Text($"비고: {d.Memo}").FontSize(9);
            }
        });
    }

    private static void ComposePayslipSection(
        IContainer e, string title, List<PayslipLine> lines, decimal total, bool showBorder)
    {
        e.Column(col =>
        {
            col.Item().Background(Colors.Grey.Lighten2).Padding(4).Text(title).Bold();

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.ConstantColumn(90); });

                foreach (var line in lines)
                {
                    t.Cell().Element(x => PayslipCell(x, showBorder)).Text(line.ItemName);
                    t.Cell().Element(x => PayslipCell(x, showBorder)).AlignRight().Text($"{line.Amount:N0}");
                }

                if (lines.Count == 0)
                {
                    t.Cell().ColumnSpan(2).Element(x => PayslipCell(x, showBorder))
                        .AlignCenter().Text("-").FontSize(9);
                }

                t.Cell().Element(x => PayslipCell(x, showBorder)).Text("합계").Bold();
                t.Cell().Element(x => PayslipCell(x, showBorder)).AlignRight().Text($"{total:N0}").Bold();
            });
        });
    }

    private static IContainer PayslipCell(IContainer c, bool showBorder) =>
        showBorder ? c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4) : c.Padding(4);

    /// <summary>
    /// 고객이 맞춘 좌표대로 양식용지에 값만 얹는다 (사장님 지시 2026-08-12 "양식용지만 해").
    /// </summary>
    /// <remarks>
    /// 시판 양식지는 칸 위치가 종이마다 다르다. 종전에는 좌표를 <b>읽지도 않아</b> 코드에 고정된
    /// 자리로만 인쇄됐고, 그래서 칸이 안 맞아 이 기능을 사실상 못 썼다.
    ///
    /// 원점은 <b>종이 왼쪽 위 모서리</b>다. 그래서 이 경로만 페이지 여백을 0 으로 두고
    /// 값 하나하나를 절대 위치에 올린다(고객이 자로 잰 값이 그대로 통해야 한다).
    ///
    /// 품목 줄은 <c>line.품목명</c> 처럼 적으면 첫 줄 위치가 되고, 그 아래로
    /// <c>row_pitch_mm</c> 간격(기본 6mm)으로 반복한다 — 줄마다 좌표를 적게 하면 못 쓴다.
    /// </remarks>
    private static void ComposeBodyCoords(IContainer e, DocumentSnapshot d, List<FieldCoord> coords)
    {
        // 줄 간격: 고객이 row_pitch 라는 이름으로 한 번만 지정한다(값 없으면 6mm).
        var pitch = coords.FirstOrDefault(c =>
            c.Key.Equals("row_pitch", StringComparison.OrdinalIgnoreCase))?.YMm ?? 0f;
        if (pitch <= 0f) pitch = 6f;

        // 몇 줄까지 찍을 수 있나 — 넘치면 종이 밖으로 나가 조용히 사라진다.
        var maxRows = coords.FirstOrDefault(c =>
            c.Key.Equals("max_rows", StringComparison.OrdinalIgnoreCase))?.XMm ?? 0f;

        // 🔴 20261021작 절 D — 넘침 안내 한 줄.
        //   🔴 자르는 동작(limit)·max_rows·row_pitch 해석은 **한 글자도 안 바꿨다.**
        //   쪽 이어찍기는 이번 범위 밖(결재 Q-4)이라, 조용히 사라지던 것을 **말해 주기만** 한다.
        //   조용히 사라지는 것은 세금 사고다 — 적어도 고객이 알아야 한다.
        //   🔴 20261021작 §9-2 봉합(R-2) — **세금계산서 경로에서만** 나온다.
        //   반려 사유: 이 메서드는 10종이 **같이 타는** 길이라, 거래명세서·견적서 등에서도
        //   알림이 찍혔다 ⇒ 결재 Q-4 의 집행 조건 「나머지 9종 종이 무변화」가 깨졌다.
        //   🔴 자르는 동작·지우는 코드는 여전히 0건이다 — 나오는 **자리**만 좁힌다.
        var rowLimit = maxRows > 0 ? (int)maxRows : d.Lines.Count;
        var printedRows = Math.Min(d.Lines.Count, rowLimit);
        var overflowed = d.Lines.Count > printedRows
            && string.Equals(d.DocType, TaxInvoiceDocType, StringComparison.Ordinal);

        // 안내를 붙일 자리 = 품목 줄 중 가장 왼쪽 칸의 좌표. 그 아래 한 줄에 적는다.
        var noticeAnchor = overflowed
            ? coords.Where(c => c.Key.StartsWith("line.", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.XMm)
                    .FirstOrDefault()
            : null;

        e.Layers(layers =>
        {
            // 기준 층 — 좌표 인쇄는 이 위에 얹는다.
            layers.PrimaryLayer().Height(297, Unit.Millimetre);

            foreach (var c in coords)
            {
                if (c.Key.Equals("row_pitch", StringComparison.OrdinalIgnoreCase)) continue;
                if (c.Key.Equals("max_rows", StringComparison.OrdinalIgnoreCase)) continue;

                // 품목 줄: 첫 줄 좌표에서 아래로 반복
                if (c.Key.StartsWith("line.", StringComparison.OrdinalIgnoreCase))
                {
                    var field = c.Key.Substring(5);
                    var limit = maxRows > 0 ? (int)maxRows : d.Lines.Count;
                    for (int i = 0; i < d.Lines.Count && i < limit; i++)
                    {
                        var text = LineValue(d.Lines[i], field, i + 1);
                        if (string.IsNullOrEmpty(text)) continue;
                        PlaceText(layers, c, c.YMm + pitch * i, text);
                    }
                    continue;
                }

                var val = HeaderValue(d, c.Key);
                if (string.IsNullOrEmpty(val)) continue;
                PlaceText(layers, c, c.YMm, val);
            }

            // 🔴 넘친 줄이 있으면 마지막 줄 아래 한 줄로 알린다 — **더하기만**.
            if (noticeAnchor is not null)
            {
                var notice = new FieldCoord
                {
                    Key = "overflow_notice",
                    XMm = noticeAnchor.XMm,
                    FontPt = 8f,
                    WidthMm = 150f
                };
                PlaceText(layers, notice, noticeAnchor.YMm + pitch * printedRows,
                    $"품목 {d.Lines.Count}줄 중 {printedRows}줄만 인쇄되었습니다. 양식용지 줄 수 설정을 확인하세요.");
            }
        });
    }

    /// <summary>값 하나를 종이 위 절대 위치(mm)에 올린다.</summary>
    private static void PlaceText(LayersDescriptor layers, FieldCoord c, float yMm, string text)
    {
        var width = c.WidthMm > 0 ? c.WidthMm : 30f;

        // TranslateX/Y 로 종이 왼쪽 위 기준 위치를 잡는다.
        var cell = layers.Layer()
            .TranslateX(c.XMm, Unit.Millimetre)
            .TranslateY(yMm, Unit.Millimetre)
            .Width(width, Unit.Millimetre);

        // 금액 칸은 오른쪽 정렬이라야 자릿수가 맞는다.
        var aligned = c.Align switch
        {
            "right" => cell.AlignRight(),
            "center" => cell.AlignCenter(),
            _ => cell
        };

        aligned.Text(text).FontSize(c.FontPt > 0 ? c.FontPt : 10f);
    }

    /// <summary>문서 머리 칸 이름 → 값. 고객이 한글로 적으므로 한글 이름을 먼저 받는다.</summary>
    private static string HeaderValue(DocumentSnapshot d, string key) => key.ToLowerInvariant() switch
    {
        "문서번호" or "doc_no" or "docno" => d.DocNo,
        "일자" or "doc_date" or "docdate" => d.DocDate.ToString("yyyy-MM-dd"),
        "년" or "year" => d.DocDate.ToString("yyyy"),
        "월" or "month" => d.DocDate.ToString("MM"),
        "일" or "day" => d.DocDate.ToString("dd"),
        "거래처" or "거래처명" or "상호" or "partner_name" => d.PartnerName,
        "사업자번호" or "등록번호" or "partner_business_no" => d.PartnerBusinessNo ?? "",
        "주소" or "partner_address" => d.PartnerAddress ?? "",
        "연락처" or "전화" or "partner_contact" => d.PartnerContact ?? "",
        "공급가액" or "total_supply" => d.TotalSupply.ToString("N0"),
        "세액" or "부가세" or "total_vat" => d.TotalVat.ToString("N0"),
        "합계" or "합계금액" or "total_amount" or "total" => d.TotalAmount.ToString("N0"),
        "비고" or "remark" => d.Remark ?? "",
        _ => ""
    };

    /// <summary>품목 줄 칸 이름 → 값.</summary>
    private static string LineValue(DocumentLine l, string field, int seq) => field.ToLowerInvariant() switch
    {
        "번호" or "순번" or "seq" or "no" => seq.ToString(),
        "품목" or "품목명" or "item_name" or "item" => l.ItemName,
        "규격" or "spec" => l.Spec ?? "",
        "수량" or "qty" => l.Qty.ToString("N1"),
        "단가" or "unit_price" or "price" => l.UnitPrice.ToString("N0"),
        "공급가액" or "supply" => l.Supply.ToString("N0"),
        "세액" or "부가세" or "vat" => l.Vat.ToString("N0"),
        "금액" or "합계" or "total" => l.Total.ToString("N0"),
        _ => ""
    };

    // 작지② paper_mode preprint 렌더링 — 시판 양식용지에 필드값만 좌표 저장
    // 테두리·헤더·푸터·합계 박스 모두 미렌더. 라인 데이터만 표 형태로 저장.
    //   ⚠️ 좌표가 없을 때만 쓰는 기본 배치다. 좌표가 있으면 ComposeBodyCoords 로 간다.
    private static void ComposeBodyPreprint(IContainer e, DocumentSnapshot d)
    {
        e.Column(col =>
        {
            // 거래처명 + 일자 — 양식용지 상단 좌표 (라벨 없음, 값만)
            col.Item().Row(r =>
            {
                r.RelativeItem().Text(d.PartnerName).FontSize(11);
                r.RelativeItem().AlignRight().Text(d.DocDate.ToString("yyyy-MM-dd")).FontSize(10);
            });

            // 라인 — 양식용지 본문 영역 (테두리 0, 좌표 저장)
            col.Item().PaddingTop(40).Table(tbl =>
            {
                tbl.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(25);
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                    c.ConstantColumn(50);
                    c.ConstantColumn(70);
                    c.ConstantColumn(80);
                    c.ConstantColumn(60);
                    c.ConstantColumn(80);
                });
                int seq = 1;
                foreach (var line in d.Lines)
                {
                    tbl.Cell().Padding(2).AlignCenter().Text($"{seq++}").FontSize(9);
                    tbl.Cell().Padding(2).Text(line.ItemName).FontSize(9);
                    tbl.Cell().Padding(2).Text(line.Spec ?? "").FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.Qty.ToString("N1")).FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.UnitPrice.ToString("N0")).FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.Supply.ToString("N0")).FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.Vat.ToString("N0")).FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.Total.ToString("N0")).FontSize(9);
                }
            });

            // 합계만 (라벨 없음, 양식용지 합계 박스 좌표)
            col.Item().PaddingTop(20).AlignRight().Text(d.TotalAmount.ToString("N0")).FontSize(11).Bold();
        });
    }

    /// <summary>고객이 저장한 좌표 JSON 을 읽는다. 잘못 적혀 있어도 인쇄는 되게 한다.</summary>
    /// <remarks>
    /// 🔴 여기서 예외를 던지면 <b>인쇄 자체가 막힌다.</b> 좌표는 거들 뿐이고 문서는 나가야 한다.
    ///   ⇒ 깨진 항목은 건너뛰고, 전부 깨졌으면 빈 목록(=종전 기본 배치)으로 되돌린다.
    /// 키 이름은 고객이 손으로 적으므로 <c>x_mm</c>·<c>xMm</c>·<c>x</c> 를 모두 받는다.
    /// </remarks>
    private List<FieldCoord> ParseFieldCoords(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<FieldCoord>();

        var list = new List<FieldCoord>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            // 🔴 [2026-11-19 갈래 A 고침1 · [4] #6] 이 함수의 로그 세 줄 문구만 「양식용지 좌표」 → 「좌표 양식의 칸 좌표」 —
            //   빈 A4 Y 템플릿도 좌표를 탄다. 판정 · 건너뛰기 · 경계값(`x > 420 || y > 594`) 무변 · 고객 노출 0.
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                _logger.LogWarning("좌표 양식의 칸 좌표가 목록 형태가 아니다 — 기본 배치로 인쇄한다.");
                return list;
            }

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                var key = Str(el, "key") ?? Str(el, "name");
                if (string.IsNullOrWhiteSpace(key)) continue;   // 어느 칸인지 모르면 못 찍는다

                var x = Num(el, "x_mm", "xMm", "x");
                var y = Num(el, "y_mm", "yMm", "y");
                if (x is null || y is null) continue;           // 위치 없으면 찍을 자리가 없다

                // 종이 밖 좌표는 조용히 사라진다 — 고객이 왜 안 나오는지 모르므로 남긴다.
                if (x < 0 || y < 0 || x > 420 || y > 594)
                {
                    _logger.LogWarning("좌표 양식의 칸 좌표가 종이 밖이다 — key={Key} x={X}mm y={Y}mm, 이 칸은 건너뛴다.", key, x, y);
                    continue;
                }

                list.Add(new FieldCoord
                {
                    Key = key!.Trim(),
                    XMm = (float)x.Value,
                    YMm = (float)y.Value,
                    FontPt = (float)(Num(el, "font_pt", "fontPt", "font") ?? 10d),
                    Align = (Str(el, "align") ?? "left").Trim().ToLowerInvariant(),
                    WidthMm = (float)(Num(el, "width_mm", "widthMm", "width") ?? 0d)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "좌표 양식의 칸 좌표를 읽지 못했다 — 기본 배치로 인쇄한다.");
            return new List<FieldCoord>();
        }
        return list;

        static string? Str(System.Text.Json.JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String
                ? p.GetString() : null;

        static double? Num(System.Text.Json.JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                if (!el.TryGetProperty(n, out var p)) continue;
                if (p.ValueKind == System.Text.Json.JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
                // 고객이 "12.5" 처럼 따옴표를 붙여 적는 일이 잦다 — 그것도 받는다.
                if (p.ValueKind == System.Text.Json.JsonValueKind.String
                    && double.TryParse(p.GetString(), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
            }
            return null;
        }
    }

    /// <summary>
    /// 🔴 [2026-11-20 W-G1-51 갈래 §59 · 대표 §59 「그림만 + 안내」 · PM 결재 ②] 이 종류의 저장된 양식으로 전표 [인쇄] 를 하면
    /// <b>그림만</b> 나오나 — 전표 인쇄 미리보기 창이 「아직 칸을 안 맞췄습니다」 안내를 띄울지 묻는 문(<c>GET …/print-notice</c>)이 부른다.
    /// </summary>
    /// <remarks>
    /// 🔴 렌더(<c>RenderDocumentAsync</c>)와 <b>같은 차례 · 같은 함수</b>로 센다:
    ///   양식 읽기(<see cref="TryLoadTemplateAsync"/> 그대로) → 저장된 그림 찍기 → 그림 읽기(<see cref="ResolveFormBackground"/> · 인쇄 문 꼴)
    ///   → Y 네 칸(렌더 <c>isYTemplate</c> 과 같은 셈) → <see cref="IsNoCoordsImageOnly"/>(판정 유일 자리).
    /// 🔴 Y 네 칸 변수 이름을 <c>yTemplate</c> 로 둔다 — <c>var isYTemplate =</c> 로 시작하는 줄을 새로 만들면
    ///   편집창 판정 게이트(<c>FormYTemplateCoordsKeepGateTests</c>)가 첫 줄을 읽는 자리가 흐려진다.
    /// 🔴 실패하면 경고 1줄 + 거짓(#15) — 안내가 안 뜰 뿐 미리보기 · 인쇄는 그대로다(#20).
    /// 🔴 전표 미리보기 옵션(<c>Options</c>)은 안 받는다 — 창은 그림 찍기 옵션을 보내지 않는다(작업지시서 §3-2 · 개발명세서 §9 3).
    /// </remarks>
    public async Task<bool> IsNoCoordsImageOnlyAsync(string tenantId, string documentType, CancellationToken ct = default)
    {
        try
        {
            var template = await TryLoadTemplateAsync(tenantId, documentType, ct).ConfigureAwait(false);
            // 인쇄 문(`DocumentController.PreviewPdf`)이 세우는 꼴 그대로 — 설정 미리보기 아님 · 인쇄 문 참.
            var printDoor = new PdfPreviewOverrides { NoCoordsImageOnly = true };
            var savedPrintBackground = template?.PrintBackground == true;
            var bgImage = ResolveFormBackground(printDoor, template);
            var yTemplate = string.Equals(template?.PaperMode, "plain", StringComparison.Ordinal)
                && savedPrintBackground
                && !string.IsNullOrWhiteSpace(template?.BackgroundImageUrl)
                && bgImage is not null;
            // §59 고침 1(②-8) — 칸 0 = 저장된 칸 0 · 판정은 ②-7(IsNoCoordsImageOnly 의 FormCoordsStored.IsEmpty 줄) 하나 · 이 문은 코드 무변.
            return IsNoCoordsImageOnly(documentType, printDoor, yTemplate, template);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] 인쇄 전 안내(그림만) 판정을 못 했다 — 안내 없이 둔다. tenant={Tenant} type={Type}",
                tenantId, documentType);
            return false;
        }
    }

    // form_templates 조회 — 미존재 시 null (기존 plain 동작 정합)
    private async Task<FormTemplateInfo?> TryLoadTemplateAsync(string tenantId, string documentType, CancellationToken ct)
    {
        if (_formTemplateService is null) return null;

        // documentType → form_type 매핑
        //   ⚠️ 여기 빠진 문서는 **템플릿을 못 찾아 조용히 기본 모양으로 인쇄된다.**
        //   오류가 안 나서 더 위험하다 — 고객은 양식을 설정했는데 왜 안 바뀌는지 알 수 없다.
        //   ⇒ 양식(form_type)을 늘릴 때 이 표를 같이 늘려야 한다(2026-08-12 조사에서 적발).
        var formType = documentType switch
        {
            "quotation" => "estimate",
            "sales_order" => "sales_order",
            "delivery" => "delivery",
            "purchase_order" => "purchase_order",
            "purchase_receipt" => "receipt",
            "purchase_return" => "purchase_return",
            // 🔴 봉합 2026-08-12 — 종전 누락분.
            //   sales_return: 양식 시드는 됐는데 이 표에 없어 판매반품만 설정이 안 먹었다.
            "sales_return" => "sales_return",
            "tax_invoice" => "tax_invoice",
            // 사장님 결재 2026-08-11 (10종 확장) — 계산서·입금표
            "invoice_exempt" => "invoice_exempt",
            "payment_receipt" => "payment_receipt",
            // 🔴 20260826작6 — 급여명세서. 사장님 결재: "양식은 자유롭게 붙일 수 있도록 하되,
            //   기본 템플릿 제공". 이 표에 안 넣으면 고객이 양식을 설정해도 **안 먹는다**
            //   (위 주석의 8/12 적발 사례와 같은 자리 — 오류가 안 나서 더 위험하다).
            PayslipDocType => PayslipDocType,
            _ => null
        };
        if (formType is null) return null;

        try
        {
            var dto = await _formTemplateService.GetDefaultAsync(tenantId, formType, ct).ConfigureAwait(false);
            if (dto is null) return null;
            return ToTemplateInfo(dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "form_templates 조회 실패 - tenant={Tenant} type={Type}, 기본 plain 가도", tenantId, formType);
            return null;
        }
    }

    /// <summary>
    /// 양식 DTO → 작성기 입력. 🔴 <b>매핑은 한 벌이다</b> — 미리보기도 이것을 쓴다.
    /// 두 벌이 되면 미리보기와 인쇄물이 조용히 어긋난다(20261021작 §11-3).
    /// </summary>
    private FormTemplateInfo ToTemplateInfo(FormTemplateDto dto)
    {
        return new FormTemplateInfo
            {
                PaperMode = dto.PaperMode,
                MarginTopMm = dto.MarginTopMm,
                MarginLeftMm = dto.MarginLeftMm,
                MarginRightMm = dto.MarginRightMm,
                MarginBottomMm = dto.MarginBottomMm,
                ShowCompanyLogo = dto.ShowCompanyLogo,
                ShowBorder = dto.ShowBorder,
                // 🔴 20261021작 절 B-1 — 종전에는 이 값을 읽지 않아 고객이 「인쇄 매수」를
                //   무엇으로 두든 **같은 종이**가 나왔다. 세금계산서는 법정 2매다.
                PrintCopyMode = dto.PrintCopyMode,
                // 🔴 20261021작 §10-4 S-2 (DB-147) — 이 한 줄이 없으면 고객이 설정을
                //   켜도 끄나 켜나 **같은 종이**가 나온다.
                ShowTearLine = dto.ShowTearLine,
                // 🔴 봉합 2026-08-12 (사장님 지시 "양식정보 설정의 양식용지만 해")
                //   종전에는 이 값을 **읽어오지도 않았다.** 고객이 양식용지 좌표를 아무리
                //   맞춰도 인쇄물은 코드에 고정된 위치로만 나왔다.
                //   ⇒ 시판 양식지 칸과 안 맞아 양식용지 모드가 사실상 못 쓰는 기능이었다.
                FieldCoords = ParseFieldCoords(dto.FieldCoordsJson),
                // 🔴 §59 고침 1(작업지시서 ②-6) — 저장값 글자 그대로도 들고 간다. 판정 「저장된 칸 0」(FormCoordsStored.IsEmpty)이 읽는다.
                //   DTO 와 같은 이름 · 같은 형이라 짝 대조 게이트(축⒡ 긁기)가 이 배선을 스스로 잰다.
                FieldCoordsJson = dto.FieldCoordsJson,
            // 🔴 20261024 사장님 오더 "인쇄까지 모두 양식지 적용" · §13
            //   *"이미 업체에서 쓰는 양식지가 있을수도 있으니"* — 종전에는 이 칸을
            //   **읽어오지도 않았다**(PdfRenderService 안 BackgroundImageUrl 0건).
            //   고객이 쓰는 양식지를 올려도 종이에 안 나왔다.
            BackgroundImageUrl = dto.BackgroundImageUrl,
            // 🔴 20261024 사장님 결재 — 종전에는 아래 네 칸이 작성기에 **0건**이었다.
            //   편집기에서 고르고 저장까지 되는데 **종이만 안 바뀌는** 칸들이었다.
            //   🔴 PaperSize·Orientation = 실제로 종이가 바뀐다.
            //   🔴 ShowCompanySeal·StyleKey = **배선만**(아래 각 칸 주석에 까닭을 적었다).
            PaperSize = dto.PaperSize,
            Orientation = dto.Orientation,
            ShowCompanySeal = dto.ShowCompanySeal,
            StyleKey = dto.StyleKey,
            // 🔴 20261026작1 §A-3 (DB-148) — 이 다섯 줄이 없으면 고객이 양식정보설정에서
            //   켜도 **종이만 안 바뀌는** 칸이 또 생긴다. 10/24·10/25 와 같은 사고다.
            //   🔴 수치 셋은 **null 을 그대로** 들고 간다 — 여기서 기본값을 채우면
            //   「공통값을 따른다」가 사라지고 그 양식은 공통 설정을 영영 안 따라온다.
            PrintBackground = dto.PrintBackground,
            ShowWatermark = dto.ShowWatermark,
            WatermarkOpacity = dto.WatermarkOpacity,
            WatermarkSaturate = dto.WatermarkSaturate,
            WatermarkScalePct = dto.WatermarkScalePct
        };
    }

    /// <summary>
    /// 예시 자료 — 🔴 <b>전표 없이</b> 양식을 맞춰 볼 수 있게 한다(20261021작 §11-2 ②).
    /// 양식정보설정은 6단계 중 <b>1단계</b>인데 전표는 3~4단계다. 신규 고객이 자를 대고
    /// mm 를 맞추는 바로 그 시점에 전표가 <b>0건</b>이라 지금은 아무것도 못 봤다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>품목 줄 수를 바꿔 볼 수 있어야 한다</b>(§11-3) — 「몇 줄부터 두 쪽이 되나」·
    ///   「몇 줄부터 외 N건이 되나」를 눈으로 보는 것이 이 화면의 목적이다.
    /// 🔴 종이에 <b>「예시」</b>가 찍힌다 — 진짜 계산서로 오해하면 안 된다.
    /// </remarks>
    private static DocumentSnapshot BuildSampleSnapshot(string documentType, PdfPreviewOverrides ov)
    {
        var count = Math.Clamp(ov.SampleLineCount, 1, 200);
        var lines = new List<DocumentLine>(count);
        for (var i = 1; i <= count; i++)
        {
            // 금액은 decimal 이다 (#4). 줄마다 조금씩 달라야 합계 검산이 눈에 보인다.
            var qty = (decimal)(i % 5 + 1);
            var unit = 10000m + (i % 7) * 1500m;
            var supply = qty * unit;
            var vat = Math.Round(supply * 0.1m, 0, MidpointRounding.AwayFromZero);
            lines.Add(new DocumentLine
            {
                ItemName = $"예시품목 {i}",
                Spec = i % 2 == 0 ? "1BOX" : "1EA",
                Qty = qty,
                UnitPrice = unit,
                Supply = supply,
                Vat = vat,
                Total = supply + vat
            });
        }

        var totalSupply = lines.Sum(l => l.Supply);
        var totalVat = lines.Sum(l => l.Vat);

        return new DocumentSnapshot
        {
            DocType = documentType,
            // 🔴 번호 자리에 「예시」를 적는다 — 어느 양식이든 머리에 반드시 찍히는 칸이다.
            DocNo = "예시-0000",
            DocDate = DateTime.Today,
            PartnerName = "(예시) 거래처명",
            PartnerBusinessNo = "000-00-00000",
            PartnerAddress = "(예시) 주소 — 실제 자료가 아닙니다",
            PartnerContact = "담당자",
            PartnerCeoName = "(예시) 대표자",
            PartnerBizType = "(예시) 업태",
            PartnerBizItem = "(예시) 종목",
            PartnerPhone = "000-0000-0000",
            TotalSupply = totalSupply,
            TotalVat = totalVat,
            TotalAmount = totalSupply + totalVat,
            Remark = ov.MarkAsSample
                ? "🔴 예시 자료입니다 — 실제 거래 내용이 아닙니다. 양식 확인용으로만 쓰십시오."
                : null,
            Lines = lines
        };
    }

    private sealed class FormTemplateInfo
    {
        public string PaperMode { get; set; } = "plain";

        /// <summary>
        /// 🔴 저장된 칸 좌표 글자 그대로 (W-G1-51 §59 고침 1 · P2-1). <see cref="FieldCoords"/> 는 읽어 낸 목록이라
        ///   모두 무효 칸이면 0개가 된다 — 「저장된 칸 0」 판정(<see cref="FormCoordsStored.IsEmpty"/>)은 이 글자를 본다.
        /// </summary>
        public string? FieldCoordsJson { get; set; }
        public decimal MarginTopMm { get; set; } = 20;
        public decimal MarginLeftMm { get; set; } = 20;
        public decimal MarginRightMm { get; set; } = 20;
        public decimal MarginBottomMm { get; set; } = 20;
        public bool ShowCompanyLogo { get; set; } = true;
        public bool ShowBorder { get; set; } = true;

        /// <summary>인쇄 매수 — both·recipient·supplier (DB-90). 세금계산서 전용 작성기가 읽는다.</summary>
        public string PrintCopyMode { get; set; } = "recipient";

        /// <summary>
        /// 두 벌을 한 장에 모으고 가운데 자르는 선을 넣나 (DB-147 · 20261021작 §10-2 ⓑ).
        /// 기본 false — 양식 행이 없을 때도 「끔」으로 본다(품목이 조용히 빠지면 안 된다).
        /// </summary>
        public bool ShowTearLine { get; set; }

        /// <summary>
        /// 🔴 양식지 그림을 <b>실제 종이에도</b> 찍나 (DB-148 · 20261026작1 · 사장님 오더 10/25).
        /// </summary>
        /// <remarks>
        /// 🔴 <b><c>PaperMode</c> 와 축이 다르고 곱해진다.</b>
        /// <c>preprint</c>(양식용지를 사다 쓴다) + 이 칸이 켜짐 = <b>이중 인쇄</b>다.
        /// <c>plain</c>(순백지) + 켜짐 = 우리가 가진 양식지 그림을 깔아서 뽑는다.
        /// 🔴 기본 false — 끈 고객의 종이에 그림이 새면 그게 사고다.
        /// </remarks>
        public bool PrintBackground { get; set; }

        /// <summary>
        /// 회사 로고를 종이 <b>정중앙에 연하게</b> 깔까 (DB-148 · 사장님 오더
        /// <i>"대학교들 졸업증이나 재학증명서 느낌으로"</i>).
        /// </summary>
        /// <remarks>
        /// 재료는 회사 정보의 로고 그림이다. 그 자리가 비면 아무 일도 하지 않는다.
        /// 🔴🔴 <b>세 값이다</b> — <c>null</c>(공통을 따른다) · <c>true</c>(이 양식만 켬) ·
        ///   <c>false</c>(이 양식만 끔). [2026-10-26 [4] 반려 R-2 봉합] 종전에는 비널러블
        ///   <c>bool</c> 이어서 「정하지 않았다」가 없었고, 공통 켜기가 양식 행이 있는
        ///   고객에게 영영 안 닿았다.
        /// </remarks>
        public bool? ShowWatermark { get; set; }

        /// <summary>
        /// 워터마크 명도 0.00~1.00 — 🔴 <b>null 이면 공통 설정을 따른다.</b>
        /// </summary>
        /// <remarks>
        /// 🔴 <b>0.00 과 null 은 다른 뜻이다</b> — 0.00 = <i>"완전히 투명하게"</i>,
        /// null = <i>"나는 정하지 않았다"</i>. 작성기는 null 을 만나면 공통값을 찾아간다.
        /// </remarks>
        public decimal? WatermarkOpacity { get; set; }

        /// <summary>워터마크 채도 0.00~1.00 (0=흑백) — null 이면 공통 설정을 따른다. DB-148.</summary>
        public decimal? WatermarkSaturate { get; set; }

        /// <summary>워터마크 크기 — 용지 폭 대비 %. null 이면 공통 설정을 따른다. DB-148.</summary>
        public int? WatermarkScalePct { get; set; }

        /// <summary>
        /// 🔴 업체 양식지 그림 (20261024 사장님 오더 · §13). 종전에는 이 칸이
        /// <b>작성기에 아예 없었다</b> — 고객이 올려도 종이에 안 나왔다.
        /// </summary>
        /// <remarks>
        /// 사장님: <i>"양식지를 추가할수도 있어야해. 이미 업체에서 쓰는 양식지가 있을수도 있으니."</i>
        /// 🔴 스캔한 양식지 위에 내용을 겹쳐 보여줘야 mm 맞추기가 의미를 가진다.
        /// </remarks>
        public string? BackgroundImageUrl { get; set; }

        /// <summary>
        /// 🔴 용지 크기 — <c>A4</c>·<c>A5</c>·<c>Letter</c> (20261024 사장님 결재).
        /// 종전에는 작성기에 <b>이 칸이 없었다</b> — 고객이 A5 를 골라도 A4 가 나왔다.
        /// </summary>
        public string PaperSize { get; set; } = "A4";

        /// <summary>
        /// 🔴 용지 방향 — <c>portrait</c>·<c>landscape</c> (20261024 사장님 결재).
        /// 종전 <b>0건</b>. 방향을 지정하는 코드가 작성기에 아예 없었다.
        /// </summary>
        public string Orientation { get; set; } = "portrait";

        /// <summary>
        /// 🔴 이 양식에서 직인을 찍나 (20261024 사장님 결재 · <b>배선만</b>).
        /// 🔴 <b>「직인이 나온다」는 뜻이 아니다</b> — 직인 그림 보관소가 없어
        /// <c>seal_url</c> 이 늘 비어 있다. 보관소가 생기는 날 이 칸이 켬/끔을 가른다.
        /// </summary>
        public bool ShowCompanySeal { get; set; } = true;

        /// <summary>
        /// 🔴 디자인 스타일 (DB-90 · <b>배선만</b>). 출하 DDL 주석에 적힌 그대로
        /// <i>"4종 확정 전까지 basic 단일"</i> ⇒ 🔴 <b>지금은 무엇이 와도 basic 으로 그린다.</b>
        /// 종전에는 저장·조회만 되고 <b>작성기가 읽지 않았다</b>.
        /// 🔴 [2026-11-20 W-G1-51 갈래 E⑵] 위 「지금은 무엇이 와도 basic」 은 낡았다(이력으로 둔다) —
        /// 순백지 양식에 한해 <c>formal</c> · <c>simple</c> · <c>large</c> 를 그린다(<c>ResolvePrintStyle</c>).
        /// 🔴 [2026-11-20 갈래 E⑵ 고침 1 · PM 판단 §72 E-1] 법정 서식(또는 그 후보) 3종(세금계산서 · 면세계산서 · 입금표)은 빼고.
        /// 🔴 [2026-11-20 갈래 E⑵ 고침 2 · E⑷ E4-6 · §7] 위에 옮겨 적은 DDL 주석 <i>"4종 확정 전까지 basic 단일"</i> 도 낡았다(이력으로 둔다) —
        /// 새 글자 = <i>"디자인 스타일 (DB-90). basic·formal·simple·large — 빈 A4 순백지이고 양식 그림이 없는 양식에만 쓰임(그 밖은 basic 모양)"</i>.
        /// </summary>
        public string StyleKey { get; set; } = "basic";

        /// <summary>고객이 맞춘 양식용지 필드 위치. 비어 있으면 종전 기본 배치로 인쇄한다.</summary>
        public List<FieldCoord> FieldCoords { get; set; } = new();
    }

    /// <summary>
    /// 양식용지 한 칸의 위치. 고객이 실제 종이에 자를 대고 잰 값(mm)이 그대로 들어온다.
    /// </summary>
    /// <remarks>
    /// 원점은 <b>종이 왼쪽 위 모서리</b>다 — 여백이 아니라. 고객이 자로 재는 기준이 종이 끝이기 때문이다.
    /// 그래서 좌표 인쇄 경로는 페이지 여백을 0 으로 두고 값만 절대 위치에 올린다.
    /// </remarks>
    private sealed class FieldCoord
    {
        public string Key { get; set; } = "";
        public float XMm { get; set; }
        public float YMm { get; set; }
        public float FontPt { get; set; } = 10f;
        /// <summary>left(기본)·right·center. 금액 칸은 오른쪽 정렬이라야 자릿수가 맞는다.</summary>
        public string Align { get; set; } = "left";
        /// <summary>칸 너비(mm). 오른쪽·가운데 정렬일 때 기준이 된다. 0 이면 30mm 로 본다.</summary>
        public float WidthMm { get; set; }
    }
}
