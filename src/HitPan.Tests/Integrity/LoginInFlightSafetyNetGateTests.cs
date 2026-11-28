using HitPan.Web.Services;
using Step = HitPan.Web.Services.LoginInFlightSafetyNet.Step;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261128작 로그인 경합 갈래 A 고침 1 (작업지시서 §8-2 A-① · §8-3 3V-P2 · 3V-P3-1) — 안전망 판정.
/// </summary>
/// <remarks>
/// 제품 <c>LoginInFlightSafetyNet</c> 을 <b>실제로 불러</b> 잰다(소스 링크 · <c>HitPan.Tests.csproj</c>). 소스 글자를 읽지 않는다.
/// 시간은 가짜 시계(밀리초 숫자) — 화면은 같은 판정을 <c>Environment.TickCount64</c> 로 부른다.
/// ⚠️ 이 시험이 재는 것 = 판정(멈춤 · 다시 15초 · 이동 냄이면 해제 · 비움). 화면이 알림 앞뒤 · 이동 뒤에 그 칸을 부르는지(배선)는
///   화면 부품이라 여기서 못 잰다 ⇒ 갈래 T 장면 G 줄 2개(개발명세서 「고침 1」 절).
/// </remarks>
public sealed class LoginInFlightSafetyNetGateTests
{
    private const int WaitMs = 15000;

    [Fact(DisplayName = "A-①a 멈춤 없음 · 이동 없음 — 기다리기 시작부터 15초 전엔 기다림 · 15초에 `/` 1회")]
    public void NoInput_GoHomeAtFifteenSeconds()
    {
        var net = new LoginInFlightSafetyNet(WaitMs);

        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 1_000, nowMs: 1_000));
        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 1_000, nowMs: 15_999));
        Assert.Equal(Step.GoHome, net.Next(waitStartedAtMs: 1_000, nowMs: 16_000));
    }

    [Fact(DisplayName = "A-①b 알림 20초 열어 둠 → 그동안 `/` 0 · 닫은 뒤 다시 15초 · 그 뒤에만 `/`")]
    public void DialogOpenTwentySeconds_ClockPausedThenRestarts()
    {
        var net = new LoginInFlightSafetyNet(WaitMs);
        const long waitStart = 0;

        // 앞 화면이 새 기기 알림을 띄웠다.
        net.BeginWaitingForInput();
        Assert.True(net.IsWaitingForInput);

        // 알림이 떠 있는 동안 — 15초 · 20초가 지나도 보내지 않는다.
        Assert.Equal(Step.Wait, net.Next(waitStart, nowMs: 15_000));
        Assert.Equal(Step.Wait, net.Next(waitStart, nowMs: 20_500));

        // 사용자가 20.5초에 닫았다.
        net.EndWaitingForInput(nowMs: 20_500);
        Assert.False(net.IsWaitingForInput);

        // 닫은 시각부터 다시 15초 — 그 전엔 기다림(고치기 전 꼴이면 여기서 이미 `/`).
        Assert.Equal(Step.Wait, net.Next(waitStart, nowMs: 21_000));
        Assert.Equal(Step.Wait, net.Next(waitStart, nowMs: 35_499));
        Assert.Equal(Step.GoHome, net.Next(waitStart, nowMs: 35_500));
    }

    [Fact(DisplayName = "A-①c 대화 창 두 겹 — 하나만 닫혀도 아직 멈춤 · 둘 다 닫힌 뒤 15초")]
    public void NestedInputs_StayPausedUntilAllEnd()
    {
        var net = new LoginInFlightSafetyNet(WaitMs);

        net.BeginWaitingForInput();
        net.BeginWaitingForInput();
        net.EndWaitingForInput(nowMs: 5_000);

        Assert.True(net.IsWaitingForInput);
        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 0, nowMs: 60_000));

        net.EndWaitingForInput(nowMs: 60_000);
        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 0, nowMs: 74_999));
        Assert.Equal(Step.GoHome, net.Next(waitStartedAtMs: 0, nowMs: 75_000));
    }

    [Fact(DisplayName = "A-①d 기다리기 전에 끝난 입력은 시계를 늦추지 않는다 · 짝 없는 끝은 음수 멈춤을 만들지 않는다")]
    public void InputEndedBeforeWaitStarted_DoesNotDelay()
    {
        var net = new LoginInFlightSafetyNet(WaitMs);

        net.EndWaitingForInput(nowMs: 2_000);   // 짝 없는 끝
        Assert.False(net.IsWaitingForInput);

        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 10_000, nowMs: 24_999));
        Assert.Equal(Step.GoHome, net.Next(waitStartedAtMs: 10_000, nowMs: 25_000));

        // 짝 없는 끝 뒤에 한 번 멈추면 정말로 멈춘다(깊이가 음수로 내려가 있지 않다).
        net.BeginWaitingForInput();
        Assert.True(net.IsWaitingForInput);
        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 10_000, nowMs: 90_000));
    }

    [Fact(DisplayName = "A1-① 고침 2 멈춤 상한 10분 — 알림 결과가 안 와도 처음 본 때부터 10분에 종전대로 `/` 1회 · 그 전엔 기다림")]
    public void PauseNeverEnds_GoHomeAfterTenMinutes()
    {
        // 화면과 같은 한 인자 꼴 — 상한은 제품 기본값(MaxInputPauseMs)을 그대로 잰다.
        var net = new LoginInFlightSafetyNet(WaitMs);
        Assert.Equal(10 * 60 * 1000, LoginInFlightSafetyNet.MaxInputPauseMs);

        net.BeginWaitingForInput();   // 알림을 띄웠는데 결과가 영영 안 온다(EndWaitingForInput 0)

        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 0, nowMs: 1_000));       // 처음 본 때 = 1초
        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 0, nowMs: 300_000));     // 5분 — 아직 멈춤
        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 0, nowMs: 600_999));     // 10분 − 1ms
        Assert.Equal(Step.GoHome, net.Next(waitStartedAtMs: 0, nowMs: 601_000));   // 10분 — 고치기 전엔 여기서도 Wait(진행 막대에 머묾)

        // 이동을 냈으면 상한보다 해제가 먼저(§8-3 3V-P2 그대로).
        net.MarkNavigated();
        Assert.Equal(Step.StandDown, net.Next(waitStartedAtMs: 0, nowMs: 700_000));

        // 멈춤이 다 풀리면 상한 시계도 비워진다 — 다음 멈춤은 처음부터 10분.
        var net2 = new LoginInFlightSafetyNet(WaitMs);
        net2.BeginWaitingForInput();
        Assert.Equal(Step.Wait, net2.Next(waitStartedAtMs: 0, nowMs: 0));
        net2.EndWaitingForInput(nowMs: 500_000);
        net2.BeginWaitingForInput();
        Assert.Equal(Step.Wait, net2.Next(waitStartedAtMs: 0, nowMs: 700_000));    // 새 멈춤을 처음 본 때 = 700초
        Assert.Equal(Step.Wait, net2.Next(waitStartedAtMs: 0, nowMs: 1_299_999));
        Assert.Equal(Step.GoHome, net2.Next(waitStartedAtMs: 0, nowMs: 1_300_000));
    }

    [Fact(DisplayName = "3V-P2a 앞 화면이 `/terms` 이동을 냈다 → 새 문서가 20초 늦어도 안전망 `/` 0(해제)")]
    public void Navigated_SlowDocument_NeverGoesHome()
    {
        var net = new LoginInFlightSafetyNet(WaitMs);
        const long waitStart = 0;

        net.MarkNavigated();   // 앞 화면 `:468` /terms 또는 `:531` 갈 곳 이동 직후

        Assert.True(net.HasNavigated);
        Assert.Equal(Step.StandDown, net.Next(waitStart, nowMs: 1_000));
        Assert.Equal(Step.StandDown, net.Next(waitStart, nowMs: 15_000));
        Assert.Equal(Step.StandDown, net.Next(waitStart, nowMs: 20_000));   // 고치기 전 꼴이면 GoHome = 앞 이동 취소
        Assert.Equal(Step.StandDown, net.Next(waitStart, nowMs: 600_000));
    }

    [Fact(DisplayName = "3V-P2b 알림을 닫고 이동을 냈다 → 멈춤 풀림 뒤에도 해제가 먼저")]
    public void DialogThenNavigated_StandDownWins()
    {
        var net = new LoginInFlightSafetyNet(WaitMs);

        net.BeginWaitingForInput();
        net.EndWaitingForInput(nowMs: 20_000);
        net.MarkNavigated();

        Assert.Equal(Step.StandDown, net.Next(waitStartedAtMs: 0, nowMs: 40_000));
    }

    [Fact(DisplayName = "3V-P3-1 비움 — 앞 시도의 멈춤 · 이동 냄이 남아도 Reset 뒤 안전망은 다시 산다(진행 막대만 남는 길 0)")]
    public void Reset_ClearsPauseAndNavigated()
    {
        var net = new LoginInFlightSafetyNet(WaitMs);

        net.BeginWaitingForInput();
        net.MarkNavigated();
        net.EndWaitingForInput(nowMs: 50_000);

        net.Reset();

        Assert.False(net.IsWaitingForInput);
        Assert.False(net.HasNavigated);
        // 마지막 입력 시각도 비워졌다 — 기다리기 시작한 때부터 15초.
        Assert.Equal(Step.Wait, net.Next(waitStartedAtMs: 0, nowMs: 14_999));
        Assert.Equal(Step.GoHome, net.Next(waitStartedAtMs: 0, nowMs: 15_000));
    }
}
