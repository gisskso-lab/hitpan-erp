namespace HitPan.Web.Services;

/// <summary>
/// 🔴 <b>자료보관 컴퓨터인지 먼저 묻고, 통과한 뒤에만 화면이 자료를 부른다</b> — 20260924작1 절D.
/// </summary>
/// <remarks>
/// <para>
/// [무엇이 문제였나] 백업·자료이관 화면은 <c>OnInitializedAsync</c> 에서 곧바로 조회를 쐈다.
/// 그런데 그 조회는 <b>자료보관 컴퓨터에서만</b> 열리는 문이다. 클라이언트 PC 에서는
/// <b>매번 403 이 콘솔에 쌓였고</b>, 메인PC 에서도 왕복이 끝나기 전에 쏘면 한 번 튕겼다.
/// </para>
/// <para>
/// ⇒ 판정이 <b>통과</b>로 끝난 뒤에만 부른다. 화면을 감추는 것이 차단이 아니듯
/// (서버 판정은 그대로 둔다), 이것은 <b>소음과 깜빡임을 없애는 일</b>이다.
/// </para>
/// <para>
/// ⚠️ <b>Blazor 를 안 쓴다</b>(의도). 게이트(G-27)가 이 파일을 <b>그대로 링크해</b>
/// <i>"막혔을 때 0회 · 통과했을 때 정확히 1회"</i> 를 <b>세어 본다</b> — 글자검사가 아니다.
/// </para>
/// </remarks>
public sealed class MainPcAccessState
{
    private bool _resolved;

    /// <summary>아직 묻는 중인가 — 화면은 이때 회전자만 보여준다.</summary>
    public bool Checking { get; private set; } = true;

    /// <summary>🟢 자료보관 컴퓨터로 확인됐는가.</summary>
    public bool IsMainPc { get; private set; }

    /// <summary>
    /// 한 번 묻고, <b>통과했을 때만</b> <paramref name="onAllowed"/> 를 <b>한 번</b> 부른다.
    /// </summary>
    /// <param name="ask">서버에 묻는 일(<c>api/devices/is-main-pc</c>).</param>
    /// <param name="onAllowed">통과 뒤에 할 일(화면의 조회). 없으면 안 부른다.</param>
    /// <param name="log">🔴 #15 — 못 물어봤으면 흔적을 남긴다. 그리고 <b>잠근 채로</b> 둔다.</param>
    public async Task ResolveAsync(Func<Task<bool>> ask, Func<Task>? onAllowed, Action<Exception> log)
    {
        // 🔴 두 번 불러도 두 번 열지 않는다 — 재렌더링이 조회를 또 쏘면 그것도 소음이다.
        if (_resolved) return;
        _resolved = true;

        try
        {
            IsMainPc = await ask().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 못 물어봤으면 **안 보여준다.** 확인이 안 된 상태에서 열어주면 막은 적이 없는 것과 같다.
            log(ex);
            IsMainPc = false;
        }
        finally
        {
            Checking = false;
        }

        if (IsMainPc && onAllowed is not null)
        {
            await onAllowed().ConfigureAwait(false);
        }
    }
}
