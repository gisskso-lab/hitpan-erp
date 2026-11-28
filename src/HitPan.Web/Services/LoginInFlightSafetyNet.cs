namespace HitPan.Web.Services;

/// <summary>
/// 🔴 20261128작 로그인 경합 갈래 A 고침 1 (작업지시서 §8-2 A-① · §8-3 3V-P2 · 3V-P3-1 · PM 결재) — 안전망 판정.
/// </summary>
/// <remarks>
/// 로그인 진행 중에 다시 만들어진 로그인 화면은 앞 화면이 이동을 낼 때까지 기다리고, 15초(D2) 뒤에도 이동이 없으면 `/` 로 간다.
/// 그 판정을 이 한 곳이 한다. 칸 셋:<br/>
/// ① <b>입력 대기(멈춤)</b> — §8-2 A-①. 앞 화면이 새 기기 알림 등 대화 창을 띄워 사용자를 기다리는 동안 시계를 멈춘다.
///   ⬛ 고치기 전엔 그동안에도 시계가 흘러, 알림을 15초 넘게 두면 안전망이 `/` 로 문서를 새로 읽어 알림이 사라지고
///   [최신버젼업데이트] 를 눌러 둔 경우 `/data/update` 대신 `/` 에 닿았다(코드 읽기 · 가설). 입력이 끝나면 그때부터 다시 15초.<br/>
/// ② <b>이동 냄</b> — §8-3 3V-P2. 앞 화면이 이동(`/terms` · 갈 곳 · `/`)을 낸 순간 켠다. 켜져 있으면 안전망은 영구 해제(`/` 이동 0).
///   ⬛ 고치기 전엔 「주소가 아직 login 인가」 만 봐서 「냈지만 새 문서가 아직 안 왔다」(터널 재연결 · Windows Update 직후 · 콜드 스타트)와
///   「안 냈다」 를 못 갈랐다 ⇒ 15초 넘게 늦으면 뒤에 낸 `/` 가 앞의 `/terms` · `/data/update` 이동을 취소했다(보안상무 병렬이슈).<br/>
/// ③ 마지막 입력이 끝난 시각 — ① 의 다시 15초 기준.<br/>
/// 로그인 시작 때 <see cref="Reset"/> 으로 셋 다 비운다(앞 시도의 칸이 남아 안전망을 막지 않게 · 3V-P3-1).<br/>
/// Blazor 비의존 순수 C# — 시험 프로젝트가 소스를 링크해 <b>실제로 불러</b> 잰다(RollbackWatch.cs 와 같은 방식).
/// 시간은 부르는 쪽이 밀리초 숫자로 넘긴다(화면 = <c>Environment.TickCount64</c> · 시험 = 가짜 시계).
/// 앱 한 벌(브라우저 탭 하나)에 하나 — 로그인 화면의 정적 칸이 들고 있다(D1 · DI · Program.cs 손 0). forceLoad 다시 읽기로 저절로 비워진다.
/// </remarks>
public sealed class LoginInFlightSafetyNet
{
    /// <summary>기다리는 화면이 판정을 다시 묻는 간격(밀리초).</summary>
    public const int PollMs = 250;

    private readonly int _waitMs;
    private int _inputWaitDepth;
    private long? _lastInputEndedAtMs;
    private bool _navigated;

    public LoginInFlightSafetyNet(int waitMs)
    {
        _waitMs = waitMs;
    }

    /// <summary>안전망이 할 일.</summary>
    public enum Step
    {
        /// <summary>아직 기다린다(멈춤이거나 15초 전).</summary>
        Wait,

        /// <summary>멈춤 아님 상태로 15초가 지났고 이동도 아직 없다 ⇒ `/` 1회(부르는 쪽이 주소가 아직 login 인지 한 번 더 본다).</summary>
        GoHome,

        /// <summary>앞 화면이 이미 이동을 냈다 ⇒ 안전망은 아무것도 하지 않고 끝난다.</summary>
        StandDown,
    }

    /// <summary>로그인을 부른 화면이 지금 사용자 입력(대화 창)을 기다리는 중인가.</summary>
    public bool IsWaitingForInput => _inputWaitDepth > 0;

    /// <summary>로그인을 부른 화면이 이미 이동을 냈나.</summary>
    public bool HasNavigated => _navigated;

    /// <summary>로그인 시작 때 · 실패로 끝날 때 부른다 — 멈춤 · 이동 냄 · 마지막 입력 시각을 비운다.</summary>
    public void Reset()
    {
        _inputWaitDepth = 0;
        _lastInputEndedAtMs = null;
        _navigated = false;
    }

    /// <summary>로그인을 부른 화면이 대화 창을 띄우기 직전에 부른다. 시계가 멈춘다.</summary>
    public void BeginWaitingForInput()
    {
        _inputWaitDepth++;
    }

    /// <summary>대화 창이 닫힌 뒤(성공 · 실패 모두 — finally) 부른다. 이 시각부터 다시 15초.</summary>
    public void EndWaitingForInput(long nowMs)
    {
        if (_inputWaitDepth > 0)
        {
            _inputWaitDepth--;
        }

        _lastInputEndedAtMs = nowMs;
    }

    /// <summary>로그인을 부른 화면이 이동을 낸 순간 부른다(갈 곳 식 손 0 — 냈다는 표시만).</summary>
    public void MarkNavigated()
    {
        _navigated = true;
    }

    /// <summary>
    /// 안전망 판정 — 이동 냄이면 <see cref="Step.StandDown"/> 이 먼저다. 그다음 멈춤이면 <see cref="Step.Wait"/>.
    /// 멈춤 아님 · (기다리기 시작한 때와 마지막 입력이 끝난 때 중 늦은 쪽)부터 15초가 지났을 때만 <see cref="Step.GoHome"/>.
    /// </summary>
    public Step Next(long waitStartedAtMs, long nowMs)
    {
        if (_navigated)
        {
            return Step.StandDown;
        }

        if (IsWaitingForInput)
        {
            return Step.Wait;
        }

        var anchor = _lastInputEndedAtMs is long ended && ended > waitStartedAtMs ? ended : waitStartedAtMs;
        return nowMs - anchor >= _waitMs ? Step.GoHome : Step.Wait;
    }
}
