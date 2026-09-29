namespace HitPan.Web.Services;

/// <summary>
/// 🔵 업데이트 팝업의 <b>유일한 방아쇠 자리</b> (20260929작3 절F5 · 설계 §8).
/// </summary>
/// <remarks>
/// <para>
/// 팝업은 <c>UpdateConsentGate.razor</c> 하나만 띄운다. 로그인 직후(게이트 자신) · 사이드바 [업데이트 마치기] ·
/// 그리고 나중에 붙을 방아쇠(Q-3 「바로도」 — 사장님 재확인 중)는 <b>전부 여기를 부른다</b>
/// ⇒ 새 방아쇠는 <see cref="RequestPromptAsync"/> 를 부르는 한 줄을 <b>더하기만</b> 하면 된다(#1).
/// </para>
/// <para>
/// 🔴 사이드바 버튼이 동의를 직접 쓰거나 적용을 부르면 안 된다(#43 금지 3) — 같은 팝업 → 같은
/// <c>update-consent</c> → 같은 소비. 이 버스는 「팝업을 열어 달라」만 전한다.
/// </para>
/// <para>
/// 게이트가 받아 온 최신 상태도 여기로 흘린다(<see cref="Publish"/>) — 메인PC 확인이 늦게 와 게이트가 다시 물은
/// 결과(M-22 R-1)를 사이드바가 <b>추가 호출 없이</b> 따라간다.
/// </para>
/// <para>⚠️ Blazor 를 안 쓴다(의도) — 시험이 링크해 부른다.</para>
/// </remarks>
public sealed class UpdatePromptBus
{
    private readonly object _gate = new();
    private Func<Task>? _promptHandler;

    /// <summary>게이트가 마지막으로 흘린 상태. 아직 없으면 null.</summary>
    public UpdateStatusSnapshot? Latest { get; private set; }

    /// <summary>
    /// <see cref="Publish"/> 할 때마다 올라간다 — 사이드바가 「내 조회가 끝나기 전에 더 새 값이 왔나」를 가른다.
    /// </summary>
    public int Sequence { get; private set; }

    /// <summary>상태가 바뀔 때 알린다(사이드바가 받는다).</summary>
    public event Action<UpdateStatusSnapshot>? StatusChanged;

    /// <summary>팝업을 띄우는 쪽(게이트)이 자기를 건다. 하나만 걸린다(나중에 건 것이 이긴다).</summary>
    public void AttachPrompt(Func<Task> handler)
    {
        lock (_gate) { _promptHandler = handler; }
    }

    /// <summary>자기가 건 것일 때만 뗀다(다른 게이트가 이미 걸었으면 그대로 둔다).</summary>
    public void DetachPrompt(Func<Task> handler)
    {
        lock (_gate)
        {
            if (_promptHandler == handler) _promptHandler = null;
        }
    }

    /// <summary>
    /// 🔵 팝업을 열어 달라. 받을 게이트가 없으면 <c>false</c>(부른 쪽이 흔적만 남긴다 · #15).
    /// </summary>
    public async Task<bool> RequestPromptAsync()
    {
        Func<Task>? handler;
        lock (_gate) { handler = _promptHandler; }
        if (handler is null) return false;

        // ⚠️ ConfigureAwait(false) 를 쓰지 않는다 — 받는 쪽이 화면(Blazor) 컴포넌트다.
        await handler();
        return true;
    }

    /// <summary>게이트가 받아 온 최신 상태를 흘린다.</summary>
    public void Publish(UpdateStatusSnapshot snapshot)
    {
        lock (_gate)
        {
            Latest = snapshot;
            Sequence++;
        }

        StatusChanged?.Invoke(snapshot);
    }
}
