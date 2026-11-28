using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Backup;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ 고침 1 [3-V] P3-2 — 기동 때 1회: 앞 프로세스(정전 · 워치독 업데이트 · 강제 종료)가 남긴 <c>restore_history</c> <c>running</c> 줄을
/// <c>failed</c>(<see cref="SafeRestoreDenyCodes.RestoreInterrupted"/>)로 닫는다 — 정본 기록에 「무슨 일이 있었나」 가 비지 않게.
/// </summary>
/// <remarks>
/// <para>자료는 손 0 — ⑤ 도중 죽으면 서버가 커밋 안 된 거래를 되돌리고, 성공이면 같은 거래가 이미 <c>success</c> 로 바꿨으므로 남은 <c>running</c> = 자료 그대로.</para>
/// <para>원장 교체 함수(<see cref="RestoreSwapStep"/>) 밖 정적 함수 — G9 울타리(교체 함수를 부르는 곳 = 그 클래스 안뿐)에 닿지 않음. 기록 시각 = 서버 지역 시각(D3).</para>
/// </remarks>
public static class RestoreHistoryRecovery
{
    /// <summary>앞 프로세스의 <c>running</c> 줄을 닫고 닫은 줄 수를 돌려줌(명시 커밋 · P3-4 와 같은 꼴).</summary>
    public static async Task<int> CloseInterruptedAsync(IDbConnection db, DateTime nowLocal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.State == ConnectionState.Broken) db.Close();
        if (db.State != ConnectionState.Open)
        {
            if (db is DbConnection dc) await dc.OpenAsync(ct).ConfigureAwait(false);
            else db.Open();
        }
        await using var tx = await ((DbConnection)db).BeginTransactionAsync(ct).ConfigureAwait(false);
        var n = await db.ExecuteAsync(new CommandDefinition(RestoreSwapSql.CloseInterruptedHistory,
            new { Now = nowLocal, Code = SafeRestoreDenyCodes.RestoreInterrupted }, tx, cancellationToken: ct)).ConfigureAwait(false);
        await tx.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return n;
    }
}
