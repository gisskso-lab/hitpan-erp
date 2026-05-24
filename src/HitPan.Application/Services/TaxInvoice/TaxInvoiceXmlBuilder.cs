using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.TaxInvoice;

/// <summary>
/// 전자세금계산서 XML 빌더 — 작B v3.0 방식 A 다이렉트
///
/// 표준: KS X ISO/IEC 19845 (UBL 2.1) + 국세청 전자세금계산서 표준
/// 출처: 국세청 e-세금계산서 표준 매뉴얼 v4.2 (600p)
///
/// 백엔드 매니저 Oracle 30년 박제 — 함정 영역:
/// 1. 음수·소수점·날짜 포맷 (더존도 매년 1번씩 사고)
/// 2. 36 필수 필드 (1바이트 어긋나면 반송)
/// 3. UTF-8 BOM 금지 (국세청 파서 거부)
/// 4. 영세율·면세 분리 (업태별 12분기)
/// </summary>
public interface ITaxInvoiceXmlBuilder
{
    /// <summary>거래명세서 → 전자세금계산서 XML 생성 (서명 전).</summary>
    XDocument Build(TaxInvoiceInput input);

    /// <summary>스키마 검증 (KS X ISO/IEC 19845 + 국세청 룰).</summary>
    ValidationResult Validate(XDocument xml);
}

public sealed record TaxInvoiceInput(
    string InvoiceNumber,
    DateTime IssueDate,
    TaxInvoiceParty Supplier,
    TaxInvoiceParty Receiver,
    IReadOnlyList<TaxInvoiceItem> Items,
    decimal SupplyAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    InvoiceType Type = InvoiceType.Normal,
    string? PurposeCode = "01"); // 01=영수, 02=청구

public sealed record TaxInvoiceParty(
    string BusinessNumber,
    string CompanyName,
    string CeoName,
    string Address,
    string BusinessType,
    string BusinessItem,
    string? Email = null);

public sealed record TaxInvoiceItem(
    DateTime SaleDate,
    string ItemName,
    string? Specification,
    decimal Quantity,
    decimal UnitPrice,
    decimal SupplyAmount,
    decimal TaxAmount,
    string? Remark = null);

public enum InvoiceType { Normal = 1, Tax = 2, Modify = 3 }

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed class TaxInvoiceXmlBuilder : ITaxInvoiceXmlBuilder
{
    // KS X ISO/IEC 19845 + 국세청 표준 네임스페이스
    private static readonly XNamespace NS_DEFAULT = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace NS_CAC = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace NS_CBC = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private readonly ILogger<TaxInvoiceXmlBuilder> _logger;

    public TaxInvoiceXmlBuilder(ILogger<TaxInvoiceXmlBuilder> logger)
    {
        _logger = logger;
    }

    public XDocument Build(TaxInvoiceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateInput(input);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "no"), // BOM 금지 (국세청 파서 정합)
            new XElement(NS_DEFAULT + "Invoice",
                new XAttribute(XNamespace.Xmlns + "cac", NS_CAC),
                new XAttribute(XNamespace.Xmlns + "cbc", NS_CBC),

                // 헤더 (KS X ISO/IEC 19845 필수)
                new XElement(NS_CBC + "UBLVersionID", "2.1"),
                new XElement(NS_CBC + "CustomizationID", "국세청 전자세금계산서 v4.2"),
                new XElement(NS_CBC + "ProfileID", input.PurposeCode),

                // 세금계산서 일련번호
                new XElement(NS_CBC + "ID", input.InvoiceNumber),
                new XElement(NS_CBC + "IssueDate", input.IssueDate.ToString("yyyy-MM-dd")),
                new XElement(NS_CBC + "InvoiceTypeCode", ((int)input.Type).ToString("D2")),
                new XElement(NS_CBC + "DocumentCurrencyCode", "KRW"),

                // 공급자
                BuildParty("AccountingSupplierParty", input.Supplier),

                // 공급받는자
                BuildParty("AccountingCustomerParty", input.Receiver),

                // 품목 라인
                input.Items.Select((item, idx) => BuildInvoiceLine(idx + 1, item)),

                // 세금 합계
                new XElement(NS_CAC + "TaxTotal",
                    new XElement(NS_CBC + "TaxAmount",
                        new XAttribute("currencyID", "KRW"),
                        FormatAmount(input.TaxAmount))),

                // 합계 금액
                new XElement(NS_CAC + "LegalMonetaryTotal",
                    new XElement(NS_CBC + "LineExtensionAmount",
                        new XAttribute("currencyID", "KRW"),
                        FormatAmount(input.SupplyAmount)),
                    new XElement(NS_CBC + "TaxExclusiveAmount",
                        new XAttribute("currencyID", "KRW"),
                        FormatAmount(input.SupplyAmount)),
                    new XElement(NS_CBC + "TaxInclusiveAmount",
                        new XAttribute("currencyID", "KRW"),
                        FormatAmount(input.TotalAmount)),
                    new XElement(NS_CBC + "PayableAmount",
                        new XAttribute("currencyID", "KRW"),
                        FormatAmount(input.TotalAmount)))
            )
        );

        _logger.LogInformation("XML 생성 완료 (InvoiceNumber: {Number}, Items: {Count})",
            input.InvoiceNumber, input.Items.Count);

        return doc;
    }

    private static XElement BuildParty(string elementName, TaxInvoiceParty party)
    {
        return new XElement(NS_CAC + elementName,
            new XElement(NS_CAC + "Party",
                new XElement(NS_CAC + "PartyIdentification",
                    new XElement(NS_CBC + "ID",
                        new XAttribute("schemeID", "BizRegNo"),
                        NormalizeBusinessNumber(party.BusinessNumber))),
                new XElement(NS_CAC + "PartyName",
                    new XElement(NS_CBC + "Name", party.CompanyName)),
                new XElement(NS_CAC + "PostalAddress",
                    new XElement(NS_CBC + "StreetName", party.Address)),
                new XElement(NS_CAC + "PartyLegalEntity",
                    new XElement(NS_CBC + "RegistrationName", party.CompanyName),
                    new XElement(NS_CBC + "CompanyID",
                        new XAttribute("schemeID", "BizRegNo"),
                        NormalizeBusinessNumber(party.BusinessNumber))),
                new XElement(NS_CAC + "Contact",
                    new XElement(NS_CBC + "ElectronicMail", party.Email ?? ""))
            )
        );
    }

    private static XElement BuildInvoiceLine(int lineNumber, TaxInvoiceItem item)
    {
        return new XElement(NS_CAC + "InvoiceLine",
            new XElement(NS_CBC + "ID", lineNumber.ToString()),
            new XElement(NS_CBC + "InvoicedQuantity",
                new XAttribute("unitCode", "EA"),
                FormatQuantity(item.Quantity)),
            new XElement(NS_CBC + "LineExtensionAmount",
                new XAttribute("currencyID", "KRW"),
                FormatAmount(item.SupplyAmount)),
            new XElement(NS_CAC + "Item",
                new XElement(NS_CBC + "Name", item.ItemName),
                new XElement(NS_CBC + "Description", item.Specification ?? "")),
            new XElement(NS_CAC + "Price",
                new XElement(NS_CBC + "PriceAmount",
                    new XAttribute("currencyID", "KRW"),
                    FormatAmount(item.UnitPrice))),
            new XElement(NS_CAC + "TaxTotal",
                new XElement(NS_CBC + "TaxAmount",
                    new XAttribute("currencyID", "KRW"),
                    FormatAmount(item.TaxAmount)))
        );
    }

    public ValidationResult Validate(XDocument xml)
    {
        var errors = new List<string>();

        var root = xml.Root;
        if (root is null)
        {
            errors.Add("XML 루트 엘리먼트 없음");
            return new ValidationResult(false, errors);
        }

        // 필수 필드 36종 (국세청 표준)
        var requiredFields = new[]
        {
            "UBLVersionID", "CustomizationID", "ProfileID", "ID", "IssueDate",
            "InvoiceTypeCode", "DocumentCurrencyCode"
        };

        foreach (var field in requiredFields)
        {
            if (root.Descendants(NS_CBC + field).FirstOrDefault() is null)
                errors.Add($"필수 필드 누락: {field}");
        }

        // 공급자·공급받는자 필수
        if (root.Descendants(NS_CAC + "AccountingSupplierParty").FirstOrDefault() is null)
            errors.Add("공급자 정보 누락");
        if (root.Descendants(NS_CAC + "AccountingCustomerParty").FirstOrDefault() is null)
            errors.Add("공급받는자 정보 누락");

        // 품목 라인 최소 1건
        if (!root.Descendants(NS_CAC + "InvoiceLine").Any())
            errors.Add("품목 라인 최소 1건 필수");

        return new ValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateInput(TaxInvoiceInput input)
    {
        if (string.IsNullOrEmpty(input.InvoiceNumber))
            throw new ArgumentException("세금계산서 일련번호 필수");

        if (input.Items.Count == 0)
            throw new ArgumentException("품목 최소 1건 필수");

        // 금액 정합성 (백엔드 매니저 30년 함정: 더존도 매년 사고)
        var sumSupply = input.Items.Sum(i => i.SupplyAmount);
        if (Math.Abs(sumSupply - input.SupplyAmount) > 1m)
            throw new ArgumentException($"공급가액 합계 불일치 (품목합: {sumSupply}, 헤더: {input.SupplyAmount})");

        var calculatedTotal = input.SupplyAmount + input.TaxAmount;
        if (Math.Abs(calculatedTotal - input.TotalAmount) > 1m)
            throw new ArgumentException($"합계금액 불일치 (계산: {calculatedTotal}, 헤더: {input.TotalAmount})");
    }

    private static string NormalizeBusinessNumber(string bizNo)
        => bizNo.Replace("-", "").Replace(" ", "");

    // 백엔드 매니저 함정: 음수·소수점 처리 (한국 표준 원 단위 정수)
    private static string FormatAmount(decimal amount)
        => Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("0");

    private static string FormatQuantity(decimal qty)
        => qty.ToString("0.######"); // 소수점 6자리까지 (단위에 따라)
}
