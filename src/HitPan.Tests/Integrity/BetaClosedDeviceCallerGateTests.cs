using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HitPan.API.Controllers;
using HitPan.Application.Interfaces;
using HitPan.Application.Services; // TenantDeviceService(Infrastructure 어셈블리 · 이름공간은 Application.Services)
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-S-03 고침 1 (2026-11-28 · [3-V] P2-1 · PM 결재) — 옛 휴대폰 QR 서비스 두 함수를 <b>부르는 곳</b>을 잰다. DB 불필요.
/// </summary>
/// <remarks>
/// <para>
/// 왜: 두 함수(<see cref="ITenantDeviceService.IssueMobileRegisterTokenAsync"/> · <see cref="ITenantDeviceService.RegisterMobileByTokenAsync"/>)는
/// 보존(#1 · #37)이라 공개된 채 남는다. 지금 부르는 곳 = 닫힌 두 액션(<c>DeviceController.IssueMobileToken</c> · <c>RegisterMobile</c>, 둘 다 <c>[NonAction]</c>)뿐이다.
/// 다른 컨트롤러에 새 주소로 그 함수를 부르는 액션을 더하면 G-S3-1(경로 글자 대조)은 못 잡는다 — 그 길을 이 시험이 막는다.
/// </para>
/// <para>
/// 방법(글자 grep 아님 · 동작에 가깝게): 컴파일된 HitPan.API · HitPan.Application · HitPan.Infrastructure 어셈블리의 <b>모든 메서드 본문 IL</b> 을
/// 명령 하나씩 읽어 <c>call</c> · <c>callvirt</c> · <c>newobj</c> · <c>ldftn</c> · <c>ldvirtftn</c>(메서드 묶음 · 대리자) 의 대상을 반사로 풀고,
/// 두 함수(인터페이스 메서드 + 구현 클래스 메서드)를 부르는 메서드를 모은다. 컴파일러가 만든 형(비동기 상태 기계 · 람다 · 지역 함수)은
/// 그것을 품은 원래 메서드로 되돌려 센다. 주석 · 문자열은 IL 에 호출로 남지 않으므로 저절로 빠진다.
/// 덧붙여 두 함수 이름 글자(<c>ldstr</c> — 반사 이름 · <c>nameof</c>)도 모은다(반사로 이름을 불러 부르는 길).
/// </para>
/// <para>
/// 한계(개발명세서 「고침 1」): ① 웹(HitPan.Web)은 시험이 참조하지 않는 WebAssembly 어셈블리라 IL 을 읽지 않는다 — 대신 웹 프로젝트가
/// Application · Infrastructure · API 를 참조하지 않음(= 그 함수를 컴파일 단계에서 부를 수 없음)을 csproj 로 확인한다.
/// ② 이름을 런타임에 이어 붙여 만드는 반사 · <c>dynamic</c> 은 못 잡는다. ③ 다른 서비스 함수를 거쳐 간접으로 부르는 길은
/// 그 거치는 함수가 「부르는 곳」 으로 잡혀 빨강이 된다(닫힌 쪽).
/// </para>
/// </remarks>
public sealed class BetaClosedDeviceCallerGateTests
{
    private static readonly string[] TargetNames =
    {
        nameof(ITenantDeviceService.IssueMobileRegisterTokenAsync),
        nameof(ITenantDeviceService.RegisterMobileByTokenAsync),
    };

    /// <summary>부르는 곳으로 허용되는 자리 = 닫힌 두 액션(둘 다 [NonAction]).</summary>
    private static readonly string[] AllowedCallers =
    {
        $"{typeof(DeviceController).FullName}::{nameof(DeviceController.IssueMobileToken)}",
        $"{typeof(DeviceController).FullName}::{nameof(DeviceController.RegisterMobile)}",
    };

    private static Assembly[] ProductAssemblies() => new[]
    {
        typeof(DeviceController).Assembly,      // HitPan.API
        typeof(ITenantDeviceService).Assembly,  // HitPan.Application
        typeof(TenantDeviceService).Assembly,   // HitPan.Infrastructure
    };

    // ─────────────────────────────────────────────────────────────
    //  IL 읽기
    // ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<short, OpCode> OpByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => f.GetValue(null))
        .OfType<OpCode>()
        .ToDictionary(o => o.Value);

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>IL 한 바퀴의 결과 — 부른 메서드 · 이름 글자 · 풀지 못한 토큰.</summary>
    internal sealed record IlScan(List<MethodBase> Calls, List<string> Strings, List<string> Unresolved);

    internal static IlScan ScanIl(MethodBase m)
    {
        var scan = new IlScan(new List<MethodBase>(), new List<string>(), new List<string>());
        var il = m.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return scan;
        }

        var typeArgs = m.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        var methodArgs = m is MethodInfo { IsGenericMethod: true } gm ? gm.GetGenericArguments() : null;
        var i = 0;
        while (i < il.Length)
        {
            int b = il[i++];
            short value;
            if (b == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[i++]));
            }
            else
            {
                value = (short)b;
            }

            if (!OpByValue.TryGetValue(value, out var op))
            {
                scan.Unresolved.Add($"{Describe(m)} 모르는 명령 0x{value & 0xFFFF:X4} @ {i}");
                return scan; // 이 본문은 더 못 읽는다 — 아래 단언이 「풀지 못함 0」 으로 빨강을 낸다(닫힌 쪽)
            }

            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    i += 8;
                    break;
                case OperandType.InlineSwitch:
                    var n = BitConverter.ToInt32(il, i);
                    i += 4 + (4 * n);
                    break;
                case OperandType.InlineMethod:
                    var mt = BitConverter.ToInt32(il, i);
                    i += 4;
                    try
                    {
                        var called = m.Module.ResolveMethod(mt, typeArgs, methodArgs);
                        if (called is not null)
                        {
                            scan.Calls.Add(called);
                        }
                    }
                    catch (ArgumentException ex)
                    {
                        scan.Unresolved.Add($"{Describe(m)} 메서드 토큰 0x{mt:X8} ({ex.GetType().Name})");
                    }

                    break;
                case OperandType.InlineString:
                    var st = BitConverter.ToInt32(il, i);
                    i += 4;
                    try
                    {
                        scan.Strings.Add(m.Module.ResolveString(st));
                    }
                    catch (ArgumentException ex)
                    {
                        scan.Unresolved.Add($"{Describe(m)} 글자 토큰 0x{st:X8} ({ex.GetType().Name})");
                    }

                    break;
                default:
                    // InlineBrTarget · InlineI · InlineField · InlineSig · InlineTok · InlineType · ShortInlineR — 4바이트
                    i += 4;
                    break;
            }
        }

        return scan;
    }

    private static string Describe(MethodBase m) => $"{m.DeclaringType?.FullName}::{m.Name}";

    private static bool IsGenerated(Type t) => t.Name.Contains('<') || t.IsDefined(typeof(CompilerGeneratedAttribute), false);

    /// <summary>
    /// 컴파일러가 만든 자리(상태 기계 MoveNext · 람다 · 지역 함수)를 그것을 품은 원래 메서드 이름으로 되돌린다.
    /// 되돌리지 못하면 날 이름 그대로 — 허용 목록에 없으니 빨강(닫힌 쪽).
    /// </summary>
    internal static string OwnerOf(MethodBase m)
    {
        var t = m.DeclaringType!;
        var gen = t;
        var outer = t;
        while (IsGenerated(outer) && outer.DeclaringType is not null)
        {
            gen = outer;
            outer = outer.DeclaringType;
        }

        if (!ReferenceEquals(outer, t))
        {
            // 상태 기계: 품은 형의 메서드 중 [AsyncStateMachine] · [IteratorStateMachine] 이 그 형을 가리키는 것
            var genDef = gen.IsGenericType ? gen.GetGenericTypeDefinition() : gen;
            foreach (var cand in outer.GetMethods(AllDeclared))
            {
                var sm = cand.GetCustomAttribute<StateMachineAttribute>(false);
                if (sm is not null && sm.StateMachineType == genDef)
                {
                    return $"{outer.FullName}::{cand.Name}";
                }
            }
        }

        // 람다 `<Outer>b__3_0` · 지역 함수 `<Outer>g__Local|3_0` · 표시 클래스 안 메서드 — 이름 꺾쇠 안
        var name = m.Name;
        if (name.StartsWith('<'))
        {
            var close = name.IndexOf('>');
            if (close > 1)
            {
                return $"{outer.FullName}::{name[1..close]}";
            }
        }

        // 표시 클래스 형 이름 `<Outer>d__5` (상태 기계 대응이 없던 경우)
        if (!ReferenceEquals(outer, t) && gen.Name.StartsWith('<'))
        {
            var close = gen.Name.IndexOf('>');
            if (close > 1)
            {
                return $"{outer.FullName}::{gen.Name[1..close]}";
            }
        }

        return $"{outer.FullName}::{name}";
    }

    private static IEnumerable<MethodBase> MethodsOf(Type t) =>
        t.GetMethods(AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(AllDeclared));

    private static Type[] LoadableTypes(Assembly a)
    {
        try
        {
            return a.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // 일부 형을 못 읽으면 그 수를 드러낸다 — 못 읽은 형이 부르는 곳일 수 있으므로 아래 단언이 빨강(닫힌 쪽)
            throw new InvalidOperationException($"{a.GetName().Name} 형 {ex.Types.Count(x => x is null)} 개를 못 읽었다", ex);
        }
    }

    /// <summary>대상 = 인터페이스의 두 함수 + 그 인터페이스를 구현한 클래스의 대응 메서드(구현 클래스를 직접 부르는 길).</summary>
    internal static HashSet<(Module, int)> TargetsIn(IEnumerable<Assembly> assemblies)
    {
        var set = new HashSet<(Module, int)>();
        var iface = typeof(ITenantDeviceService);
        foreach (var name in TargetNames)
        {
            var im = iface.GetMethod(name) ?? throw new InvalidOperationException($"인터페이스에 {name} 이 없다");
            set.Add((im.Module, im.MetadataToken));
        }

        foreach (var t in assemblies.SelectMany(LoadableTypes))
        {
            if (t.IsInterface || !iface.IsAssignableFrom(t))
            {
                continue;
            }

            var map = t.GetInterfaceMap(iface);
            for (var k = 0; k < map.InterfaceMethods.Length; k++)
            {
                if (TargetNames.Contains(map.InterfaceMethods[k].Name))
                {
                    set.Add((map.TargetMethods[k].Module, map.TargetMethods[k].MetadataToken));
                }
            }
        }

        return set;
    }

    /// <summary>훑은 결과 — 부르는 원래 메서드 · 이름 글자를 쓴 원래 메서드 · 풀지 못한 토큰 · 읽은 본문 수.</summary>
    internal sealed record CallerScan(SortedSet<string> Callers, SortedSet<string> NameUsers, List<string> Unresolved, int Bodies);

    internal static CallerScan FindCallers(IEnumerable<Type> types, HashSet<(Module, int)> targets)
    {
        var callers = new SortedSet<string>(StringComparer.Ordinal);
        var nameUsers = new SortedSet<string>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        var bodies = 0;
        foreach (var t in types)
        {
            foreach (var m in MethodsOf(t))
            {
                // 구현 클래스의 대상 함수 자신(본문)은 「부르는 곳」 이 아니다
                if (targets.Contains((m.Module, m.MetadataToken)))
                {
                    continue;
                }

                var scan = ScanIl(m);
                if (m.GetMethodBody() is not null)
                {
                    bodies++;
                }

                unresolved.AddRange(scan.Unresolved);
                var def = scan.Calls.Select(c => c is MethodInfo { IsGenericMethod: true } g ? g.GetGenericMethodDefinition() : c);
                if (def.Any(c => targets.Contains((c.Module, c.MetadataToken))))
                {
                    callers.Add(OwnerOf(m));
                }

                if (scan.Strings.Any(s => TargetNames.Contains(s, StringComparer.Ordinal)))
                {
                    nameUsers.Add(OwnerOf(m));
                }
            }
        }

        return new CallerScan(callers, nameUsers, unresolved, bodies);
    }

    private static readonly Lazy<CallerScan> Product = new(() =>
    {
        var asms = ProductAssemblies();
        return FindCallers(asms.SelectMany(LoadableTypes), TargetsIn(asms));
    });

    // ─────────────────────────────────────────────────────────────
    //  G-S3-5 — 부르는 곳 = 닫힌 두 액션뿐
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "G-S3-5-0 IL 훑기가 실제로 돈다(본문 1000개 이상 · 풀지 못한 토큰 0)")]
    public void G_S3_5_0_훑기가_돈다()
    {
        var scan = Product.Value;
        Assert.True(scan.Bodies >= 1000, $"G-S3-5 읽은 메서드 본문이 {scan.Bodies} 개뿐이다 — 훑기가 안 돈다.");
        Assert.True(scan.Unresolved.Count == 0,
            $"G-S3-5 풀지 못한 IL 토큰 {scan.Unresolved.Count} 개 — 못 읽은 자리가 부르는 곳일 수 있다: {string.Join(" | ", scan.Unresolved.Take(5))}");
    }

    [Fact(DisplayName = "G-S3-5-1 옛 휴대폰 등록 서비스 함수 2개를 부르는 곳 = 닫힌 두 액션뿐(그 밖 0 · 둘 다 찾음)")]
    public void G_S3_5_1_부르는_곳은_닫힌_두_액션뿐()
    {
        var callers = Product.Value.Callers;
        var extra = callers.Except(AllowedCallers, StringComparer.Ordinal).ToList();
        var missing = AllowedCallers.Except(callers, StringComparer.Ordinal).ToList();
        Assert.True(extra.Count == 0,
            $"G-S3-5 닫힌 옛 휴대폰 QR 등록 함수를 새로 부르는 곳이 생겼다 — 새 주소로 옛 등록이 다시 열린다(D-3 = 폐기 §325): {string.Join(", ", extra)}");
        Assert.True(missing.Count == 0,
            $"G-S3-5 닫힌 두 액션에서 부르는 것을 찾지 못했다 — 훑기가 안 돌았거나 자리가 바뀌었다(대조군): {string.Join(", ", missing)}");
    }

    [Fact(DisplayName = "G-S3-5-2 허용된 두 자리는 지금도 서버 액션이 아니다([NonAction])")]
    public void G_S3_5_2_허용된_두_자리는_NonAction()
    {
        foreach (var name in new[] { nameof(DeviceController.IssueMobileToken), nameof(DeviceController.RegisterMobile) })
        {
            var m = typeof(DeviceController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(m is not null, $"G-S3-5 DeviceController.{name} 이 없다");
            Assert.True(m!.IsDefined(typeof(NonActionAttribute), false),
                $"G-S3-5 DeviceController.{name} 에 [NonAction] 이 빠졌다 — 부르는 곳이 다시 서버 문이 된다.");
        }
    }

    [Fact(DisplayName = "G-S3-5-3 두 함수 이름 글자(반사 이름 · nameof)를 쓰는 제품 자리 0")]
    public void G_S3_5_3_이름_글자_0()
    {
        var users = Product.Value.NameUsers;
        Assert.True(users.Count == 0,
            $"G-S3-5 옛 휴대폰 등록 함수 이름 글자를 쓰는 자리가 생겼다 — 반사로 부르는 길인지 본다: {string.Join(", ", users)}");
    }

    [Fact(DisplayName = "G-S3-5-4 웹 프로젝트는 서비스 · 서버 프로젝트를 참조하지 않는다(그 함수를 부를 수 없음)")]
    public void G_S3_5_4_웹은_서비스를_참조하지_않는다()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "src", "HitPan.Web", "HitPan.Web.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "레포 뿌리를 찾지 못했다");
        var csproj = File.ReadAllText(Path.Join(dir!.FullName, "src", "HitPan.Web", "HitPan.Web.csproj"));
        foreach (var p in new[] { "HitPan.Application", "HitPan.Infrastructure", "HitPan.API" })
        {
            Assert.False(csproj.Contains($"{p}.csproj", StringComparison.OrdinalIgnoreCase),
                $"G-S3-5 HitPan.Web 이 {p} 를 참조한다 — 웹 어셈블리도 IL 훑기 대상에 넣어야 한다(이 시험 한계 ①).");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  G-S3-5-N — 음성 대조(시험 안 짝): 다른 컨트롤러가 그 함수를 부른다고 가정
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 시험 안 가짜 — 「뒤의 누군가가 다른 컨트롤러에 새 주소로 옛 등록을 다시 연」 꼴. 실행하지 않는다(IL 만 읽는다).
    /// 비동기 상태 기계 · 람다 · 메서드 묶음(대리자) · 구현 클래스 직접 호출 · 반사 이름 네 꼴을 다 담는다.
    /// </summary>
    [ApiController]
    [Route("api/gate-revived")]
    private sealed class RevivedQrDoorController : ControllerBase
    {
        private readonly ITenantDeviceService _svc;
        private readonly TenantDeviceService? _impl;

        public RevivedQrDoorController(ITenantDeviceService svc, TenantDeviceService? impl)
        {
            _svc = svc;
            _impl = impl;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(string token, CancellationToken ct)
        {
            var (ok, message, _, _) = await _svc.RegisterMobileByTokenAsync(token, "폰", "fp", "", null, null, ct);
            return ok ? Ok() : BadRequest(message);
        }

        [HttpPost("token")]
        public IActionResult Token()
        {
            Func<Task<string>> issue = () => _svc.IssueMobileRegisterTokenAsync("t", "u");
            return Ok(issue);
        }

        [HttpPost("group")]
        public IActionResult Group()
        {
            Func<string, string, CancellationToken, Task<string>> f = _svc.IssueMobileRegisterTokenAsync;
            return Ok(f);
        }

        [HttpPost("impl")]
        public async Task<IActionResult> Impl()
        {
            if (_impl is null)
            {
                return NotFound();
            }

            return Ok(await _impl.IssueMobileRegisterTokenAsync("t", "u"));
        }

        [HttpGet("by-name")]
        public IActionResult ByName() =>
            Ok(typeof(ITenantDeviceService).GetMethod(nameof(ITenantDeviceService.RegisterMobileByTokenAsync)));
    }

    [Fact(DisplayName = "G-S3-5-N 음성 대조: 다른 컨트롤러가 부르면 네 꼴(비동기 · 람다 · 대리자 · 구현 직접) 모두 잡히고 허용 밖이라 빨강")]
    public void G_S3_5_N_음성_대조()
    {
        var targets = TargetsIn(ProductAssemblies());
        var fake = typeof(RevivedQrDoorController);
        var types = new[] { fake }.Concat(fake.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        var scan = FindCallers(types, targets);

        string Of(string m) => $"{fake.FullName}::{m}";
        var expected = new[] { Of("Register"), Of("Token"), Of("Group"), Of("Impl") };
        Assert.Empty(scan.Unresolved);
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), scan.Callers);
        Assert.Equal(new[] { Of("ByName") }, scan.NameUsers);

        // G-S3-5-1 과 같은 판정식 — 허용 밖 = 빨강이어야 한다
        var extra = scan.Callers.Except(AllowedCallers, StringComparer.Ordinal).ToList();
        Assert.Equal(4, extra.Count);
    }
}
