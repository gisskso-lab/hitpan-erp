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
/// ④ 🔴 <b>HQA 보류 고침 · 물러남</b> (작업지시서 §8-13 · 설계 로그인경합_고침2_정적칸남음 §3 · PM 결재 E1 ~ E7) — 「이동을 냈다」 ≠ 「목적 문서가 열렸다」.
///   중지 단추 · Esc(문서가 끝내 안 열림)나 뒤로 가기(복원 캐시)로 같은 런타임에 남은 로그인 화면이 진행 그림에 F5 전까지 머물렀다(②가 손만 뗐다).
///   이동 냄 뒤에도 기다리는 화면이 이 판정을 처음 부른 때부터 30초(<see cref="FallBackToFormMs"/>)가 지나면 <see cref="ShouldFallBackToForm"/> 이 참 —
///   화면은 진행 그림을 끄고 기존 로그인 입력 칸을 그린다(= 고치기 전 끝 상태 · 빈 로그인 화면). <b>이동은 하나도 새로 내지 않는다</b>(앞 이동 덮어쓰기 길 0).
///   ⬛ 고치기 전(②만)엔 이동 냄이면 늘 <see cref="Step.StandDown"/> 으로 끝나 진행 그림이 꺼지지 않았다.<br/>
/// 로그인 시작 때 <see cref="Reset"/> 으로 셋 다 비운다(앞 시도의 칸이 남아 안전망을 막지 않게 · 3V-P3-1).<br/>
/// Blazor 비의존 순수 C# — 시험 프로젝트가 소스를 링크해 <b>실제로 불러</b> 잰다(RollbackWatch.cs 와 같은 방식).
/// 시간은 부르는 쪽이 밀리초 숫자로 넘긴다(화면 = <c>Environment.TickCount64</c> · 시험 = 가짜 시계).
/// 앱 한 벌(브라우저 탭 하나)에 하나 — 로그인 화면의 정적 칸이 들고 있다(D1 · DI · Program.cs 손 0). forceLoad 다시 읽기로 저절로 비워진다.
/// </remarks>
public sealed class LoginInFlightSafetyNet
{
    /// <summary>기다리는 화면이 판정을 다시 묻는 간격(밀리초).</summary>
    public const int PollMs = 250;

    /// <summary>
    /// 🔴 20261128작 갈래 A 고침 2 (작업지시서 §8-5 A1-① · PM 결재) — 멈춤 상한 10분(밀리초).
    /// 알림 결과가 영영 안 오면(생길 길은 코드 읽기로 못 찾음 · 가설) 다시 만들어진 화면이 진행 막대에 머문다.
    /// 멈춤이 이만큼 이어지면 멈춤을 무시하고 종전대로 `/` 1회(<see cref="Step.GoHome"/>) — 최악 = 고치기 전과 같음.
    /// </summary>
    public const int MaxInputPauseMs = 10 * 60 * 1000;

    /// <summary>
    /// 🔴 HQA 보류 고침 (작업지시서 §8-13 · PM 결재 E2) — 물러남 시간 30초(밀리초).
    /// 안전망 15초 칸은 다시 쓰지 않는다 — 실세계 느린 문서(터널 재연결 등 15초 넘게 늦음) 중간에 입력 칸이 튀어나오는 일을 줄이고,
    /// CI 문서 20초 늦춤 장면(terms-slow-doc)의 진행 그림과도 겹치지 않게 20초보다 길게.
    /// </summary>
    public const int FallBackToFormMs = 30000;

    private readonly int _waitMs;
    private int _inputWaitDepth;
    private long? _lastInputEndedAtMs;
    private bool _navigated;
    // 🔴 고침 2 — 이번 멈춤(깊이 0 → 1 이상)을 기다리는 화면이 처음 본 시각. 시계는 기다리는 화면의 것(Next 의 nowMs) 하나만 쓴다
    //   — 알림을 띄우는 화면(BeginWaitingForInput)은 시각을 넘기지 않으므로 Login.razor 손 0. 깊이가 0 으로 돌아오거나 Reset 이면 비운다.
    //   처음 보는 시각 ≤ 멈춘 시각 + PollMs(250ms) — 상한이 그만큼 늦을 뿐(빨라지지 않음).
    private readonly int _maxPauseMs;
    private long? _pauseSeenAtMs;
    // 🔴 HQA 보류 고침 — 이동 냄 뒤 기다리는 화면이 ShouldFallBackToForm 을 처음 부른 시각(멈춤 상한과 같은 「기다리는 쪽 시계 하나」 꼴).
    //   앞 화면(MarkNavigated)은 시각을 넘기지 않는다 ⇒ 앞 화면 이동 3곳 손 0. Reset 이면 비운다.
    private long? _navigatedSeenAtMs;

    // ⬛ [낡은 몸 · 고침 2 전] public LoginInFlightSafetyNet(int waitMs) { _waitMs = waitMs; }
    //   사유: 멈춤 상한 칸이 더해졌다 — 화면이 부르는 한 인자 꼴은 그대로 두고 상한 = MaxInputPauseMs(10분).
    public LoginInFlightSafetyNet(int waitMs)
        : this(waitMs, MaxInputPauseMs)
    {
    }

    /// <summary>🔴 고침 2 — 멈춤 상한을 따로 받는 꼴(화면은 쓰지 않는다 · 한 인자 꼴이 10분을 넘긴다).</summary>
    public LoginInFlightSafetyNet(int waitMs, int maxPauseMs)
    {
        _waitMs = waitMs;
        _maxPauseMs = maxPauseMs;
    }

    /// <summary>안전망이 할 일.</summary>
    public enum Step
    {
        /// <summary>아직 기다린다(멈춤이거나 15초 전).</summary>
        Wait,

        /// <summary>멈춤 아님 상태로 15초가 지났고 이동도 아직 없다 ⇒ `/` 1회(부르는 쪽이 주소가 아직 login 인지 한 번 더 본다).
        /// 🔴 고침 2 — 멈춤이 <see cref="MaxInputPauseMs"/>(10분) 넘게 이어져도 같다.</summary>
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
        _pauseSeenAtMs = null;   // 🔴 고침 2
        _navigatedSeenAtMs = null;   // 🔴 HQA 보류 고침 — 다음 로그인 시도가 앞 시도의 물러남 시계를 안 물려받게
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

        // 🔴 고침 2 — 멈춤이 다 풀렸으면 이번 멈춤의 상한 시계도 비운다(다음 멈춤은 처음부터 10분).
        if (_inputWaitDepth == 0)
        {
            _pauseSeenAtMs = null;
        }
    }

    /// <summary>로그인을 부른 화면이 이동을 낸 순간 부른다(갈 곳 식 손 0 — 냈다는 표시만).</summary>
    public void MarkNavigated()
    {
        _navigated = true;
    }

    /// <summary>
    /// 🔴 HQA 보류 고침 (작업지시서 §8-13 · 설계 로그인경합_고침2_정적칸남음 §3-3) — 물러남 판정.
    /// 이동 냄 아님 ⇒ 늘 거짓(그 길은 <see cref="Next"/> 가 맡는다). 이동 냄 ⇒ 이 판정을 처음 부른 시각을 적고(<c>??=</c>),
    /// 그때부터 <see cref="FallBackToFormMs"/>(30초)가 지나면 참 — 참이면 기다리는 화면이 진행 그림을 끄고 기존 로그인 입력 칸을 그린다.
    /// 이동은 내지 않는다(<see cref="Next"/> 의 <see cref="Step.StandDown"/> 뜻 「안전망 `/` 영구 해제」 그대로).
    /// </summary>
    public bool ShouldFallBackToForm(long nowMs)
    {
        if (!_navigated)
        {
            return false;
        }

        _navigatedSeenAtMs ??= nowMs;
        return nowMs - _navigatedSeenAtMs.Value >= FallBackToFormMs;
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

        // ⬛ [낡은 줄 · 고침 2 전] if (IsWaitingForInput) { return Step.Wait; }
        //   사유: 멈춤에 상한이 없어 알림 결과가 안 오면 진행 막대에 머물렀다(§8-5 A1-①).
        // 🔴 고침 2 — 멈춤이면 기다리되, 이번 멈춤을 처음 본 때부터 10분이 지나면 종전대로 `/` 1회.
        if (IsWaitingForInput)
        {
            _pauseSeenAtMs ??= nowMs;
            return nowMs - _pauseSeenAtMs.Value >= _maxPauseMs ? Step.GoHome : Step.Wait;
        }

        var anchor = _lastInputEndedAtMs is long ended && ended > waitStartedAtMs ? ended : waitStartedAtMs;
        return nowMs - anchor >= _waitMs ? Step.GoHome : Step.Wait;
    }
}
