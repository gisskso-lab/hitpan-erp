using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>판매라인 상태 한글 통일 · 반품하기 경로 — 20260828작14 W4·W6 게이트</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 결재 7</b>: 수주완료 → 판매완료 → 판매확정 → 계산서발행 → 전자발행 /
/// 반품완료 → 반품확정. <b>워딩만 바꾼다</b> — DB enum 값은 한 글자도 안 건드린다(마이그 부담 0).
/// </para>
///
/// <para>
/// ⚠️ <b>이 게이트가 재는 것과 못 재는 것을 분명히 한다.</b>
/// 여기서 재는 것은 <b>라벨 사전과 배선</b>이다 — 화면에 그 글자가 실제로 떴는지는 못 잰다.
/// 8/27 작7 이 게이트 9건을 전부 통과하고도 반려된 자리가 정확히 이 틈이다
/// (<i>"고쳤나"가 아니라 "갔나"</i>). 화면 실측은 별도다.
/// </para>
/// </remarks>
public sealed class SalesStatusVocabularyGateTests
{
    // ─────────────────────────────────────────────────────────────────────
    // W4 — 상태 한글 (결재 7)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-1 — <b>DB enum 값은 그대로다.</b> 이 설계의 가장 중요한 판단이다.
    /// 값을 바꿨다면 기존 데이터·인덱스·통계·리포트를 전부 다시 손대야 한다.
    /// </summary>
    [Fact]
    public void G1_DB_enum값은_그대로다()
    {
        var src = Read("src", "HitPan.Web", "Helpers", "StatusLabel.cs");

        // 키(왼쪽)는 영문 코드 그대로여야 한다 — 한글 키로 바뀌면 DB 값과 안 맞는다
        Assert.Contains("\"confirmed\"", src);
        Assert.Contains("\"draft\"", src);
        Assert.Contains("\"cancelled\"", src);
    }

    /// <summary>
    /// 🔴 G-2 — <b>거래명세서 어휘.</b> draft=판매완료(원장 무접촉), confirmed=판매확정(재고·미수·분개).
    /// 이 둘의 차이가 곧 되돌리기 방법의 차이다(삭제 / 취소).
    /// </summary>
    [Fact]
    public void G2_거래명세서는_판매완료와_판매확정을_쓴다()
    {
        var body = Slice(Read("src", "HitPan.Web", "Helpers", "StatusLabel.cs"),
                         "public static string Delivery(", "public static string SalesReturn(");

        Assert.Contains("판매확정", body);
        Assert.Contains("판매완료", body);

        // 🔴 종전 어휘가 남아 있으면 안 된다 — 화면마다 다른 말이 뜨면 통일이 아니다
        Assert.DoesNotContain("\"임시저장\"", body);
    }

    /// <summary>
    /// 🔴 G-3 — <b>매출반품 어휘가 존재한다.</b> 반품완료(국세청 미발송) → 반품확정(발송).
    /// ⚠️ 철자 — sales_returns 는 canceled(l 하나), 명세서는 cancelled(l 둘).
    /// <b>둘 다 받아야 한다</b> — 한쪽만 받으면 나머지 한쪽이 화면에 영문으로 샌다.
    /// </summary>
    [Fact]
    public void G3_매출반품_어휘와_철자_양쪽을_받는다()
    {
        var body = Slice(Read("src", "HitPan.Web", "Helpers", "StatusLabel.cs"),
                         "public static string SalesReturn(", "SplitShipmentBadge");

        Assert.Contains("반품완료", body);
        Assert.Contains("반품확정", body);
        Assert.Contains("\"canceled\"", body);    // l 하나 — sales_returns
        Assert.Contains("\"cancelled\"", body);   // l 둘   — 혹시 섞여 들어와도 영문 노출 금지
    }

    /// <summary>
    /// 🔴 G-4 — <b>분할출고는 상태가 아니라 뱃지다</b>(결재 7 추가결정 ②).
    /// 상태로 쪼개면 거래조건(잔금까지 받음·계약금만) 조합이 폭발한다.
    /// 잔량에서 파생하므로 <b>컬럼 신설 0건</b>이다.
    /// </summary>
    [Fact]
    public void G4_분할출고는_상태가_아니라_잔량에서_파생한다()
    {
        var src = Read("src", "HitPan.Web", "Helpers", "StatusLabel.cs");

        Assert.Contains("SplitShipmentBadge", src);
        Assert.Contains("분할출고", src);

        // 🔴 파생 판정: 0 < 기출고 < 주문 일 때만 뱃지다.
        //   경계를 틀리면 전량출고에도 "분할출고" 가 뜨거나, 아예 안 뜬다.
        Assert.Contains("deliveredQty > 0 && deliveredQty < orderedQty", src);
    }

    /// <summary>
    /// 🔴 G-5 — <b>목록 화면도 같은 어휘를 쓴다.</b>
    /// 판정 사전만 고치고 화면이 자기 사전을 따로 들고 있으면 아무것도 안 바뀐다 —
    /// 실제로 <c>SalesListDialog</c> 가 사전을 <b>따로</b> 갖고 있었다.
    /// </summary>
    [Fact]
    public void G5_판매목록_화면도_같은_어휘를_쓴다()
    {
        var src = Read("src", "HitPan.Web", "Components", "Sales", "SalesListDialog.razor");

        Assert.Contains("판매확정", src);
        Assert.Contains("계산서발행", src);
        Assert.Contains("판매완료", src);

        // draft 를 "임시" 로 부르던 자리가 남아 있으면 안 된다
        Assert.DoesNotContain("\"draft\" => \"임시\"", src);
    }

    // ─────────────────────────────────────────────────────────────────────
    // W6 — 반품하기 경로 (결재 2·3·5)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-6 — <b>반품하기 버튼이 거래명세서 화면에 있다</b>(결재 5).
    /// 새 화면을 만들지 않는다 — 새 화면은 곧 새 진입점이고, 진입점이 늘면
    /// 가드가 또 한쪽에만 붙는다(P0-1 이 정확히 그 사고).
    /// </summary>
    [Fact]
    public void G6_반품하기_버튼이_거래명세서_화면에_있다()
    {
        var src = Read("src", "HitPan.Web", "Pages", "Sales", "DeliveryPage.razor");

        Assert.Contains("반품하기", src);
        Assert.Contains("OpenReturn", src);

        // 🔴 판매확정 건에만 열린다 — 확정 전에는 재고도 미수금도 안 움직였으므로
        //   되돌릴 것이 없다. 그 자리는 반품이 아니라 삭제다(결재 7 되돌리기 3단).
        Assert.Contains("_draft?.Status != \"confirmed\"", src);
    }

    /// <summary>
    /// 🔴 G-7 — <b>같은 화면으로 돌아오고, 원전표를 실제로 읽어 (−)로 채운다.</b>
    ///
    /// <para>
    /// 🔴 이 검사가 중요한 이유 — 버튼만 있고 <c>returnOf</c> 를 <b>받는 쪽이 없으면</b>
    /// 눌러도 그냥 빈 새 전표가 열린다. 「막는 것 ≠ 알려주는 것」과 같은 종류의 틈이다.
    /// 보내는 쪽과 받는 쪽을 <b>둘 다</b> 잰다.
    /// </para>
    /// </summary>
    [Fact]
    public void G7_반품경로는_보내는쪽과_받는쪽이_모두_있다()
    {
        var src = Read("src", "HitPan.Web", "Pages", "Sales", "DeliveryPage.razor");

        // 보내는 쪽 — 같은 화면(/deliveries)으로 원전표 id 를 달고 간다
        Assert.Contains("/deliveries?returnOf=", src);

        // 받는 쪽 — 쿼리를 실제로 수신한다
        Assert.Contains("SupplyParameterFromQuery(Name = \"returnOf\")", src);

        // 🔴 적재 메서드를 **실제로 부르는지** 본다.
        //   낱말 하나로 검사하면 안 된다 — 메서드 정의만 남고 호출이 주석 처리돼도
        //   이름은 파일에 그대로 있어서 통과한다(게이트 체크리스트 ⑥·⑧).
        //   ⇒ 이 게이트를 만들 때 실제로 그 함정에 빠졌고, 반증에서 잡아 고쳤다.
        //   정의 줄(private async Task ...)을 뺀 나머지에 살아있는 호출이 있어야 한다.
        var callSites = src.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains("TryLoadReturnSourceAsync")
                        && !l.StartsWith("//")            // 주석 처리된 호출은 호출이 아니다
                        && !l.StartsWith("private")       // 정의는 호출이 아니다
                        && !l.StartsWith("///"))
            .ToList();
        Assert.True(callSites.Count > 0,
            "TryLoadReturnSourceAsync 를 실제로 부르는 자리가 없다 — 정의만 있고 안 부르면 반품 적재가 안 일어난다.");

        // 🔴 (−)로 채운다 — 부호가 안 뒤집히면 반품이 아니라 판매가 한 장 더 생긴다
        Assert.Contains("Quantity = -it.Qty", src);
    }

    /// <summary>
    /// 🔴 G-8 — <b>사슬 근거가 남는다</b>(결재 2 — 사장님: <i>"근거가 뭔지 분명하게 사슬로 연결"</i>).
    /// 근거 없는 마이너스는 장부를 못 읽게 만든다.
    /// 헤더는 원전표 번호를, 줄은 <c>delivery_item_id</c> 를 남긴다.
    /// </summary>
    [Fact]
    public void G8_반품전표에_사슬근거가_남는다()
    {
        var src = Read("src", "HitPan.Web", "Pages", "Sales", "DeliveryPage.razor");

        Assert.Contains("반품 : {src.DeliveryNo}", src);              // 헤더 — 원전표 번호
        Assert.Contains("DeliveryItemId = it.DeliveryItemId", src);   // 줄 — 원 판매 줄

        // 🔴 담당자가 자기가 무엇을 쓰고 있는지 알아야 한다 — 같은 화면을 재사용하는 설계라
        //   알려주지 않으면 (−) 숫자만 보인다.
        Assert.Contains("_isReturnMode", src);
    }

    // ─────────────────────────────────────────────────────────────────────
    // W7 — 확정 건 2단 통제 (결재 8)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-9 — <b>확정 건 되돌리기에 패스워드 2단이 걸린다</b>(사장님 결재 8).
    ///
    /// <para>
    /// 1단(권한 검사)은 서버에 이미 있다 — <c>[Authorize(Policy="SalesManager")]</c>.
    /// 2단이 이번에 신설한 것이다. 방식은 사장님 확정: <b>로그인한 본인(권한가진자) 비밀번호</b>.
    /// </para>
    ///
    /// <para>
    /// 문구는 사장님이 지정하셨다 — <b>그대로 써야 한다.</b> 임의로 다듬으면 받아쓰기의 반대편이다.
    /// </para>
    /// </summary>
    [Fact]
    public void G9_확정건_되돌리기에_패스워드_2단이_걸린다()
    {
        var src = Read("src", "HitPan.Web", "Pages", "Sales", "DeliveryPage.razor");

        // 사장님 지정 문구 — 한 글자도 바꾸지 않는다
        Assert.Contains("확정된 거래 입니다. 취소하시려면 패스워드 입력이 필요합니다", src);

        // 매출취소·삭제 두 자리 모두 — 결재 8: "삭제뿐 아니라 수정도" 대상
        var hits = src.Split("StepUpDialog.RequestAsync").Length - 1;
        Assert.True(hits >= 2,
            $"확정 건 되돌리기 2단이 {hits} 곳뿐이다 — 매출취소·삭제 양쪽에 있어야 한다.");

        // 🔴 확정 건에만 묻는다 — 확정 전까지 물으면 일상 업무가 번거로워진다.
        //   원장이 움직인 건만 막는다(결재 7 되돌리기 3단).
        Assert.Contains("_draft?.Status == \"confirmed\"", src);
    }

    /// <summary>
    /// 🔴 G-10 — <b>본인 비밀번호로 검증한다</b>(사장님 결재 확정).
    /// 남의 비밀번호나 고정 비밀번호를 받으면 통제가 아니라 형식이 된다.
    /// 서버가 JWT 의 userId 로 본인을 찾아 대조하는지까지 본다.
    /// </summary>
    [Fact]
    public void G10_본인_비밀번호로_검증한다()
    {
        var svc = Read("src", "HitPan.Application", "Services", "AuthService.cs");
        Assert.Contains("VerifyOwnPasswordAsync", svc);
        Assert.Contains("BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)", svc);

        var ctrl = Read("src", "HitPan.API", "Controllers", "AuthController.cs");
        // 🔴 userId 는 JWT 에서 온다 — 파라미터로 받으면 남의 계정으로 통과시킬 수 있다(헌법 #2 정신).
        Assert.Contains("HttpContext.Items[\"UserId\"]", ctrl);
        Assert.Contains("VerifyOwnPasswordAsync", ctrl);
    }

    // ─────────────────────────────────────────────────────────────────────

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string Slice(string src, string from, string to)
    {
        var a = src.IndexOf(from, StringComparison.Ordinal);
        Assert.True(a >= 0, $"앵커를 못 찾았다: {from} — 게이트가 딴 데를 보고 있다.");
        var b = src.IndexOf(to, a, StringComparison.Ordinal);
        return b < 0 ? src[a..] : src[a..b];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "HitPan.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }
}
