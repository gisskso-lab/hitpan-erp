using System.Data;
using System.Data.Common;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuestPDF.Infrastructure;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 R1 회송 F-2(CTO 전결 §226 · PM 결재 §227) — 매입 반품 [이메일] 첨부 PDF 가 <b>반품 자료 · 「매입반품」 제목</b>을 담는가.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 생겼나</b> — 반품처리 화면 [이메일] 이 매입 반품에도 키 <c>"purchase_receipt"</c> 를 넘겨, 서버가 매입명세서 로더로 반품 id 를 찾았다
///   → 못 찾음 → 머리 「매입명세서」 · 거래처 칸 「(원본 문서를 찾을 수 없습니다)」 · 품목 0 인 안내문 종이가 거래처 메일로 나갔다.
/// 🔴 <b>동작으로 본다(글자 검사 아님)</b> — ① 화면이 쓰는 키 고르기 <c>ReturnEmailKeyRule.Pick</c>(소스 링크)을 <b>불러</b> 키를 받고
///   ② 그 키를 이메일 길이 부르는 그 함수(<c>EmailService</c> → <c>PdfRenderService.RenderDocumentAsync</c> 4인자 · overrides 없음)에 그대로 넣어
///   ③ 그려진 PDF 를 PdfPig 로 읽어 반품 줄 품목 · 「매입반품」 제목이 있는지 본다.
///   DB 는 가짜 연결 — 매입반품 표(<c>purchase_returns</c> · <c>purchase_return_items</c>) 읽기에만 행을 돌려준다.
///   다른 로더(예: 매입명세서 <c>purchase_receipts</c>)로 가면 행 0 → 안내문 종이 → 이 시험이 빨강이다.
/// 🔴 <b>한계</b> — 화면 파일(<c>ReturnPage.razor.cs</c>)이 이 함수를 <c>IsLoadedReturnOnScreen</c> · <c>_loadedReturnIsSales</c> 로 부르는 한 줄은
///   Blazor 화면이라 이 시험이 부르지 못한다(개발명세서 §5 한계 칸). 메일 실물 발송도 재지 않는다(이 PC = 실물 DB · 메일 서버 금지).
/// 🔴 손 변이(개발명세서 §5): <c>ReturnEmailKeyRule.PurchaseReturn</c> 길을 옛 값 <c>"purchase_receipt"</c> 로 → T-F2a 빨강 · 되돌린 뒤 해시 같음.
/// </remarks>
public sealed class ReturnEmailKeyGateTests
{
    static ReturnEmailKeyGateTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private const string Tenant = "gate-f2-tenant";
    private const string ReturnId = "gate-f2-return-0001";
    private const string ReturnNo = "PR-GATE-F2-0001";
    private const string ReturnItemName = "반품시험부품가";

    private static async Task<(string Text, string[] Words, string FileName, FakeReturnDb Db)> RenderAsEmail(string key)
    {
        var db = new FakeReturnDb();
        var forms = new Mock<IFormTemplateService>();
        forms.Setup(f => f.GetDefaultAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FormTemplateDto?)null);
        var svc = new PdfRenderService(db, NullLogger<PdfRenderService>.Instance, forms.Object);

        // 이메일 길과 같은 호출 꼴(EmailService.SendDocumentAsync · EmailController.PreviewPdf = 4인자 · overrides 없음).
        var (bytes, fileName) = await svc.RenderDocumentAsync(Tenant, key, ReturnId, CancellationToken.None);

        using var read = UglyToad.PdfPig.PdfDocument.Open(bytes);
        var pages = read.GetPages().ToList();
        var text = string.Join("\n", pages.Select(p => p.Text));
        var words = pages.SelectMany(p => p.GetWords()).Select(w => w.Text).ToArray();
        return (text, words, fileName, db);
    }

    [Fact(DisplayName = "F-2 T-F2a 🔴 불러온 매입 반품 [이메일] → 키 고르기 → 이메일 렌더 PDF = 반품 줄 · 「매입반품」 제목 · 매입명세서 안내문 0")]
    public async Task T_F2a_LoadedPurchaseReturnEmailCarriesReturnPaper()
    {
        var key = ReturnEmailKeyRule.Pick(loadedReturnOnScreen: true, loadedReturnIsSales: false);

        var (text, words, fileName, db) = await RenderAsEmail(key);

        Assert.True(db.ReturnHeadReads == 1, $"키 {key} — 매입반품 머리 표를 읽지 않았다(다른 로더로 감). 읽은 문장: {string.Join(" | ", db.Seen)}");
        Assert.Contains("매입반품", words);
        Assert.Contains(ReturnItemName, text, StringComparison.Ordinal);
        Assert.Contains(ReturnNo, text, StringComparison.Ordinal);
        Assert.DoesNotContain("매입명세서", text, StringComparison.Ordinal);
        Assert.DoesNotContain("원본 문서를 찾을 수 없습니다", text, StringComparison.Ordinal);
        Assert.StartsWith("매입반품_", fileName, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "F-2 T-F2b 회귀 — 판매 반품 · 안 불러온 전표의 키는 종전 그대로")]
    [InlineData(true, true, "sales_return")]
    [InlineData(false, false, "purchase_receipt")]
    [InlineData(false, true, "purchase_receipt")]
    public void T_F2b_OtherKeysUnchanged(bool loaded, bool isSales, string expected) =>
        Assert.Equal(expected, ReturnEmailKeyRule.Pick(loaded, isSales));

    /// <summary>
    /// 🔵 매입반품 머리 · 줄 · 회사 읽기에만 행을 돌려준다. 그 밖의 읽기(양식 · 공통 인쇄 설정 · 다른 로더)는 0행 — 제품 코드가 그때 쓰는 기본값으로 그린다.
    /// </summary>
    private sealed class FakeReturnDb : DbConnection
    {
        public int ReturnHeadReads { get; private set; }
        public List<string> Seen { get; } = new();

        internal DbDataReader Read(string sql)
        {
            Seen.Add(sql.Length > 60 ? sql[..60] : sql);
            var t = new DataTable();
            if (sql.Contains("FROM purchase_returns pr", StringComparison.Ordinal))
            {
                ReturnHeadReads++;
                t.Columns.Add("DocNo", typeof(string));
                t.Columns.Add("DocDate", typeof(DateTime));
                t.Columns.Add("PartnerName", typeof(string));
                t.Columns.Add("TotalSupply", typeof(decimal));
                t.Columns.Add("TotalVat", typeof(decimal));
                t.Columns.Add("TotalAmount", typeof(decimal));
                t.Rows.Add(ReturnNo, new DateTime(2026, 11, 26), "가나상사", 10000m, 1000m, 11000m);
            }
            else if (sql.Contains("FROM purchase_return_items pri", StringComparison.Ordinal))
            {
                t.Columns.Add("ItemName", typeof(string));
                t.Columns.Add("Qty", typeof(decimal));
                t.Columns.Add("UnitPrice", typeof(decimal));
                t.Columns.Add("Supply", typeof(decimal));
                t.Columns.Add("Vat", typeof(decimal));
                t.Columns.Add("Total", typeof(decimal));
                t.Rows.Add(ReturnItemName, 2m, 5000m, 10000m, 1000m, 11000m);
            }
            else if (sql.Contains("FROM local_company", StringComparison.Ordinal))
            {
                t.Columns.Add("CompanyName", typeof(string));
                t.Rows.Add("히트판상사");
            }
            else
            {
                t.Columns.Add("Unused", typeof(string));
            }
            return t.CreateDataReader();
        }

        private ConnectionState _state = ConnectionState.Open;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "fake";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "0";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(IsolationLevel il) =>
            throw new NotSupportedException("F-2 시험은 트랜잭션을 쓰지 않는다.");

        protected override DbCommand CreateDbCommand() => new Cmd(this);

        private sealed class Cmd(FakeReturnDb owner) : DbCommand
        {
            private readonly Params _ps = new();

            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override string CommandText { get; set; } = string.Empty;
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; } = CommandType.Text;
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }

            protected override DbConnection? DbConnection { get; set; } = owner;
            protected override DbParameterCollection DbParameterCollection => _ps;
            protected override DbTransaction? DbTransaction { get; set; }

            public override void Cancel() { }
            public override int ExecuteNonQuery() =>
                throw new Xunit.Sdk.XunitException($"F-2 시험은 쓰기를 하지 않는다 — 렌더가 쓰기를 했다: {CommandText}");
            public override object? ExecuteScalar() => throw new NotSupportedException();
            public override void Prepare() { }

            protected override DbParameter CreateDbParameter() => new Param();

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => owner.Read(CommandText);
        }

        private sealed class Param : DbParameter
        {
            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
            public override bool IsNullable { get; set; }
            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override string ParameterName { get; set; } = string.Empty;
            public override int Size { get; set; }
            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override string SourceColumn { get; set; } = string.Empty;
            public override bool SourceColumnNullMapping { get; set; }
            public override object? Value { get; set; }
            public override void ResetDbType() { }
        }

        private sealed class Params : DbParameterCollection
        {
            private readonly List<DbParameter> _items = new();

            public override int Count => _items.Count;
            public override object SyncRoot => _items;

            public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
            public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
            public override void Clear() => _items.Clear();
            public override bool Contains(object value) => _items.Contains((DbParameter)value);
            public override bool Contains(string value) => IndexOf(value) >= 0;
            public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
            public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
            public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
            public override int IndexOf(string parameterName) =>
                _items.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));
            public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
            public override void Remove(object value) => _items.Remove((DbParameter)value);
            public override void RemoveAt(int index) => _items.RemoveAt(index);
            public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
            protected override DbParameter GetParameter(int index) => _items[index];
            protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
            protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
            protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
        }
    }
}
