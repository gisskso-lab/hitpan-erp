using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HitPan.Backoffice.API.Services;

// 협력업체 가입계약서 PDF 생성 (사장님 결재 2026-06-04)
//
// 임시 표준 템플릿 — 사장님 결재로 정식 본문은 추후 교체
//
// 헌법 정합:
//   #15 — 빈 catch 금지 (호출 측에서 ILogger 박제)
//   #25 — 쉽게(원클릭 자동 생성)·정확하게(신청 정보 자동 박제)·안전하게(서명 회수 절차 명시)
public interface IContractPdfGenerator
{
    byte[] CreateResellerContract(ResellerContractInput input);
}

public record ResellerContractInput(
    string ApplicationId,
    string CompanyName,
    string BizNo,
    string CeoName,
    string CompanyAddress,
    string ContactName,
    string ContactTitle,
    string Phone,
    string Email,
    string? SalesRegion,
    int? SalesYears,
    string? Reason,
    DateTime IssuedAt);

public class ContractPdfGenerator : IContractPdfGenerator
{
    public byte[] CreateResellerContract(ResellerContractInput x)
    {
        var doc = Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(40);
                p.DefaultTextStyle(s => s.FontFamily(Fonts.Calibri).FontSize(11).LineHeight(1.4f));

                p.Header().Element(BuildHeader);
                p.Content().Element(e => BuildBody(e, x));
                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span("히트판 ERP 협력업체 가입계약서 (초안) · ").FontSize(9).FontColor(Colors.Grey.Medium);
                    t.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                    t.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium);
                    t.TotalPages().FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static void BuildHeader(IContainer e)
    {
        e.Background("#0F6E56").Padding(16).Row(r =>
        {
            r.RelativeItem().Column(col =>
            {
                col.Item().Text("히트판 ERP").FontSize(20).Bold().FontColor(Colors.White);
                col.Item().Text("협력업체 가입계약서 (초안)").FontSize(13).FontColor("#E0F2EA");
            });
            r.ConstantItem(140).AlignRight().Column(col =>
            {
                col.Item().Text("HITPAN PARTNERS").FontSize(10).FontColor("#E0F2EA");
                col.Item().Text("CONFIDENTIAL").FontSize(9).FontColor("#E0F2EA");
            });
        });
    }

    private static void BuildBody(IContainer e, ResellerContractInput x)
    {
        e.PaddingVertical(20).Column(col =>
        {
            col.Spacing(14);

            // 머리말
            col.Item().Text(t =>
            {
                t.Span("주식회사 히트판").Bold();
                t.Span("(이하 \"본사\")과 아래 협력업체(이하 \"협력업체\")는 히트판 ERP SaaS 판매 협력에 관하여 다음과 같이 계약을 체결한다.");
            });

            // 당사자 정보 표
            col.Item().Border(1).BorderColor("#E5E7EB").Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(110);
                    c.RelativeColumn();
                });
                AddRow(t, "회사명", x.CompanyName);
                AddRow(t, "사업자등록번호", x.BizNo);
                AddRow(t, "대표자", x.CeoName);
                AddRow(t, "회사주소", string.IsNullOrWhiteSpace(x.CompanyAddress) ? "-" : x.CompanyAddress);
                AddRow(t, "담당자", string.IsNullOrWhiteSpace(x.ContactTitle)
                    ? x.ContactName
                    : $"{x.ContactName} ({x.ContactTitle})");
                AddRow(t, "연락처", x.Phone);
                AddRow(t, "이메일", x.Email);
                AddRow(t, "영업 지역", string.IsNullOrWhiteSpace(x.SalesRegion) ? "-" : x.SalesRegion!);
                AddRow(t, "영업 경력", x.SalesYears.HasValue ? $"{x.SalesYears.Value} 년" : "-");
                AddRow(t, "신청 일자", x.IssuedAt.ToString("yyyy년 MM월 dd일"));
                AddRow(t, "신청 번호", x.ApplicationId);
            });

            // 조항
            col.Item().PaddingTop(10).Text("제 1 조 (계약의 목적)").Bold().FontSize(12);
            col.Item().Text("본 계약은 본사가 제공하는 히트판 ERP SaaS 서비스의 영업·홍보·고객 모집을 협력업체가 수행하고, 본사가 그에 따른 영업 수수료를 지급하는 것을 목적으로 한다.");

            col.Item().Text("제 2 조 (계약 당사자)").Bold().FontSize(12);
            col.Item().Text("본사와 협력업체는 위 표에 기재된 정보를 기준으로 한다. 협력업체의 변경 사항은 즉시 본사에 서면 통지한다.");

            col.Item().Text("제 3 조 (영업 수수료)").Bold().FontSize(12);
            col.Item().Text("협력업체가 모집한 고객사의 유료 결제 매출에 대하여 본사는 별도 부속서로 정한 영업 수수료를 지급한다. 수수료율·지급일·정산 방식은 본사의 정책에 따르며, 본사는 30일 사전 통지로 정책을 갱신할 수 있다.");

            col.Item().Text("제 4 조 (계약 기간)").Bold().FontSize(12);
            col.Item().Text("본 계약의 효력은 양 당사자가 서명한 날부터 1년간 유효하며, 계약 종료 30일 전까지 양 당사자 어느 일방의 서면 이의가 없는 경우 동일한 조건으로 1년씩 자동 연장된다.");

            col.Item().Text("제 5 조 (비밀 유지)").Bold().FontSize(12);
            col.Item().Text("협력업체는 본 계약 수행 과정에서 취득한 본사의 영업비밀·고객정보·기술정보 일체를 계약 기간 및 계약 종료 후 3년간 제3자에게 누설하지 않으며, 계약 목적 외로 사용하지 아니한다.");

            col.Item().Text("제 6 조 (계약의 해지)").Bold().FontSize(12);
            col.Item().Text("양 당사자는 30일 전 서면 통지로 본 계약을 해지할 수 있다. 다만 일방이 본 계약을 중대하게 위반한 경우 상대방은 즉시 해지할 수 있으며, 위반 당사자에 대해 손해배상을 청구할 수 있다.");

            col.Item().Text("제 7 조 (관할 법원)").Bold().FontSize(12);
            col.Item().Text("본 계약과 관련하여 발생하는 분쟁은 본사 소재지를 관할하는 대한민국 법원을 제1심 관할 법원으로 한다.");

            col.Item().Text("제 8 조 (약관·부속서)").Bold().FontSize(12);
            col.Item().Text("히트판 ERP 서비스 약관, 개인정보 처리방침, 영업 수수료 부속서는 본 계약의 일부를 구성하며, 본사 공식 홈페이지에 게재된 최신본을 따른다.");

            // 안내문
            col.Item().PaddingTop(8).Background("#FEF3C7").BorderColor("#FDE68A").Border(1).Padding(10).Text(t =>
            {
                t.Span("본 계약서는 신청 접수 시점에 자동 생성된 초안입니다. ").Bold();
                t.Span("본사 검토·승인 후 정식 계약서가 별도로 발송되며, 양 당사자 서명 후에 효력이 발생합니다.");
            });

            // 서명란
            col.Item().PaddingTop(20).Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text("본사").Bold();
                    c.Item().Text("상호: 주식회사 히트판");
                    c.Item().Text("대표자: ________________");
                    c.Item().PaddingTop(20).Text("서명: ________________");
                });
                r.ConstantItem(20);
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text("협력업체").Bold();
                    c.Item().Text($"상호: {x.CompanyName}");
                    c.Item().Text($"대표자: {x.CeoName}");
                    c.Item().PaddingTop(20).Text("서명: ________________");
                });
            });

            col.Item().PaddingTop(20).AlignCenter().Text(x.IssuedAt.ToString("yyyy년 MM월 dd일"));
        });
    }

    private static void AddRow(TableDescriptor t, string label, string value)
    {
        t.Cell().Background("#F0FAF6").Padding(8).Text(label).Bold().FontSize(10);
        t.Cell().Padding(8).Text(value).FontSize(10);
    }
}
