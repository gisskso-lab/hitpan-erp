using System.Reflection;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261024 작2 — 양식지 <b>용지·방향 4칸</b> + <b>그림 올리기 통로</b> 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 근거: 사장님 2026-10-24 ①*"3건 모두 결재. 즉시 진행해."* ②*"이미지파일로 양식지 추가시,
/// PDF편집기로 즉시 반영될 수 있도록 설계, 구현해"* ·
/// <see href="docs/운영기록/20261024작2_D열_양식지4칸_이미지업로드_즉시반영_작업지시서.md">작업지시서 §5</see>
/// </para>
/// <para>
/// 🔴 <b>축을 센다, 건수를 세지 않는다.</b> 10/24 에 「42/42 초록」이면서 핵심 추가물이
/// 보호선 0건이었다 — <c>WiredDocTypes()</c> 를 <b>손으로 7개</b> 적었기 때문이다.
/// 그래서 축 F 는 <b>손 목록을 쓰지 않고 긁는다.</b> 칸이 늘고 배선이 빠지면 <b>스스로 빨개진다.</b>
/// </para>
/// <para>
/// 축 여섯: ⒜올리기가 <b>테넌트 폴더 안에만</b> 쓴다 · ⒝형식·크기·깨진 바이트를 <b>각각</b> 거절한다 ·
/// ⒞고객 파일 <b>이름을 쓰지 않는다</b> · ⒟용지·방향이 <b>종이에 닿는다</b> ·
/// ⒠모르는 값은 <b>A4·세로로 떨어진다</b> · ⒡양식 칸 배선을 <b>긁어서</b> 본다.
/// </para>
/// <para>
/// 🔴 <b>이 게이트가 못 재는 것</b>(「통과」로 적지 않는다):
///   종이에 A5·가로가 실제로 나오는지는 <b>프린터 실물</b>이라 여기서 못 잰다(종이 0회 · 일곱 차수 연속) ·
///   직인은 보관소가 없어 <c>seal_url</c> 이 비어 있어 <b>켬/끔 배선만</b> 재는 것이다 ·
///   스타일 4종은 디자인 미확정이라 <b>basic 으로 떨어지는 것만</b> 잰다 ·
///   편집기에서 「고르면 바로 그려지나」는 브라우저라 여기서 못 잰다.
/// </para>
/// <para>🔴 기존 시험 파일은 <b>한 줄도 안 고쳤다</b>(#1). DB 를 쓰지 않아 CI 에서 건너뛰지 않는다.</para>
/// </remarks>
public class FormPaperSizeAndBackgroundUploadGateTests
{
    // 1x1 PNG — 작성기가 실제로 받아들이는 바이트.
    private static readonly byte[] RealPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static FormBackgroundStore MakeStore() =>
        new(NullLogger<FormBackgroundStore>.Instance);

    private static PdfRenderService MakeRenderService() =>
        (PdfRenderService)Activator.CreateInstance(
            typeof(PdfRenderService),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: BuildRenderServiceArgs(),
            culture: null)!;

    /// <summary>작성기를 DB 없이 만든다 — 참조형 인자는 null, 로거만 넣는다.</summary>
    private static object?[] BuildRenderServiceArgs()
    {
        var ctor = typeof(PdfRenderService).GetConstructors().First();
        return ctor.GetParameters().Select(p =>
            p.ParameterType.IsGenericType
            && p.ParameterType.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>)
                ? Activator.CreateInstance(
                    typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments()[0]))
                : null).ToArray();
    }

    private static MethodInfo Private(string name) =>
        typeof(PdfRenderService).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{name} 이 없다 — 배선이 지워졌다.");

    private static Type InfoType =>
        typeof(PdfRenderService).GetNestedType("FormTemplateInfo", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("FormTemplateInfo 가 없다.");

    // ──────────────────────────────────────────────────────────────
    // 축 ⒜ 올리기는 테넌트 폴더 안에만 쓴다
    // ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "축⒜ 🔴 올린 그림은 **그 테넌트 폴더 안**에 생긴다")]
    public async Task AxisA_SavesInsideTenantFolder()
    {
        var tenant = $"gate-{Guid.NewGuid():N}";
        var store = MakeStore();

        var (path, why) = await store.SaveAsync(tenant, "내양식지.png", new MemoryStream(RealPng));
        try
        {
            Assert.Null(why);
            Assert.NotNull(path);
            Assert.True(File.Exists(path!), "파일이 실제로 생기지 않았다 — 「올렸다」가 거짓이다.");

            var tenantRoot = Path.GetFullPath(FormBackgroundPaths.TenantRoot(tenant));
            Assert.StartsWith(tenantRoot, Path.GetFullPath(path!), StringComparison.OrdinalIgnoreCase);

            // 🔴 그리고 그 자리는 **작성기가 읽는 뿌리 안**이어야 한다 — 아니면 올려도 안 나온다.
            Assert.StartsWith(Path.GetFullPath(FormBackgroundPaths.Root),
                Path.GetFullPath(path!), StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "축⒜음 🔴 음성 대조군 — 테넌트가 비면 **아무것도 쓰지 않는다**(#2)")]
    public async Task AxisA_Negative_NoTenantWritesNothing()
    {
        var store = MakeStore();
        var (path, why) = await store.SaveAsync("   ", "a.png", new MemoryStream(RealPng));

        Assert.Null(path);
        Assert.NotNull(why);
    }

    [Fact(DisplayName = "축⒜음 🔴 음성 대조군 — 테넌트 값이 폴더를 벗어나는 모양이면 거절")]
    public async Task AxisA_Negative_TraversalTenantRejected()
    {
        var store = MakeStore();
        var (path, why) = await store.SaveAsync("..", "a.png", new MemoryStream(RealPng));

        Assert.Null(path);
        Assert.NotNull(why);
    }

    // ──────────────────────────────────────────────────────────────
    // 축 ⒝ 형식·크기·깨진 바이트를 각각 거절한다
    // ──────────────────────────────────────────────────────────────

    [Theory(DisplayName = "축⒝ 🔴 그림이 아닌 형식은 **보관소에 들어가지 않는다**")]
    [InlineData("payload.pdf")]
    [InlineData("payload.svg")]
    [InlineData("payload.exe")]
    [InlineData("payload")]
    public async Task AxisB_RejectsWrongExtension(string name)
    {
        var tenant = $"gate-{Guid.NewGuid():N}";
        try
        {
            var (path, why) = await MakeStore().SaveAsync(tenant, name, new MemoryStream(RealPng));
            Assert.Null(path);
            Assert.NotNull(why);
            Assert.False(Directory.Exists(FormBackgroundPaths.TenantRoot(tenant)),
                "거절했는데 폴더를 만들었다 — 검사 전에 디스크를 건드린 것이다.");
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "축⒝ 🔴 빈 파일은 거절한다")]
    public async Task AxisB_RejectsEmpty()
    {
        var tenant = $"gate-{Guid.NewGuid():N}";
        try
        {
            var (path, why) = await MakeStore().SaveAsync(tenant, "a.png", new MemoryStream(Array.Empty<byte>()));
            Assert.Null(path);
            Assert.NotNull(why);
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "축⒝ 🔴 이름만 그림이고 속이 쓰레기면 **보관소에 넣지 않는다**")]
    public async Task AxisB_RejectsCorruptBytes()
    {
        var tenant = $"gate-{Guid.NewGuid():N}";
        try
        {
            var junk = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 이건 그림이 아니다");
            var (path, why) = await MakeStore().SaveAsync(tenant, "a.png", new MemoryStream(junk));

            Assert.Null(path);
            Assert.NotNull(why);

            // 🔴 이 축이 핵심이다 — 못 읽을 파일이 보관소에 쌓이면 고객은
            //   「올라갔는데 안 나온다」를 겪고, 우리는 원인을 인쇄 쪽에서 찾게 된다.
            var dir = FormBackgroundPaths.TenantRoot(tenant);
            Assert.True(!Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0,
                "못 읽는 바이트가 보관소에 들어갔다.");
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "축⒝ 🔴 상한을 넘는 그림은 거절한다 (올리는 쪽·읽는 쪽 **같은 값**)")]
    public async Task AxisB_RejectsOversize()
    {
        var tenant = $"gate-{Guid.NewGuid():N}";
        try
        {
            var big = new byte[FormBackgroundPaths.MaxBytes + 1];
            var (path, why) = await MakeStore().SaveAsync(tenant, "a.png", new MemoryStream(big));
            Assert.Null(path);
            Assert.NotNull(why);

            // 🔴 작성기 상한과 **같은 값**인지 — 둘이 어긋나면 올라간 그림이 안 읽힌다.
            var cap = (long)typeof(PdfRenderService)
                .GetField("MaxBackgroundBytes", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;
            Assert.Equal(FormBackgroundPaths.MaxBytes, cap);
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "축⒝ 🔴 받아 주는 형식 목록이 **한 자리**다 (두 벌이면 어긋난다)")]
    public void AxisB_ExtensionListIsSingleSource()
    {
        var prop = typeof(PdfRenderService).GetProperty("AllowedBackgroundExts",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(prop);

        var readerExts = (HashSet<string>)prop!.GetValue(null)!;
        Assert.Same(FormBackgroundPaths.AllowedExts, readerExts);
    }

    // ──────────────────────────────────────────────────────────────
    // 축 ⒞ 고객 파일 이름을 쓰지 않는다
    // ──────────────────────────────────────────────────────────────

    [Theory(DisplayName = "축⒞ 🔴 고객 파일 **이름을 쓰지 않는다** (덮어쓰기·예약어·거슬러 오르기 차단)")]
    [InlineData("CON.png")]
    [InlineData("..\\..\\탈출.png")]
    [InlineData("거래명세서 양식.png")]
    public async Task AxisC_DoesNotUseCustomerFileName(string given)
    {
        var tenant = $"gate-{Guid.NewGuid():N}";
        var store = MakeStore();
        try
        {
            var (path, why) = await store.SaveAsync(tenant, given, new MemoryStream(RealPng));
            Assert.Null(why);
            Assert.NotNull(path);

            var saved = Path.GetFileNameWithoutExtension(path!);
            var givenStem = Path.GetFileNameWithoutExtension(given);
            Assert.DoesNotContain(givenStem, saved, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "축⒞ 🔴 같은 이름을 두 번 올려도 **먼저 올린 그림이 안 사라진다**(#1)")]
    public async Task AxisC_SecondUploadDoesNotOverwrite()
    {
        var tenant = $"gate-{Guid.NewGuid():N}";
        var store = MakeStore();
        try
        {
            var (first, _) = await store.SaveAsync(tenant, "양식지.png", new MemoryStream(RealPng));
            var (second, _) = await store.SaveAsync(tenant, "양식지.png", new MemoryStream(RealPng));

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotEqual(first, second);
            Assert.True(File.Exists(first!), "먼저 올린 그림이 덮여 사라졌다.");
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "축⒞ 🔴 남의 폴더 파일은 **지우지 못한다**")]
    public void AxisC_CannotDeleteOutsideTenant()
    {
        var mine = $"gate-{Guid.NewGuid():N}";
        var other = $"gate-{Guid.NewGuid():N}";
        var store = MakeStore();

        var otherDir = FormBackgroundPaths.TenantRoot(other);
        Directory.CreateDirectory(otherDir);
        var victim = Path.Combine(otherDir, "남의양식지.png");
        File.WriteAllBytes(victim, RealPng);

        try
        {
            var why = store.Delete(mine, victim);
            Assert.NotNull(why);
            Assert.True(File.Exists(victim), "남의 테넌트 그림이 지워졌다.");
        }
        finally { Cleanup(mine); Cleanup(other); }
    }

    // ──────────────────────────────────────────────────────────────
    // 축 ⒟ 용지·방향이 종이에 닿는다
    // ──────────────────────────────────────────────────────────────

    [Theory(DisplayName = "축⒟ 🔴 용지 크기가 **실제로 바뀐다** (A4 고정이 아니다)")]
    [InlineData("A5")]
    [InlineData("Letter")]
    [InlineData("letter")]
    [InlineData("a5")]
    public void AxisD_PaperSizeChangesPaper(string size)
    {
        var a4 = ResolveSize("A4", "portrait");
        var other = ResolveSize(size, "portrait");

        // 🔴 같으면 「배선했다」가 거짓이다 — 고객이 골라도 같은 종이가 나간다.
        Assert.False(Math.Abs(a4.Width - other.Width) < 0.5f
                     && Math.Abs(a4.Height - other.Height) < 0.5f,
            $"{size} 가 A4 와 같은 종이로 나왔다 — 용지 칸이 안 닿는다.");
    }

    [Fact(DisplayName = "축⒟ 🔴 가로는 **같은 종이를 눕힌 것**이다")]
    public void AxisD_LandscapeFlipsSamePaper()
    {
        var portrait = ResolveSize("A4", "portrait");
        var landscape = ResolveSize("A4", "landscape");

        Assert.True(portrait.Height > portrait.Width, "세로가 세로가 아니다.");
        Assert.True(landscape.Width > landscape.Height, "가로로 눕지 않았다 — 방향 칸이 안 닿는다.");
        Assert.Equal(portrait.Width, landscape.Height, 1);
        Assert.Equal(portrait.Height, landscape.Width, 1);
    }

    [Fact(DisplayName = "축⒟ 🔴 A5 가로도 눕는다 (용지·방향이 **같이** 먹는다)")]
    public void AxisD_A5LandscapeAlsoFlips()
    {
        var a5 = ResolveSize("A5", "portrait");
        var a5Land = ResolveSize("A5", "landscape");

        Assert.True(a5Land.Width > a5Land.Height);
        Assert.Equal(a5.Width, a5Land.Height, 1);
    }

    // ──────────────────────────────────────────────────────────────
    // 축 ⒠ 모르는 값은 A4·세로로 떨어진다
    // ──────────────────────────────────────────────────────────────

    [Theory(DisplayName = "축⒠ 🔴 모르는 값이 와도 **종이는 나간다** — A4·세로로 떨어진다(#20)")]
    [InlineData("A3")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("레터")]
    [InlineData("A4 ")]
    public void AxisE_UnknownFallsBackToA4(string size)
    {
        var a4 = ResolveSize("A4", "portrait");
        var got = ResolveSize(size, "portrait");

        Assert.Equal(a4.Width, got.Width, 1);
        Assert.Equal(a4.Height, got.Height, 1);
    }

    [Theory(DisplayName = "축⒠ 🔴 모르는 방향은 **세로**다 (눕히지 않는다)")]
    [InlineData("가로")]
    [InlineData("")]
    [InlineData("LANDSCAPE ")]
    public void AxisE_UnknownOrientationIsPortrait(string orientation)
    {
        var got = ResolveSize("A4", orientation);
        // "LANDSCAPE " 는 공백을 털고 대소문자를 무시하므로 **가로가 맞다**.
        if (orientation.Trim().Equals("landscape", StringComparison.OrdinalIgnoreCase))
            Assert.True(got.Width > got.Height);
        else
            Assert.True(got.Height > got.Width, $"모르는 방향({orientation})인데 눕혔다.");
    }

    [Fact(DisplayName = "축⒠ 🔴 양식이 아예 없으면 A4·세로 (신규 고객 자리)")]
    public void AxisE_NullTemplateIsA4Portrait()
    {
        var svc = MakeRenderService();
        var got = (QuestPDF.Helpers.PageSize)Private("ResolvePageSize")
            .Invoke(svc, new object?[] { null })!;
        var a4 = ResolveSize("A4", "portrait");

        Assert.Equal(a4.Width, got.Width, 1);
        Assert.Equal(a4.Height, got.Height, 1);
    }

    [Theory(DisplayName = "축⒠ 🔴 스타일은 아직 **무엇이 와도 basic** (4종 디자인 미확정 · 배선만)")]
    [InlineData("basic")]
    [InlineData("modern")]
    [InlineData("")]
    [InlineData("아무거나")]
    public void AxisE_StyleAlwaysBasicForNow(string key)
    {
        var svc = MakeRenderService();
        var info = Activator.CreateInstance(InfoType)!;
        InfoType.GetProperty("StyleKey")!.SetValue(info, key);

        var got = (string)Private("ResolveStyleKey").Invoke(svc, new[] { info })!;
        Assert.Equal("basic", got);
    }

    // ──────────────────────────────────────────────────────────────
    // 축 ⒡ 🔴 손 목록을 쓰지 않는다 — 긁어서 본다
    // ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "축⒡ 🔴🔴 양식 칸 배선을 **긁어서** 본다 — 칸이 늘고 배선이 빠지면 스스로 빨개진다")]
    public void AxisF_EveryMatchingFieldIsWired()
    {
        var svc = MakeRenderService();
        var blank = Activator.CreateInstance(InfoType)!;
        var dto = new FormTemplateDto { FormType = "delivery" };

        var dtoProps = typeof(FormTemplateDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

        // 🔴 손으로 칸 이름을 적지 않는다 — 작성기의 양식 칸을 **긁어서** 짝을 찾는다.
        var scraped = new List<PropertyInfo>();
        foreach (var ip in InfoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!dtoProps.TryGetValue(ip.Name, out var dp)) continue;
            if (dp.PropertyType != ip.PropertyType) continue;
            if (!dp.CanWrite || !ip.CanRead) continue;

            // 기본값과 **다른** 값을 넣는다 — 같은 값이면 배선이 빠져도 통과한다(거짓 초록).
            var probe = DifferentFrom(ip.PropertyType, ip.GetValue(blank));
            if (probe is null) continue;

            dp.SetValue(dto, probe);
            scraped.Add(ip);
        }

        // 🔴 긁은 칸이 적으면 **긁기 자체가 깨진 것**이다 — 0건이면 이 시험은 아무것도 안 본다.
        Assert.True(scraped.Count >= 8,
            $"긁은 양식 칸이 {scraped.Count}개뿐이다 — 이름·형이 어긋나 긁기가 깨졌다.");

        var info = Private("ToTemplateInfo").Invoke(svc, new object?[] { dto })!;

        foreach (var ip in scraped)
        {
            var expected = dtoProps[ip.Name].GetValue(dto);
            var actual = ip.GetValue(info);
            Assert.True(Equals(expected, actual),
                $"양식 칸 '{ip.Name}' 이 작성기까지 안 간다 — 고객이 골라도 종이가 안 바뀐다.");
        }
    }

    [Fact(DisplayName = "축⒡ 🔴 용지·방향·직인·스타일 네 칸이 **작성기 칸으로 실재한다**")]
    public void AxisF_FourFieldsExistOnRenderer()
    {
        // 🔴 이름을 적는 유일한 자리다 — 사장님 결재 1 의 **대상 그 자체**라서 적는다.
        //   배선 여부는 위 축⒡ 가 긁어서 본다. 여기는 「칸이 사라지지 않았나」만 본다.
        foreach (var name in new[] { "PaperSize", "Orientation", "ShowCompanySeal", "StyleKey" })
        {
            Assert.NotNull(InfoType.GetProperty(name));
        }
    }

    [Fact(DisplayName = "축⒡ 🔴 직인 끄기는 **작성기 서명을 건드리지 않고** 집행된다 (게이트 136건 사고 자리)")]
    public void AxisF_SealSwitchDoesNotChangeRendererSignature()
    {
        // 🔴🔴 이 축은 **내가 낸 사고**에서 나왔다.
        //   처음에 `RenderTaxInvoiceDocument` 에 인자를 하나 더했다(기본값 true · 호출부 1곳).
        //   C# 으로는 호환이지만 기존 게이트 136건은 **반사로 인자 8개를 정확히** 넘긴다
        //   ⇒ `Invoke` 가 개수를 맞추라고 터지며 **108건이 한 번에 빨개졌다**
        //      (기준선 157 → 265). 빌드는 0/0 이었고 내 시험 41건도 전부 초록이었다.
        //   ⇒ 그래서 이 축은 「서명이 그대로인가」를 못으로 박아 둔다. 다음 사람이
        //      같은 유혹(인자 하나만 더하기)에 빠지면 **여기서 먼저 빨개진다.**
        var m = typeof(PdfRenderService).GetMethod("RenderTaxInvoiceDocument",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);

        var names = m!.GetParameters().Select(p => p.Name).ToArray();
        Assert.Equal(
            new[] { "d", "c", "copyMode", "showTearLine",
                    "marginTop", "marginLeft", "marginRight", "marginBottom" },
            names);
    }

    [Fact(DisplayName = "축⒡ 🔴 직인을 끄면 **직인 주소를 비운다** (뜻은 같고 서명은 그대로)")]
    public void AxisF_SealOffClearsSealUrlAtCallSite()
    {
        var src = ReadSource("src", "HitPan.Application", "Services", "PdfRenderService.cs");

        // 🔴 켬/끔을 읽는 자리와, 그것이 실제로 하는 일이 **둘 다** 있어야 한다.
        Assert.Contains("!template.ShowCompanySeal", src, StringComparison.Ordinal);
        Assert.Contains("company.SealUrl = null;", src, StringComparison.Ordinal);

        // 🔴 그리고 직인을 읽는 자리는 그 주소만 본다 — 비면 안 읽는다.
        Assert.Contains("TryLoadSealImage(c.SealUrl)", src, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────
    // 축 ⒢ 🔴 **부른 자리**를 본다 — 함수가 맞게 돌아도 안 부르면 종이는 안 바뀐다
    // ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "축⒢ 🔴🔴 공용 흐름이 **ResolvePageSize 를 실제로 부른다** (되돌리면 빨개진다)")]
    public void AxisG_CallSiteUsesResolvePageSize()
    {
        // 🔴 왜 이 축이 따로 필요한가 — 위 축⒟⒠ 는 **함수를 직접 불러** 잰다.
        //   그래서 누가 호출부를 `page.Size(PageSizes.A4)` 로 되돌려도 전부 초록이다.
        //   10/24 G-1 이 정확히 그 모양이었다(분기를 빼도 42/42 초록).
        //   ⇒ 「부른 자리」를 본다. 되돌리면 이 줄이 사라지므로 **빨개진다.**
        var src = ReadSource("src", "HitPan.Application", "Services", "PdfRenderService.cs");

        Assert.Contains("page.Size(ResolvePageSize(template))", src, StringComparison.Ordinal);
        Assert.Contains("_ = ResolveStyleKey(template);", src, StringComparison.Ordinal);

    }

    [Fact(DisplayName = "축⒢ 🔴 올리는 문이 **실재한다** — 입구가 없으면 그림을 넣을 길이 0이다")]
    public void AxisG_UploadEndpointExists()
    {
        var src = ReadSource("src", "HitPan.API", "Controllers", "FormTemplateController.cs");

        Assert.Contains("[HttpPost(\"background\")]", src, StringComparison.Ordinal);
        Assert.Contains("IFormBackgroundStore", src, StringComparison.Ordinal);

        // 🔴 tenant_id 는 JWT 에서만(#2) — 본문·쿼리로 받는 길을 만들면 즉시 반려다.
        Assert.Contains("HttpContext.Items[\"TenantId\"]", src, StringComparison.Ordinal);

        // 🔴 쓰기 문은 양식 고치는 역할과 같은 문이어야 한다.
        Assert.Contains("[Authorize(Roles = \"system_admin,tenant_admin\")]", src, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "축⒢ 🔴 편집기가 올린 뒤 **미리보기를 다시 그린다** — 이것이 「즉시 반영」이다")]
    public void AxisG_EditorRedrawsPreviewAfterUpload()
    {
        var src = ReadSource("src", "HitPan.Web", "Components", "Common", "FormTemplateEditorDialog.razor");

        // 파일을 고르는 자리가 있나
        Assert.Contains("OnBackgroundFileChangedAsync", src, StringComparison.Ordinal);
        Assert.Contains("api/settings/form-templates/background", src, StringComparison.Ordinal);

        // 🔴 올린 경로를 **화면 값에 넣나** — 안 넣으면 미리보기가 옛 그림을 그린다
        Assert.Contains("_form.BackgroundImageUrl = saved.StoredPath;", src, StringComparison.Ordinal);

        // 🔴 그리고 **바로 다시 그리나** — 이 줄이 없으면 고객이 단추를 또 눌러야 한다
        var idx = src.IndexOf("_form.BackgroundImageUrl = saved.StoredPath;", StringComparison.Ordinal);
        Assert.True(idx > 0);
        Assert.Contains("await PreviewAsync();", src[idx..], StringComparison.Ordinal);

        // 🔴 주소 칸을 지우지 않았나(#1) — 이미 주소로 맞춰 둔 고객이 있을 수 있다
        Assert.Contains("@bind-Value=\"_form.BackgroundImageUrl\"", src, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null, "레포 루트를 찾아야 한다");
        var path = Path.Combine(new[] { dir! }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path, System.Text.Encoding.UTF8);
    }

    // ──────────────────────────────────────────────────────────────

    private static QuestPDF.Helpers.PageSize ResolveSize(string paperSize, string orientation)
    {
        var svc = MakeRenderService();
        var info = Activator.CreateInstance(InfoType)!;
        InfoType.GetProperty("PaperSize")!.SetValue(info, paperSize);
        InfoType.GetProperty("Orientation")!.SetValue(info, orientation);

        return (QuestPDF.Helpers.PageSize)Private("ResolvePageSize").Invoke(svc, new[] { info })!;
    }

    /// <summary>기본값과 <b>다른</b> 값을 만든다 — 같은 값으로 재면 거짓 초록이 난다.</summary>
    private static object? DifferentFrom(Type type, object? current) => type switch
    {
        _ when type == typeof(bool) => !(bool)(current ?? false),
        _ when type == typeof(string) => (current as string) == "GATE-A" ? "GATE-B" : "GATE-A",
        _ when type == typeof(decimal) => (decimal)(current ?? 0m) + 7.5m,
        _ when type == typeof(int) => (int)(current ?? 0) + 7,
        _ => null,
    };

    private static void Cleanup(string tenant)
    {
        try
        {
            var dir = FormBackgroundPaths.TenantRoot(tenant);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            // 🔴 빈 catch 금지(#15). 시험 뒤처리 실패는 시험을 깨뜨리지 않는다.
            Console.Error.WriteLine($"[gate cleanup] {ex.Message}");
        }
    }
}
