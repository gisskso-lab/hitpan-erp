using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Settings;

// 양식정보설정 DTO (사장님 작업지시 2026-05-31 작지②·③)
public class FormTemplateDto
{
    public string TemplateId { get; set; } = string.Empty;
    public string FormType { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string PaperMode { get; set; } = "plain";
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public decimal MarginTopMm { get; set; } = 15m;
    public decimal MarginLeftMm { get; set; } = 15m;
    public decimal MarginRightMm { get; set; } = 15m;
    public decimal MarginBottomMm { get; set; } = 15m;
    public string? FieldCoordsJson { get; set; }
    public string? BackgroundImageUrl { get; set; }
    public bool ShowCompanyLogo { get; set; } = true;
    public bool ShowCompanySeal { get; set; } = true;
    public bool ShowBorder { get; set; } = true;

    /// <summary>
    /// 한 번 인쇄할 때 누구 몫을 찍나 (DB-90, 사장님 지시 2026-08-11).
    /// both=공급자+공급받는자 2장 / recipient=공급받는자만 / supplier=공급자만.
    /// </summary>
    /// <remarks>
    /// paper_mode(어떤 <b>종이</b>) · style_key(어떤 <b>모양</b>) 와 다른 축이다.
    /// 세금계산서·계산서는 법정 2매라 both 가 기본이다(부가가치세법 시행규칙).
    /// </remarks>
    public string PrintCopyMode { get; set; } = "recipient";

    /// <summary>디자인 스타일 (DB-90). 4종 확정 전까지 basic 단일.</summary>
    public string StyleKey { get; set; } = "basic";

    /// <summary>
    /// 둘 다 찍을 때 <b>한 장에 모을까</b>(true) <b>쪽을 나눌까</b>(false) — DB-147.
    /// 20261021작 §10-2 ⓑ 사장님 재결재.
    /// </summary>
    /// <remarks>
    /// print_copy_mode 와 축이 다르다 — 저쪽은 <b>누구 몫</b>을, 이쪽은 <b>어떻게 담을지</b>를 고른다.
    /// both 가 아니면 이 값은 아무 일도 하지 않는다.
    /// 기본 false 인 까닭은 DB-147 머리글에 적었다(지금 동작과 같게 · 품목 줄이 조용히 빠지면 안 된다).
    /// </remarks>
    public bool ShowTearLine { get; set; }

    /// <summary>
    /// 🔴 양식지 그림을 <b>실제 종이에도</b> 찍나 — DB-148.
    /// 사장님 오더 2026-10-25 (<i>"양식지 인쇄 / 순백지 인쇄 — 고객사의 옵션이 필요함"</i>).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>paper_mode 와 축이 다르고 곱해진다.</b> 양식용지를 <b>사다 쓰는</b> 고객(preprint)은
    /// 종이에 양식이 이미 인쇄돼 있으니 또 찍으면 <b>이중 인쇄</b>가 된다 ⇒ 끈다.
    /// 양식지를 <b>그림으로 받아</b> 순백지에 뽑는 고객(plain)은 켠다. 한 칸으로는 이 둘을 못 가른다.
    /// 기본 false — 오늘까지 실인쇄에는 그림이 <b>설계상</b> 안 깔렸다(양식 편집기 미리보기만 깔았다).
    /// 켜는 것은 고객이 한 번 고른다.
    /// </remarks>
    public bool PrintBackground { get; set; }

    /// <summary>
    /// 회사 로고를 종이 <b>정중앙에 연하게</b> 깔까 — DB-148.
    /// 사장님 오더 (<i>"대학교들 졸업증이나 재학증명서 느낌으로"</i>).
    /// </summary>
    /// <remarks>재료는 <c>local_company.logo_url</c> 이다. 그 자리가 비면 아무 일도 하지 않는다.</remarks>
    public bool ShowWatermark { get; set; }

    /// <summary>
    /// 워터마크 명도 0.00~1.00 — 🔴 <b>null 이면 공통값</b>(<c>print_common_settings</c>)을 따른다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>0.00 과 null 은 다른 뜻이다</b> — 0.00 = <i>"완전히 투명하게 해 달라"</i>,
    /// null = <i>"나는 정하지 않았다, 공통을 따르겠다"</i>. 그래서 nullable 이다.
    /// 한 칸에 두 뜻을 담으면 다음 사람이 가를 수 없다.
    /// </remarks>
    [Range(0.0, 1.0)]
    public decimal? WatermarkOpacity { get; set; }

    /// <summary>워터마크 채도 0.00~1.00 (0=흑백) — null 이면 공통값을 따른다. DB-148.</summary>
    [Range(0.0, 1.0)]
    public decimal? WatermarkSaturate { get; set; }

    /// <summary>워터마크 크기 — 용지 폭 대비 %. null 이면 공통값을 따른다. DB-148.</summary>
    /// <remarks>100 을 넘으면 종이를 벗어난다 — 위에서 조인다.</remarks>
    [Range(10, 100)]
    public int? WatermarkScalePct { get; set; }


    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CreateFormTemplateRequest
{
    [Required, MaxLength(30)]
    public string FormType { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string TemplateName { get; set; } = string.Empty;

    [Required]
    public string PaperMode { get; set; } = "plain";

    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    // 🔴🔴 20261021작 §17 T-7 / §20 — 0~100 → 0~35 으로 조인다(N-3 봉합).
    //   실측(1mm 간격 × 3모드 × 품목 40·80·100줄): 0~39mm 전건 정상 · 첫 고장 40mm ·
    //   80mm 이상은 종이가 아예 안 나온다(DocumentLayoutException). 🔴 처음 적은 「40mm 이상」은 틀렸다.
    //   상한 근거 = PdfRenderService.TaxInvoiceMaxMarginMm 주석의 실측표.
    //   🔴 Range 를 소수(0.0~50.0)로 둔다 — 정수 경계는 decimal 칸에서 35.5 를 통과시킨다(등재 E-4).
    //   🔴 이미 35mm 를 넘겨 저장해 둔 고객의 값은 지우지 않는다 — 그릴 때 조인다(같은 절).

    [Range(0.0, 35.0)] public decimal MarginTopMm { get; set; } = 15m;
    [Range(0.0, 35.0)] public decimal MarginLeftMm { get; set; } = 15m;
    [Range(0.0, 35.0)] public decimal MarginRightMm { get; set; } = 15m;
    [Range(0.0, 35.0)] public decimal MarginBottomMm { get; set; } = 15m;

    public string? FieldCoordsJson { get; set; }
    public string? BackgroundImageUrl { get; set; }
    public bool ShowCompanyLogo { get; set; } = true;
    public bool ShowCompanySeal { get; set; } = true;
    public bool ShowBorder { get; set; } = true;

    /// <summary>
    /// 한 번 인쇄할 때 누구 몫을 찍나 (DB-90, 사장님 지시 2026-08-11).
    /// both=공급자+공급받는자 2장 / recipient=공급받는자만 / supplier=공급자만.
    /// </summary>
    /// <remarks>
    /// paper_mode(어떤 <b>종이</b>) · style_key(어떤 <b>모양</b>) 와 다른 축이다.
    /// 세금계산서·계산서는 법정 2매라 both 가 기본이다(부가가치세법 시행규칙).
    /// </remarks>
    public string PrintCopyMode { get; set; } = "recipient";

    /// <summary>디자인 스타일 (DB-90). 4종 확정 전까지 basic 단일.</summary>
    public string StyleKey { get; set; } = "basic";

    /// <summary>
    /// 둘 다 찍을 때 <b>한 장에 모을까</b>(true) <b>쪽을 나눌까</b>(false) — DB-147.
    /// 20261021작 §10-2 ⓑ 사장님 재결재.
    /// </summary>
    /// <remarks>
    /// print_copy_mode 와 축이 다르다 — 저쪽은 <b>누구 몫</b>을, 이쪽은 <b>어떻게 담을지</b>를 고른다.
    /// both 가 아니면 이 값은 아무 일도 하지 않는다.
    /// 기본 false 인 까닭은 DB-147 머리글에 적었다(지금 동작과 같게 · 품목 줄이 조용히 빠지면 안 된다).
    /// </remarks>
    public bool ShowTearLine { get; set; }

    /// <summary>
    /// 🔴 양식지 그림을 <b>실제 종이에도</b> 찍나 — DB-148.
    /// 사장님 오더 2026-10-25 (<i>"양식지 인쇄 / 순백지 인쇄 — 고객사의 옵션이 필요함"</i>).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>paper_mode 와 축이 다르고 곱해진다.</b> 양식용지를 <b>사다 쓰는</b> 고객(preprint)은
    /// 종이에 양식이 이미 인쇄돼 있으니 또 찍으면 <b>이중 인쇄</b>가 된다 ⇒ 끈다.
    /// 양식지를 <b>그림으로 받아</b> 순백지에 뽑는 고객(plain)은 켠다. 한 칸으로는 이 둘을 못 가른다.
    /// 기본 false — 오늘까지 실인쇄에는 그림이 <b>설계상</b> 안 깔렸다(양식 편집기 미리보기만 깔았다).
    /// 켜는 것은 고객이 한 번 고른다.
    /// </remarks>
    public bool PrintBackground { get; set; }

    /// <summary>
    /// 회사 로고를 종이 <b>정중앙에 연하게</b> 깔까 — DB-148.
    /// 사장님 오더 (<i>"대학교들 졸업증이나 재학증명서 느낌으로"</i>).
    /// </summary>
    /// <remarks>재료는 <c>local_company.logo_url</c> 이다. 그 자리가 비면 아무 일도 하지 않는다.</remarks>
    public bool ShowWatermark { get; set; }

    /// <summary>
    /// 워터마크 명도 0.00~1.00 — 🔴 <b>null 이면 공통값</b>(<c>print_common_settings</c>)을 따른다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>0.00 과 null 은 다른 뜻이다</b> — 0.00 = <i>"완전히 투명하게 해 달라"</i>,
    /// null = <i>"나는 정하지 않았다, 공통을 따르겠다"</i>. 그래서 nullable 이다.
    /// 한 칸에 두 뜻을 담으면 다음 사람이 가를 수 없다.
    /// </remarks>
    [Range(0.0, 1.0)]
    public decimal? WatermarkOpacity { get; set; }

    /// <summary>워터마크 채도 0.00~1.00 (0=흑백) — null 이면 공통값을 따른다. DB-148.</summary>
    [Range(0.0, 1.0)]
    public decimal? WatermarkSaturate { get; set; }

    /// <summary>워터마크 크기 — 용지 폭 대비 %. null 이면 공통값을 따른다. DB-148.</summary>
    /// <remarks>100 을 넘으면 종이를 벗어난다 — 위에서 조인다.</remarks>
    [Range(10, 100)]
    public int? WatermarkScalePct { get; set; }


    public bool IsDefault { get; set; }
}

public class UpdateFormTemplateRequest : CreateFormTemplateRequest
{
    public bool IsActive { get; set; } = true;
}

// 필드 좌표 (preprint 모드, JSON 직렬화용)
public class FormFieldCoord
{
    public string Key { get; set; } = string.Empty;       // partner_name·tax_id·item_name·spec·qty·price 등
    public decimal XMm { get; set; }
    public decimal YMm { get; set; }
    public decimal? WidthMm { get; set; }
    public int FontPt { get; set; } = 10;
    public string Align { get; set; } = "left";            // left·right·center
}
