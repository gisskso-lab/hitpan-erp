using System.Reflection;
using HitPan.Application.Services;
using HitPan.Contracts.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴🔴 회사 그림 보관소 게이트 — 20261026작1 설계 §7 <b>G-W10</b> · 지시서 §E-2 <b>G-W11</b>.
/// </summary>
/// <remarks>
/// 🔴🔴 <b>이 묶음이 왜 생겼나</b> — 2026-10-26 [4] 검증팀 반려 R-3:
///   새로 선 <c>CompanyImageStore</c> 는 <b>브라우저에서 올라온 파일을 디스크에 쓰고 지우는</b>
///   240줄인데 <c>grep -rn CompanyImageStore src/HitPan.Tests</c> 가 <b>0건</b>이었다.
///   10/24~25 반려 7건이 전부 이 모양에서 나왔다.
///   🔴 <b>읽어서 괜찮아 보이는 것과 재 본 것은 다르다.</b> 한 겹이라도 복붙하다 어긋났는지는
///   아무도 모르는 상태였다 — 그 상태를 「승인된 중복」이라 부를 수 없다.
///
/// 🔴 <b>대조군을 반드시 같이 둔다</b>(누적 31번+). 거절 축마다 「정상 입력은 통과한다」를 함께 잰다 —
///   그렇지 않으면 <b>전부 거절하는 고장</b>도 초록으로 보인다.
///
/// 🔴 이 시험은 <b>실제 디스크</b>에 쓴다(<c>%ProgramData%\HitPan\company-images</c>).
///   양식지 보관소 게이트(<c>FormPaperSizeAndBackgroundUploadGateTests</c>)가 쓰는 방식과 같고,
///   테넌트 이름을 매번 새로 만들어 끝에 지운다.
/// </remarks>
public sealed class CompanyImageStoreGateTests
{
    static CompanyImageStoreGateTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    /// <summary>1x1 PNG — 작성기가 실제로 받아들이는 바이트.</summary>
    private static readonly byte[] RealPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static CompanyImageStore MakeStore() => new(NullLogger<CompanyImageStore>.Instance);

    private static void Cleanup(string tenant)
    {
        try
        {
            var dir = CompanyImagePaths.TenantRoot(tenant);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // 🔴 못 지워도 시험 결과는 바뀌지 않는다 — 테넌트 이름이 매번 새 값이다.
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 축⒜ 올리기는 **그 테넌트 폴더 안**에만 쓴다
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W10⒜ 🔴 올린 로고는 **그 테넌트 폴더 안**에 생긴다(양성 대조군)")]
    public async Task A_SavesInsideTenantFolder()
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        try
        {
            var (path, why) = await MakeStore()
                .SaveAsync(tenant, "logo", "우리회사로고.png", new MemoryStream(RealPng));

            Assert.Null(why);
            Assert.NotNull(path);
            Assert.True(File.Exists(path!), "파일이 실제로 생기지 않았다 — 「등록되었습니다」가 거짓이 된다.");

            var tenantRoot = Path.GetFullPath(CompanyImagePaths.TenantRoot(tenant));
            Assert.StartsWith(tenantRoot, Path.GetFullPath(path!), StringComparison.OrdinalIgnoreCase);

            // 🔴 작성기가 읽는 뿌리 안이어야 한다 — 아니면 올려도 종이에 안 나온다(R-6 의 그 길).
            Assert.StartsWith(Path.GetFullPath(CompanyImagePaths.Root),
                Path.GetFullPath(path!), StringComparison.OrdinalIgnoreCase);

            // 🔴 **고객 파일 이름을 쓰지 않는다** — 덮어쓰기·예약어·거슬러 오르기를 한 번에 막는다.
            var name = Path.GetFileName(path!);
            Assert.StartsWith("logo_", name, StringComparison.Ordinal);
            Assert.DoesNotContain("우리회사로고", name, StringComparison.Ordinal);
        }
        finally { Cleanup(tenant); }
    }

    [Theory(DisplayName = "G-W10⒜음 🔴 음성 대조군 — 테넌트가 비거나 경로를 벗어나는 모양이면 **아무것도 안 쓴다**(#2)")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("../other")]
    [InlineData(@"..\other")]
    public async Task A_Negative_BadTenantWritesNothing(string tenant)
    {
        var (path, why) = await MakeStore()
            .SaveAsync(tenant, "logo", "a.png", new MemoryStream(RealPng));

        Assert.Null(path);
        Assert.NotNull(why);
    }

    // ══════════════════════════════════════════════════════════════════
    // 축⒝ 종류·형식·크기·깨진 바이트를 **각각** 거절한다
    // ══════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "G-W10⒝ 🔴 모르는 종류는 거절한다 — 바깥 글자를 파일 이름에 쓰지 않는다")]
    [InlineData("logoo")]
    [InlineData("LOGO")]          // 🔴 대소문자까지 못으로 — 파일 앞머리에 그대로 들어가는 값이다
    [InlineData("")]
    [InlineData("../seal")]
    public async Task B_RejectsUnknownKind(string kind)
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        try
        {
            var (path, why) = await MakeStore()
                .SaveAsync(tenant, kind, "a.png", new MemoryStream(RealPng));

            Assert.Null(path);
            Assert.NotNull(why);
            Assert.False(Directory.Exists(CompanyImagePaths.TenantRoot(tenant)),
                "거절했는데 폴더를 만들었다 — 검사 전에 디스크를 건드린 것이다.");
        }
        finally { Cleanup(tenant); }
    }

    [Theory(DisplayName = "G-W10⒝ 🔴 그림이 아닌 형식은 보관소에 **들어가지 않는다**")]
    [InlineData("payload.pdf")]
    [InlineData("payload.svg")]
    [InlineData("payload.exe")]
    [InlineData("payload")]
    public async Task B_RejectsWrongExtension(string name)
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        try
        {
            var (path, why) = await MakeStore()
                .SaveAsync(tenant, "logo", name, new MemoryStream(RealPng));

            Assert.Null(path);
            Assert.NotNull(why);
            Assert.False(Directory.Exists(CompanyImagePaths.TenantRoot(tenant)),
                "거절했는데 폴더를 만들었다.");
        }
        finally { Cleanup(tenant); }
    }

    [Theory(DisplayName = "G-W10⒝ 🔴 받아 주는 네 형식은 **대소문자를 가리지 않고** 통과한다(양성 대조군)")]
    [InlineData("a.png")]
    [InlineData("a.PNG")]
    [InlineData("a.jpg")]
    [InlineData("a.jpeg")]
    [InlineData("a.bmp")]
    [InlineData("a.webp")]
    public async Task B_AcceptsAllowedExtensions(string name)
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        try
        {
            // 🔴 바이트는 PNG 다 — 이 축이 재는 것은 **확장자 목록**이다(속은 작성기가 본다).
            var (path, why) = await MakeStore()
                .SaveAsync(tenant, "logo", name, new MemoryStream(RealPng));

            Assert.Null(why);
            Assert.NotNull(path);
            Assert.Equal(Path.GetExtension(name).ToLowerInvariant(),
                Path.GetExtension(path!));
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "G-W10⒝ 🔴 빈 파일은 거절한다")]
    public async Task B_RejectsEmpty()
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        try
        {
            var (path, why) = await MakeStore()
                .SaveAsync(tenant, "logo", "a.png", new MemoryStream(Array.Empty<byte>()));
            Assert.Null(path);
            Assert.NotNull(why);
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "G-W10⒝ 🔴 2MB 를 넘는 그림은 거절한다 — **상한이 한 자리**다")]
    public async Task B_RejectsOversize()
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        try
        {
            var big = new byte[CompanyImageLimits.MaxBytes + 1];
            var (path, why) = await MakeStore()
                .SaveAsync(tenant, "logo", "a.png", new MemoryStream(big));

            Assert.Null(path);
            Assert.NotNull(why);

            // 🔴 보관소·화면·작성기가 **같은 값**을 읽나 — 어긋나면 올라간 그림이 안 읽힌다.
            Assert.Equal(CompanyImageLimits.MaxBytes, CompanyImagePaths.MaxBytes);

            // 🔴 양식지(20MB)보다 작아야 한다 — 로고는 워터마크 재료라 base64 로 1.33배 늘고
            //   그 일이 모든 양식·모든 장에서 일어난다(그래서 두 상한이 다르다).
            Assert.True(CompanyImageLimits.MaxBytes < FormBackgroundLimits.MaxBytes,
                "로고 상한이 양식지 상한보다 작아야 한다 — 까닭은 CompanyImageLimits 머리글에 있다.");
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "G-W10⒝ 🔴 이름만 그림이고 속이 쓰레기면 **보관소에 넣지 않는다**(사전 디코드)")]
    public async Task B_RejectsCorruptBytes()
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        try
        {
            var junk = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 이건 그림이 아니다");
            var (path, why) = await MakeStore()
                .SaveAsync(tenant, "logo", "a.png", new MemoryStream(junk));

            Assert.Null(path);
            Assert.NotNull(why);

            // 🔴 이 축이 핵심이다 — 못 읽을 파일이 보관소에 쌓이면 고객은 「올라갔는데 안 나온다」를
            //   겪고, 우리는 원인을 인쇄 쪽에서 찾는다(10/24 P0 의 모양).
            var dir = CompanyImagePaths.TenantRoot(tenant);
            Assert.True(!Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0,
                "못 읽는 바이트가 보관소에 들어갔다.");
        }
        finally { Cleanup(tenant); }
    }

    // ══════════════════════════════════════════════════════════════════
    // 축⒞ 지우기 — 남의 것·울타리 밖은 **못 지운다**
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W10⒞ 🔴 올린 사람은 자기 그림을 지울 수 있다(양성 대조군)")]
    public async Task C_DeletesOwnImage()
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        try
        {
            var (path, _) = await MakeStore()
                .SaveAsync(tenant, "logo", "a.png", new MemoryStream(RealPng));
            Assert.NotNull(path);

            var why = MakeStore().Delete(tenant, path!);

            Assert.Null(why);
            Assert.False(File.Exists(path!), "지웠다고 했는데 파일이 남아 있다 — 화면의 「지웠습니다」가 거짓이 된다.");
        }
        finally { Cleanup(tenant); }
    }

    [Fact(DisplayName = "G-W10⒞ 🔴🔴 **남의 테넌트 그림은 못 지운다** — 교차 삭제 실측")]
    public async Task C_CannotDeleteOtherTenantsImage()
    {
        var mine = $"cigate-{Guid.NewGuid():N}";
        var yours = $"cigate-{Guid.NewGuid():N}";
        try
        {
            var (victim, _) = await MakeStore()
                .SaveAsync(yours, "logo", "a.png", new MemoryStream(RealPng));
            Assert.NotNull(victim);

            // 🔴 내 테넌트로 로그인한 채 **남의 경로**를 넘긴다 — 한 PC 에 사업장이 둘일 수 있다.
            var why = MakeStore().Delete(mine, victim!);

            Assert.NotNull(why);
            Assert.True(File.Exists(victim!), "🔴 남의 로고가 지워졌다 — 테넌트 격리가 깨졌다.");
        }
        finally { Cleanup(mine); Cleanup(yours); }
    }

    [Fact(DisplayName = "G-W10⒞ 🔴 울타리 **밖** 경로는 못 지운다")]
    public void C_CannotDeleteOutsideFence()
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        var outside = Path.Combine(Path.GetTempPath(), $"hp-victim-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(outside, RealPng);
        try
        {
            var why = MakeStore().Delete(tenant, outside);

            Assert.NotNull(why);
            Assert.True(File.Exists(outside), "🔴 울타리 밖 파일이 지워졌다.");
        }
        finally
        {
            try { File.Delete(outside); } catch (IOException) { /* 남아도 해롭지 않다 */ }
            Cleanup(tenant);
        }
    }

    [Theory(DisplayName = "G-W10⒞ 🔴 지우기도 테넌트 모양을 본다 — 올리기에만 두면 비대칭이 된다")]
    [InlineData("")]
    [InlineData("..")]
    public void C_RejectsBadTenant(string tenant)
    {
        var why = MakeStore().Delete(tenant, Path.Combine(CompanyImagePaths.Root, "x.png"));
        Assert.NotNull(why);
    }

    // ══════════════════════════════════════════════════════════════════
    // 축⒟ 링크 울타리 — 못 재면 **막는 쪽**으로 떨어진다 ([3-V] P2-7)
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W10⒟ 🔴 링크 검사가 **못 잴 때 막는 쪽**으로 떨어진다(대조군: 보통 경로는 통과)")]
    public void D_LinkCheckFailsClosed()
    {
        // 🔴🔴 [3-V] P2-7 — 이 함수는 본(FormBackgroundStore)에 있는 `try/catch` 를 잃고
        //   `static` 으로 베껴져 있었다. 그 상태에서는 ① 거절 까닭이 뭉개지고
        //   ② 뒤에 누가 바깥 `try` 없이 부르면 울타리가 그날 열린다.
        //   ⇒ **못 재는 경로**를 넣어 「막는 쪽으로 떨어지나」를 잰다.
        var store = MakeStore();
        var m = typeof(CompanyImageStore).GetMethod("PassesThroughLink",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        Assert.False(m!.IsStatic, "울타리 검사가 static 으로 되돌아갔다 — 로거를 잃고 까닭이 뭉개진다.");

        var root = Path.GetFullPath(CompanyImagePaths.Root).TrimEnd(Path.DirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;

        // 🔴 못 재는 경로 — 파일 이름에 쓸 수 없는 글자가 들어 있다.
        var unmeasurable = root + "a\0b.png";
        var blocked = (bool)m.Invoke(store, new object?[] { unmeasurable, root })!;
        Assert.True(blocked, "🔴 못 재는 경로가 통과했다 — 울타리가 열리는 쪽으로 떨어졌다.");

        // 🔴 대조군 — 보통 경로는 막지 않는다(전부 막는 고장도 초록으로 보이면 안 된다).
        var normal = Path.Combine(root, "logo_x.png");
        var pass = (bool)m.Invoke(store, new object?[] { normal, root })!;
        Assert.False(pass, "보통 경로를 막았다 — 그러면 고객은 아무 그림도 못 올린다.");
    }

    [Fact(DisplayName = "G-W10⒟ 🔴🔴 테넌트 폴더가 **링크면 쓰지도 지우지도 않는다** — 실제 링크를 만들어 잰다")]
    public async Task D_LinkedTenantFolderIsRefused()
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        var target = Path.Combine(Path.GetTempPath(), $"hp-link-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(target);
        var tenantDir = CompanyImagePaths.TenantRoot(tenant);
        Directory.CreateDirectory(CompanyImagePaths.Root);

        var made = TryMakeDirectoryLink(tenantDir, target);
        try
        {
            // 🔴 만들 수 없으면 **초록으로 치지 않는다** — 미측정을 통과로 적는 것이 10/20 의 사고였다.
            Assert.True(made,
                "이 환경에서 디렉터리 링크를 만들 수 없어 이 축을 재지 못했다 — 미측정을 초록으로 치지 않는다.");

            var (path, why) = await MakeStore()
                .SaveAsync(tenant, "logo", "a.png", new MemoryStream(RealPng));

            Assert.Null(path);
            Assert.NotNull(why);
            Assert.Empty(Directory.GetFiles(target));   // 🔴 링크 건너편에 파일이 떨어지지 않았다
        }
        finally
        {
            try { if (Directory.Exists(tenantDir)) Directory.Delete(tenantDir); }
            catch (IOException) { /* 링크만 지운다 — 실패해도 이름이 매번 새 값이다 */ }
            try { Directory.Delete(target, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 디렉터리 링크를 만든다 — 🔴 symlink 가 막히면 <b>junction</b> 으로 다시 시도한다.
    /// </summary>
    /// <remarks>
    /// 🔴 Windows 에서 symlink 는 관리자·개발자 모드가 필요하지만 <b>junction 은 아니다.</b>
    ///   리눅스에서는 symlink 가 그냥 된다. ⇒ 두 길을 다 시도하고, 둘 다 안 되면
    ///   <b>못 쟀다고 말한다</b>(조용한 통과 금지).
    /// </remarks>
    private static bool TryMakeDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return Directory.Exists(link);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows()) return false;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
                $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(10_000);
            return new DirectoryInfo(link).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or System.ComponentModel.InvalidEnumArgumentException)
        {
            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // G-W11 · 🔴 **두 보관소가 각각 산다** — 복붙이 아니라 둘 다 도는지
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W11 🔴 같은 입력을 두 보관소에 넣는다 — **둘 다** 막고, 다른 것은 상한뿐이다")]
    public async Task W11_BothStoresFenceTheSameInput()
    {
        var tenant = $"cigate-{Guid.NewGuid():N}";
        var formStore = new FormBackgroundStore(NullLogger<FormBackgroundStore>.Instance);
        try
        {
            // ① 같은 거절 — 그림이 아닌 형식
            var (p1, w1) = await MakeStore().SaveAsync(tenant, "logo", "x.exe", new MemoryStream(RealPng));
            var (p2, w2) = await formStore.SaveAsync(tenant, "x.exe", new MemoryStream(RealPng));
            Assert.Null(p1); Assert.NotNull(w1);
            Assert.Null(p2); Assert.NotNull(w2);

            // ② 같은 거절 — 속이 그림이 아니다
            var junk = System.Text.Encoding.UTF8.GetBytes("not an image");
            var (p3, w3) = await MakeStore().SaveAsync(tenant, "logo", "x.png", new MemoryStream(junk));
            var (p4, w4) = await formStore.SaveAsync(tenant, "x.png", new MemoryStream(junk));
            Assert.Null(p3); Assert.NotNull(w3);
            Assert.Null(p4); Assert.NotNull(w4);

            // ③ 🔴 **다른 것은 상한뿐이다** — 2MB 를 조금 넘는 그림은
            //    회사 그림은 거절하고 양식지는 받는다(까닭은 CompanyImageLimits 머리글).
            //    이 축이 두 보관소를 **별건으로 승인한 전제**를 지킨다.
            Assert.True(CompanyImageLimits.MaxBytes < FormBackgroundLimits.MaxBytes);

            // ④ 두 보관소의 **자리가 다르다** — 「양식 정리」가 로고를 지우는 날이 오지 않게.
            Assert.NotEqual(
                Path.GetFullPath(CompanyImagePaths.Root).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(FormBackgroundPaths.Root).TrimEnd(Path.DirectorySeparatorChar));
        }
        finally
        {
            Cleanup(tenant);
            try
            {
                var d = FormBackgroundPaths.TenantRoot(tenant);
                if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
            }
            catch (IOException) { }
        }
    }
}
