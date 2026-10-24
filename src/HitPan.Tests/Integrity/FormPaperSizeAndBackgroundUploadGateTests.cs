using System.Reflection;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
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
    static FormPaperSizeAndBackgroundUploadGateTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

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

    [Fact(DisplayName = "축⒝ 🔴 받아 주는 형식·상한이 **한 자리**다 — 🆕 **화면 벌까지** (반려 R-1)")]
    public void AxisB_ExtensionListIsSingleSource()
    {
        var prop = typeof(PdfRenderService).GetProperty("AllowedBackgroundExts",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(prop);

        var readerExts = (HashSet<string>)prop!.GetValue(null)!;
        Assert.Same(FormBackgroundPaths.AllowedExts, readerExts);

        // 🔴🔴 [2026-10-25 [4] 반려 R-1 봉합] 종전에는 **서버 두 자리만** 대조했다.
        //   화면(`HitPan.Web`)은 `HitPan.Application` 을 참조하지 않아 razor 안에
        //   `20L * 1024 * 1024` 와 `Accept=".png,..."` **두 번째 벌**이 있었고,
        //   이 축은 그걸 못 봤다 ⇒ 어긋나도 50/50 초록이었다.
        //   ⇒ 값을 `HitPan.Contracts` 로 내렸고, **화면이 그 자리를 읽는지**를 여기서 본다.
        var razor = ReadCode("src", "HitPan.Web", "Components", "Common", "FormTemplateEditorDialog.razor");

        Assert.Contains("FormBackgroundLimits.MaxBytes", razor, StringComparison.Ordinal);
        Assert.Contains("FormBackgroundLimits.AcceptAttribute", razor, StringComparison.Ordinal);

        // 🔴 두 번째 벌이 **다시 생기면** 빨개진다 — 숫자를 손으로 적은 자리가 없어야 한다.
        Assert.DoesNotContain("20L * 1024 * 1024", razor, StringComparison.Ordinal);
        Assert.DoesNotContain(".png,.jpg", razor, StringComparison.Ordinal);

        // 🔴 서버 상한도 같은 자리를 읽나.
        Assert.Equal(HitPan.Contracts.Forms.FormBackgroundLimits.MaxBytes, FormBackgroundPaths.MaxBytes);
    }

    [Theory(DisplayName = "축⒝ 🔴 대문자·이중 확장자도 **같은 규칙**으로 갈린다 (반려 R-9 미측정 입력)")]
    [InlineData("양식.PNG", true)]
    [InlineData("양식.JpEg", true)]
    [InlineData("양식.png.exe", false)]
    [InlineData("양식.exe.png", true)]
    public async Task AxisB_CaseAndDoubleExtension(string name, bool shouldPass)
    {
        // 🔴 [4] 가 「못 쟀다」로 적은 입력들이다. 🔴 `a.exe.png` 가 통과하는 것은 **정상**이다
        //   — 마지막 확장자가 그림이고, 속을 열어 **그려 보기**까지 통과해야 보관된다.
        //   이름이 아니라 **바이트**가 판정의 바닥이라는 뜻이고, 그게 설계 의도다.
        var tenant = $"gate-{Guid.NewGuid():N}";
        try
        {
            var (path, why) = await MakeStore().SaveAsync(tenant, name, new MemoryStream(RealPng));

            if (shouldPass)
            {
                Assert.Null(why);
                Assert.NotNull(path);
            }
            else
            {
                Assert.Null(path);
                Assert.NotNull(why);
            }
        }
        finally { Cleanup(tenant); }
    }

    [Theory(DisplayName = "축⒝ 🔴 상한 **경계값** — 정확히 상한은 받고, 한 바이트 넘으면 거절 (반려 R-9)")]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task AxisB_SizeBoundary(int delta, bool shouldPass)
    {
        // 🔴 경계는 둘 다 재야 뜻이 있다. 「넘으면 거절」만 재면 상한을 1 로 바꿔도 통과한다.
        var tenant = $"gate-{Guid.NewGuid():N}";
        try
        {
            var body = new byte[FormBackgroundPaths.MaxBytes + delta];
            RealPng.CopyTo(body, 0);   // 앞머리는 진짜 PNG — 크기 축만 재려고

            var (path, why) = await MakeStore().SaveAsync(tenant, "a.png", new MemoryStream(body));

            if (shouldPass)
            {
                // 🔴 상한과 같은 크기는 **크기 때문에** 거절되지 않는다.
                //    (뒤가 쓰레기라 「그려 보기」에서 떨어질 수 있다 — 그건 다른 축이다.)
                Assert.True(why is null || !why.Contains("큽니다", StringComparison.Ordinal),
                    $"정확히 상한인데 크기로 거절했다: {why}");
            }
            else
            {
                Assert.Null(path);
                Assert.NotNull(why);
                Assert.Contains("큽니다", why!, StringComparison.Ordinal);
            }
        }
        finally { Cleanup(tenant); }
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
    // 축 ⒤ 🔴🔴 **테넌트 폴더가 링크면** — 검증팀이 실제로 뚫은 길 (재검증 S-1)
    // ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "축⒤ 🔴🔴 **테넌트 폴더가 junction 이면 지우지 않는다** — 밖의 파일이 지워졌던 자리")]
    public async Task AxisI_TenantFolderJunction_DeleteRejected()
    {
        // 🔴🔴 [2026-10-25 재검증 S-1] 1차 봉합은 울타리 뿌리를 **테넌트 폴더**로 넘겼고,
        //   링크 검사는 **뿌리 자신을 건너뛴다**(뿌리는 관리자가 정한 자리라는 뜻이었다)
        //   ⇒ 테넌트 폴더가 junction 이면 아무도 안 봤다. 검증팀이 그 길로 **울타리 밖 파일을
        //   실제로 지웠다.** `%ProgramData%` 는 일반 사용자도 쓸 수 있으니, 직원 계정이
        //   폴더를 바꿔 두면 관리자의 「그림 지우기」 한 번에 밖이 지워진다.
        var tenant = $"gate-{Guid.NewGuid():N}";
        var outside = Path.Combine(Path.GetTempPath(), $"gate-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);

        var victim = Path.Combine(outside, "밖의파일.png");
        File.WriteAllBytes(victim, RealPng);

        var tenantDir = FormBackgroundPaths.TenantRoot(tenant);
        Directory.CreateDirectory(FormBackgroundPaths.Root);

        try
        {
            MakeJunction(tenantDir, outside);

            var store = MakeStore();

            // 🔴 junction 을 지나는 경로로 지우기를 부른다 — 울타리 앞글자 비교는 통과한다.
            var through = Path.Combine(tenantDir, "밖의파일.png");
            var why = store.Delete(tenant, through);

            Assert.NotNull(why);   // 거절해야 한다
            Assert.True(File.Exists(victim), "🔴 울타리 밖 파일이 지워졌다 — S-1 이 되살아났다.");

            // 🔴 올리기도 같은 길로 막히나 — 그림이 밖에 떨어지면 안 된다.
            var before = Directory.GetFiles(outside).Length;
            var (path, rejection) = await store
                .SaveAsync(tenant, "a.png", new MemoryStream(RealPng));

            Assert.Null(path);
            Assert.NotNull(rejection);
            Assert.Equal(before, Directory.GetFiles(outside).Length);
        }
        finally
        {
            // junction 은 폴더로 지운다 — 대상(outside)은 따라 지워지지 않는다.
            try { if (Directory.Exists(tenantDir)) Directory.Delete(tenantDir); }
            catch (Exception ex) { Console.Error.WriteLine($"[gate cleanup] {ex.Message}"); }

            try { if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true); }
            catch (Exception ex) { Console.Error.WriteLine($"[gate cleanup] {ex.Message}"); }
        }
    }

    [Fact(DisplayName = "축⒤음 🔴 음성 대조군 — **링크가 아니면** 올리기·지우기가 된다(과잉 차단 아님)")]
    public async Task AxisI_Negative_NormalFolderStillWorks()
    {
        var tenant = $"gate-{Guid.NewGuid():N}";
        var store = MakeStore();
        try
        {
            var (path, why) = await store
                .SaveAsync(tenant, "a.png", new MemoryStream(RealPng));

            Assert.Null(why);
            Assert.NotNull(path);
            Assert.Null(store.Delete(tenant, path!));
        }
        finally { Cleanup(tenant); }
    }

    /// <summary>
    /// 🔴 junction 을 만든다 — <c>mklink /J</c>. 관리자 권한이 필요 없다.
    /// </summary>
    /// <remarks>
    /// 🔴 못 만들면 <b>시험을 통과시키지 않는다</b> — 「못 쟀다」를 초록으로 적지 않는다.
    ///   이 축은 검증팀이 실물로 뚫은 자리라, 못 재는 환경이면 그 사실이 빨강으로 보여야 한다.
    /// </remarks>
    private static void MakeJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(20_000);

        Assert.True(Directory.Exists(link) && new DirectoryInfo(link).LinkTarget is not null,
            "junction 을 만들지 못해 이 축을 재지 못했다 — 「못 쟀다」는 통과가 아니다.");
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
        // 🔴 인자 개수를 손으로 적지 않는다 — 10/25 에 그 자리에서 108건이 깨졌다.
        var got = ResolveRaw(template: null);
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
        var skipped = new List<string>();
        foreach (var ip in InfoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!dtoProps.TryGetValue(ip.Name, out var dp)) continue;
            if (dp.PropertyType != ip.PropertyType) continue;
            if (!dp.CanWrite || !ip.CanRead) continue;

            // 기본값과 **다른** 값을 넣는다 — 같은 값이면 배선이 빠져도 통과한다(거짓 초록).
            var probe = DifferentFrom(ip.PropertyType, ip.GetValue(blank));

            // 🔴🔴 [2026-10-25 [4] 반려 R-8 봉합] 종전에는 여기서 `continue` 로
            //   **조용히 건너뛰었다.** 모르는 형의 칸이 늘면 배선이 빠져도 통과한다
            //   — 긁기를 세운 뜻이 그 자리에서 사라진다. 이제 건너뛰지 않고 **적어 둔다.**
            if (probe is null)
            {
                skipped.Add($"{ip.Name}:{ip.PropertyType.Name}");
                continue;
            }

            dp.SetValue(dto, probe);
            scraped.Add(ip);
        }

        // 🔴 [R-8] 건너뛴 칸은 **아는 것만** 허용한다. 새 형이 들어오면 여기서 빨개지고,
        //   그때 DifferentFrom 에 그 형을 더하면 된다. 조용히 넘어가지 않는다.
        var allowedSkips = new[] { "FieldCoords:List`1" };
        Assert.True(skipped.All(s => allowedSkips.Contains(s)),
            $"긁기가 모르는 형의 양식 칸을 건너뛰었다: {string.Join(", ", skipped)} "
            + "— DifferentFrom 에 그 형을 더하고 이 목록을 갱신하라.");

        // 🔴 긁은 칸이 적으면 **긁기 자체가 깨진 것**이다 — 0건이면 이 시험은 아무것도 안 본다.
        //   바닥은 「지금 짝이 맞는 칸 수」로 두되, 건너뛴 칸을 합쳐 **전수를 덮는지** 같이 본다.
        Assert.True(scraped.Count >= 8,
            $"긁은 양식 칸이 {scraped.Count}개뿐이다 — 이름·형이 어긋나 긁기가 깨졌다.");

        var infoFieldCount = InfoType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length;
        Assert.True(scraped.Count + skipped.Count + UnpairedByDesign >= infoFieldCount,
            $"작성기 양식 칸 {infoFieldCount}개 중 {scraped.Count + skipped.Count}개만 긁혔다 "
            + "— 짝을 못 찾은 칸이 늘었다(이름이 다른 칸이 생겼나).");

        // 🔴 [재검증 S-5] 짝 없는 칸을 **이름으로** 확인한다 — 숫자를 올려 가리는 길을 막는다.
        var paired = scraped.Select(p => p.Name).Concat(skipped.Select(s => s.Split(':')[0])).ToHashSet();
        var unpaired = InfoType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).Where(n => !paired.Contains(n)).ToArray();

        Assert.True(unpaired.All(UnpairedByDesignNames.Contains),
            $"짝 없는 작성기 칸이 늘었다: {string.Join(", ", unpaired)} "
            + "— UnpairedByDesignNames 에 더하고 **왜 짝이 없는지** 거기 적어라.");

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
        // 🔴 [2026-10-25 [4] 반려 R-2 봉합] **주석을 걷어낸 코드**에서 본다.
        //   종전에는 원문을 그대로 봐서, 호출부를 되돌리고 같은 글자를 주석 한 줄로
        //   남기면 초록이었다 — 거짓 통과를 막는 축이 주석을 믿고 있었다.
        var src = ReadCode("src", "HitPan.Application", "Services", "PdfRenderService.cs");

        Assert.Contains("page.Size(ResolvePageSize(", src, StringComparison.Ordinal);
        Assert.Contains("_ = ResolveStyleKey(template);", src, StringComparison.Ordinal);

        // 🔴 걷어내기가 정말 돌고 있나 — 고장나면 위 두 줄이 다시 거짓이 된다.
        var raw = ReadSource("src", "HitPan.Application", "Services", "PdfRenderService.cs");
        Assert.Contains("내가 뭘 틀렸나", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("내가 뭘 틀렸나", src, StringComparison.Ordinal);

        // 🔴🔴 [재검증 S-2] 걷어내기가 **실코드를 손상시키지 않나** — 이게 더 나쁜 쪽이었다.
        //   종전 판은 `IndexOf("//")` 로 잘라 `url.Contains("://")` 를 반 토막 냈다.
        //   그 줄은 바깥 주소를 막는 자리다. 걷어내기가 코드를 바꿔 놓고 그 위에서 단언했다.
        Assert.Contains("\"://\"", src, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "축⒢ 🔴🔴 주석 걷어내기가 **블록 주석·문자열**을 올바로 가른다 (재검증 S-2)")]
    public void AxisG_StripCommentsIsCorrect()
    {
        // 🔴 걷어내기 자체를 시험한다 — 이것이 틀리면 축⒢ 전부가 거짓이다.
        var m = typeof(FormPaperSizeAndBackgroundUploadGateTests).GetMethod(
            "StripComments", BindingFlags.NonPublic | BindingFlags.Static)!;
        string Strip(string s) => (string)m.Invoke(null, new object?[] { s })!;

        // ⒜ 줄 주석은 지운다
        Assert.DoesNotContain("비밀", Strip("var a = 1; // 비밀"), StringComparison.Ordinal);

        // ⒝ 🔴 블록 주석도 지운다 — 1차 봉합이 못 하던 것
        Assert.DoesNotContain("숨은글", Strip("var a = 1; /* 숨은글 */ var b = 2;"), StringComparison.Ordinal);
        Assert.Contains("var b = 2;", Strip("var a = 1; /* 숨은글 */ var b = 2;"), StringComparison.Ordinal);

        // ⒞ 🔴 여러 줄 블록 주석
        Assert.DoesNotContain("숨은글", Strip("a;\n/* 한 줄\n 숨은글 */\nb;"), StringComparison.Ordinal);

        // ⒟ 🔴 문자열 안은 **건드리지 않는다** — 1차 봉합이 코드를 손상시킨 자리
        Assert.Contains("\"://\"", Strip("if (url.Contains(\"://\")) { }"), StringComparison.Ordinal);
        Assert.Contains("\"// 가짜\"", Strip("var s = \"// 가짜\";"), StringComparison.Ordinal);
        Assert.Contains("\"/* 가짜 */\"", Strip("var s = \"/* 가짜 */\";"), StringComparison.Ordinal);

        // ⒠ 🔴 음성 대조군 — 주석 안에 둔 호출부 글자는 **통과하지 못한다**
        Assert.DoesNotContain("page.Size(ResolvePageSize(",
            Strip("// page.Size(ResolvePageSize(template));\npage.Size(PageSizes.A4);"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("page.Size(ResolvePageSize(",
            Strip("/* page.Size(ResolvePageSize(template)); */\npage.Size(PageSizes.A4);"),
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "축⒢ 🔴🔴 **법정 서식은 용지를 안 바꾼다** (반려 R-5 — preprint 세금계산서가 공용 흐름으로 온다)")]
    public void AxisG_TaxInvoiceNeverChangesPaper()
    {
        // 🔴 전용 작성기로 가는 분기는 `&& !isPreprint` 다 ⇒ **양식용지 모드의 세금계산서는
        //   공용 흐름으로 온다.** 전용 작성기만 A4 로 묶어 두고 「A4 고정 그대로」라고 적은 것이
        //   절반만 참이었다. 종류로 막는지 **동작으로** 본다.
        var a4 = QuestPDF.Helpers.PageSizes.A4;

        foreach (var (size, orientation) in new[]
                 { ("A5", "portrait"), ("Letter", "landscape"), ("A4", "landscape") })
        {
            var got = ResolveSize(size, orientation, documentType: "tax_invoice");
            Assert.Equal(a4.Width, got.Width, 1);
            Assert.Equal(a4.Height, got.Height, 1);
        }

        // 🔴 음성 대조군 — 다른 종류는 **바뀌어야 한다**(전부 A4 로 묶어 버린 게 아니다).
        var other = ResolveSize("A5", "portrait", documentType: "delivery");
        Assert.NotEqual((a4.Width, a4.Height), (other.Width, other.Height));
    }

    [Theory(DisplayName = "축⒢ 🔴 **전표 종류 전건** — 어느 것이 막히고 어느 것이 안 막히나 (재검증 S-3)")]
    [InlineData("tax_invoice", true)]      // 세금계산서 — 법정
    [InlineData("invoice_exempt", true)]   // 면세계산서 — 법정 후보 · 🔴 법무 판정 전까지 막는 쪽
    [InlineData("payment_receipt", true)]  // 입금표 — 같음
    [InlineData("quotation", false)]
    [InlineData("sales_order", false)]
    [InlineData("delivery", false)]
    [InlineData("purchase_order", false)]
    [InlineData("purchase_receipt", false)]
    [InlineData("purchase_return", false)]
    [InlineData("sales_return", false)]
    public void AxisG_EveryDocTypeIsDecided(string documentType, bool shouldPin)
    {
        // 🔴 종류 이름은 법적 판단이라 긁을 수 없다. 그래서 **전건을 표로 굳힌다** —
        //   새 종류가 생기면 이 표에 없어서 누군가 결정을 내려야 한다.
        var a4 = QuestPDF.Helpers.PageSizes.A4;
        var got = ResolveSize("A5", "landscape", documentType: documentType);
        var pinned = Math.Abs(got.Width - a4.Width) < 1.5 && Math.Abs(got.Height - a4.Height) < 1.5;

        Assert.Equal(shouldPin, pinned);
    }

    [Fact(DisplayName = "축⒢ 🔴 화면 안내와 서버 판정이 **같은 종류**를 본다 (재검증 S-4 — 거짓 안내 방지)")]
    public void AxisG_EditorNoticeMatchesServer()
    {
        // 🔴 화면이 「법으로 정해져 있어 A4 로 나갑니다」라고 적는 종류와 서버가 실제로 묶는 종류가
        //   어긋나면 **고객이 거짓 안내를 받는다.** 양쪽 목록을 대조한다.
        var serverField = typeof(PdfRenderService).GetField("LegalFormDocTypes",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(serverField);
        var server = (HashSet<string>)serverField!.GetValue(null)!;

        var razor = ReadCode("src", "HitPan.Web", "Components", "Common", "FormTemplateEditorDialog.razor");

        // 🔴 서버가 묶는 종류가 화면 목록에도 적혀 있나 (form_type ↔ documentType 이 같은 글자인 종들)
        foreach (var t in server)
        {
            Assert.Contains($"\"{t}\"", razor, StringComparison.Ordinal);
        }

        // 🔴 안내 글이 실재하나 — 조건만 있고 말이 없으면 고객은 아무것도 못 본다
        Assert.Contains("PaperChoiceIgnored", razor, StringComparison.Ordinal);
        Assert.Contains("법으로 모양이 정해져", razor, StringComparison.Ordinal);
        Assert.Contains("위치를 맞춰 두셨기 때문에", razor, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "축⒢ 🔴🔴 **좌표 맞춘 양식용지는 용지를 안 바꾼다** (반려 R-4 — 조용한 빈 종이)")]
    public void AxisG_CoordFormNeverChangesPaper()
    {
        // 🔴 좌표는 종이 왼쪽 위가 원점이고 여백 0 이다. A5 로 줄이면 종이 밖으로 나간 값이
        //   **그냥 안 그려진다** — 오류도 경고도 없다. 고객은 빈 종이를 받고 이유를 모른다.
        var a4 = QuestPDF.Helpers.PageSizes.A4;

        var got = ResolveSize("A5", "landscape", useCoords: true);
        Assert.Equal(a4.Width, got.Width, 1);
        Assert.Equal(a4.Height, got.Height, 1);

        // 🔴 음성 대조군 — 좌표가 없으면 바뀌어야 한다(막아 둔 게 아니라 가린 것이다).
        var free = ResolveSize("A5", "landscape", useCoords: false);
        Assert.NotEqual((a4.Width, a4.Height), (free.Width, free.Height));
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

    // ──────────────────────────────────────────────────────────────
    // 축 ⒣ 🔴 **종이 치수가 PDF 에 실제로 찍히나** — 사장님이 프린터로 보실 바로 그 축
    // ──────────────────────────────────────────────────────────────

    [Theory(DisplayName = "축⒣ 🟡 양식값 → PDF 종이 치수 **매핑**을 PDF 바이트로 잰다 (🔴 전표 작성기는 안 탄다)")]
    [InlineData("A4", "portrait", 595, 842)]
    [InlineData("A4", "landscape", 842, 595)]
    [InlineData("A5", "portrait", 420, 595)]
    [InlineData("A5", "landscape", 595, 420)]
    [InlineData("Letter", "portrait", 612, 792)]
    [InlineData("Letter", "landscape", 792, 612)]
    [InlineData("A3", "portrait", 595, 842)]   // 🔴 모르는 값 → A4 세로로 떨어진다
    public void AxisH_RealPdfBytesCarryPaperSize(string size, string orientation, int wantW, int wantH)
    {
        // 🔴 왜 이 축이 또 필요한가 — 위 축⒟⒠ 는 `PageSize` **객체**까지만 쟀다.
        //   그 객체가 **PDF 안 `/MediaBox` 숫자로 옮겨지는지**는 다른 얘기고, 사장님이
        //   프린터로 보실 숫자가 그것이다. 그걸 직접 읽는다.
        //
        // 🔴🔴 [2026-10-25 [4] 반려 R-3 — 이 축의 한계를 이름에 적었다]
        //   이 시험은 **자기 문서를 만들어** 잰다. 실제 전표 작성기(`RenderDocumentAsync`)를
        //   타지 않으므로 **호출부를 되돌려도 이 축은 초록**이다. 호출부 방어는 축⒢ 가 한다.
        //   ⇒ 이 축이 지키는 것은 「양식값 → 종이 치수 매핑」이고, 「전표가 그 종이로 나온다」는
        //     **DB 가 있어야 재는 미측정 축**이다. 그렇게 적어 두고 넘어가지 않는다.
        //   🔴 종이 자체는 영영 못 잰다 — 프린터가 그 치수를 따르는지는 실물이다.
        var pageSize = ResolveSize(size, orientation);

        var bytes = QuestPDF.Fluent.Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(pageSize);
                page.Content().Text("게이트");
            });
        }).GeneratePdf();

        var (w, h) = ReadFirstMediaBox(bytes);

        // 1pt 안에서 맞으면 같은 종이다(소수점 반올림).
        Assert.True(Math.Abs(w - wantW) <= 1.5,
            $"{size}·{orientation} 의 종이 폭이 {w}pt 다 — {wantW}pt 를 기대했다.");
        Assert.True(Math.Abs(h - wantH) <= 1.5,
            $"{size}·{orientation} 의 종이 높이가 {h}pt 다 — {wantH}pt 를 기대했다.");
    }

    [Fact(DisplayName = "축⒣음 🔴 음성 대조군 — 치수를 **진짜로 읽고 있다**(늘 A4 라고 답하는 게 아니다)")]
    public void AxisH_Negative_ReaderActuallyReads()
    {
        var a4 = QuestPDF.Fluent.Document.Create(d => d.Page(p =>
        {
            p.Size(QuestPDF.Helpers.PageSizes.A4);
            p.Content().Text("x");
        })).GeneratePdf();

        var a5 = QuestPDF.Fluent.Document.Create(d => d.Page(p =>
        {
            p.Size(QuestPDF.Helpers.PageSizes.A5);
            p.Content().Text("x");
        })).GeneratePdf();

        // 🔴 이 둘이 같게 읽히면 읽기 함수가 고장난 것이고, 위 축⒣ 전부가 거짓 통과다.
        Assert.NotEqual(ReadFirstMediaBox(a4), ReadFirstMediaBox(a5));
    }

    /// <summary>PDF 첫 쪽의 종이 치수(pt)를 읽는다 — <c>/MediaBox [0 0 W H]</c>.</summary>
    private static (double W, double H) ReadFirstMediaBox(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var m = System.Text.RegularExpressions.Regex.Match(
            text, @"/MediaBox\s*\[\s*([\d.+-]+)\s+([\d.+-]+)\s+([\d.+-]+)\s+([\d.+-]+)\s*\]");

        Assert.True(m.Success, "PDF 안에서 종이 치수를 못 찾았다 — 읽기 자체가 깨졌다.");

        var x1 = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var y1 = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var x2 = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        var y2 = double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);

        return (Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    /// <summary>
    /// 🔴 소스를 읽되 <b>주석을 걷어낸다</b> — [2026-10-25 [4] 반려 R-2 봉합].
    /// </summary>
    /// <remarks>
    /// 🔴 <b>무엇이 거짓이었나</b> — 축⒢ 가 `File.ReadAllText` + `Assert.Contains` 였다.
    ///   그래서 누가 호출부를 `page.Size(PageSizes.A4)` 로 되돌리고
    ///   `// page.Size(ResolvePageSize(template))` 를 <b>주석 한 줄로 남기면 초록</b>이다.
    ///   10/24 G-1(거짓 통과) 재발을 막는 유일한 방어선이 이 축인데, 그 축이 주석을 믿었다.
    /// 🔴 그래서 `//` 줄과 줄 끝 주석을 지운 뒤 본다. **글자가 아니라 코드**를 보는 쪽으로 한 걸음.
    /// 🔴 여전히 못 보는 것: 문자열 리터럴 안의 같은 글자. 그건 이 축의 한계로 적어 둔다.
    /// </remarks>
    private static string ReadCode(params string[] parts)
        => StripComments(ReadSource(parts));

    /// <summary>
    /// 🔴 주석을 걷어낸다 — <b>문자열 안은 건드리지 않고</b>, 블록 주석도 지운다.
    /// </summary>
    /// <remarks>
    /// 🔴🔴 [2026-10-25 재검증 S-2 봉합] 1차 봉합은 두 가지로 틀렸다.
    ///   ⒜ <c>//</c>·<c>///</c>·<c>@*</c> 로 <b>시작하는 줄</b>만 지워서
    ///      <c>/* … */</c> 블록 주석과 <b>문자열 리터럴 안의 같은 글자</b>는 그대로 통과했다.
    ///   ⒝ 🔴 더 나빴던 것 — 줄 끝 주석을 <c>IndexOf("//")</c> 로 잘라
    ///      <c>url.Contains("://")</c> 같은 <b>실코드를 손상</b>시켰다
    ///      (`PdfRenderService.cs` 의 바깥 주소 차단 줄이 그 피해자였다).
    ///      즉 걷어내기가 코드를 바꿔 놓고 그 위에서 단언하고 있었다.
    /// ⇒ 글자를 한 자씩 걸으며 <b>문자열·글자 리터럴 안인지</b> 보고, 밖일 때만 주석으로 센다.
    /// 🔴 여전히 못 하는 것: 축자 문자열(<c>@"…"</c>·<c>"""…"""</c>) 안의 줄바꿈 처리.
    ///   이 파일들에는 그 모양이 없어 지금은 참이고, 생기면 여기를 늘려야 한다(등재).
    /// </remarks>
    private static string StripComments(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length);
        var inString = false;
        var inChar = false;
        var inBlock = false;

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            var next = i + 1 < raw.Length ? raw[i + 1] : '\0';

            if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
                else if (c == '\n') sb.Append('\n');   // 줄 수를 지킨다
                continue;
            }

            if (inString || inChar)
            {
                sb.Append(c);
                if (c == '\\' && next != '\0') { sb.Append(next); i++; continue; }
                if (inString && c == '"') inString = false;
                if (inChar && c == '\'') inChar = false;
                continue;
            }

            // 문자열 밖에서만 주석으로 센다.
            if (c == '/' && next == '/')
            {
                while (i < raw.Length && raw[i] != '\n') i++;
                if (i < raw.Length) sb.Append('\n');
                continue;
            }

            if (c == '/' && next == '*') { inBlock = true; i++; continue; }

            // razor 주석 `@* … *@`
            if (c == '@' && next == '*')
            {
                i += 2;
                while (i + 1 < raw.Length && !(raw[i] == '*' && raw[i + 1] == '@'))
                {
                    if (raw[i] == '\n') sb.Append('\n');
                    i++;
                }

                i++;
                continue;
            }

            if (c == '"') inString = true;
            if (c == '\'') inChar = true;

            sb.Append(c);
        }

        return sb.ToString();
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

    private static QuestPDF.Helpers.PageSize ResolveSize(string paperSize, string orientation,
        string? documentType = null, bool useCoords = false)
    {
        var info = Activator.CreateInstance(InfoType)!;
        InfoType.GetProperty("PaperSize")!.SetValue(info, paperSize);
        InfoType.GetProperty("Orientation")!.SetValue(info, orientation);

        return ResolveRaw(info, documentType, useCoords);
    }

    /// <summary>
    /// 🔴 <c>ResolvePageSize</c> 를 부른다 — <b>인자 개수를 서명에서 긁어</b> 맞춘다.
    /// </summary>
    /// <remarks>
    /// 🔴 손으로 개수를 적으면 10/25 의 그 사고(반사 `Invoke` 가 개수를 맞추라고 터지는 자리)를
    ///   **시험이 또 겪는다.** 실제로 봉합 중에 한 번 겪었다(축⒠ 가 1개를 넘겨 터졌다).
    /// </remarks>
    private static QuestPDF.Helpers.PageSize ResolveRaw(object? template,
        string? documentType = null, bool useCoords = false)
    {
        var svc = MakeRenderService();
        var m = Private("ResolvePageSize");

        var args = m.GetParameters().Select<ParameterInfo, object?>(p => p.Name switch
        {
            "template" => template,
            "documentType" => documentType,
            "useCoords" => useCoords,
            _ => p.HasDefaultValue ? p.DefaultValue : null,
        }).ToArray();

        return (QuestPDF.Helpers.PageSize)m.Invoke(svc, args)!;
    }

    /// <summary>
    /// 🔴 DTO 에 <b>이름 짝이 없는</b> 작성기 칸 — [2026-10-25 재검증 S-5 봉합] <b>이름으로</b> 적는다.
    /// </summary>
    /// <remarks>
    /// 🔴 종전에는 숫자(<c>UnpairedByDesign = 1</c>)였다. 숫자는 <b>가리는 손잡이</b>다 —
    ///   칸이 늘어 빨개졌을 때 다음 사람이 숫자를 올리면 조용히 넘어간다.
    ///   이름이면 「무엇이 짝이 없나」를 그 자리에서 읽게 되고, 올리려면 까닭을 적어야 한다.
    /// </remarks>
    private static readonly string[] UnpairedByDesignNames = { "FieldCoords" };

    /// <summary>
    /// 🔴 위 목록의 개수 — 전수 대조에 쓴다. <b>손으로 적지 않는다</b>(목록에서 센다).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>그 한 칸</b> = <c>FormTemplateInfo.FieldCoords</c>(`List&lt;FieldCoord&gt;`) ↔
    ///   <c>FormTemplateDto.FieldCoordsJson</c>(`string`). 이름도 형도 다르다 —
    ///   DTO 는 JSON 글자로 들고 오고 작성기는 **파싱한 목록**으로 쓴다(`ParseFieldCoords`).
    ///   그래서 이 긁기로는 짝을 못 찾는다. 그 칸의 배선은 `PreprintCoordGuardTests` 가 지킨다.
    /// 🔴 새로 짝 없는 칸을 만들면 이 숫자를 올리고 <b>왜 짝이 없는지 여기 적어라.</b>
    ///   안 적고 올리면 다음 사람이 「원래 그런가」로 읽는다.
    /// </remarks>
    private static int UnpairedByDesign => UnpairedByDesignNames.Length;

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
