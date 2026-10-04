using HitPan.API.Services.LocalSwap;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 I-API 6 — S2 부팅 안전망(<c>/SC ONSTART</c> 임시 작업 <c>HitPan-ERP-keepalive-restore</c>)이
/// 교체 일꾼의 <b>모든 끝</b>에서 어떻게 남는지 잰다. 작업지시서 §11 조건부 승인: 「S7(성공·실패 모두)에서 반드시 스스로 지운다 ·
/// 끝난 뒤 <c>HitPan-*</c> 작업 잔존 0(대조군: 지우기 줄 뺀 사본)」.
/// </summary>
/// <remarks>
/// <para>🟢 일꾼 <c>-TestRoot</c> 모드만 쓴다(<see cref="LocalSwapWorkerRig"/>) — <c>schtasks</c> 는 <c>{root}\tasks</c> 대역 파일 ·
/// 실제 예약 작업·서비스·프로세스 무접촉.</para>
/// <para>⚠️ <b>broken 한 줄은 §11 과 코드가 다르다</b> — 일꾼 <c>local-swap.ps1:651</c> 은 broken(되돌리기도 확인 못 함)에서
/// 안전망을 <b>일부러 남긴다</b>(재부팅이 keepalive 를 되살리는 마지막 복구 수단 · 계약 §6 broken 행). 이 게이트는 지금 코드를
/// 그대로 재고(남는다), PM 판정이 「broken 도 지운다」면 그 한 줄의 기대만 뒤집는다(개발명세서 I-API §6).</para>
/// </remarks>
public sealed class LocalSwapOnStartGateTests
{
    private const string NetTask = "HitPan-ERP-keepalive-restore";

    private static string[] Run(string? failAt, out SwapRequest final, out string calls, string? scriptText = null, bool netAlreadyThere = false)
    {
        using var rig = LocalSwapWorkerRig.Rollback(DateTime.UtcNow);
        if (netAlreadyThere) File.WriteAllText(Path.Combine(rig.TasksDir, NetTask), "task");
        var exit = rig.Run(scriptText, failAt);
        Assert.True(exit == 0, "일꾼 종료 코드 " + exit + " / " + rig.Log());
        final = rig.Final();
        calls = rig.Calls();
        return rig.LeftoverTasks();
    }

    [Fact(DisplayName = "I-API6 🟢 success — 이 판이 만든 ONSTART 안전망 · 1회용 작업 둘 다 지워진다(HitPan-* 잔존 0)")]
    public void Success_removes_net()
    {
        var left = Run(null, out var final, out var calls);
        Assert.Equal(SwapStates.Success, final.State);
        Assert.Contains("/SC ONSTART", calls);              // 실제로 만들었고
        Assert.Contains("/Delete /TN \"" + NetTask + "\"", calls); // 스스로 지웠다
        Assert.Empty(left);
    }

    [Fact(DisplayName = "I-API6 🟢 reverted(S6 확인 실패 → 원위치) — 안전망 지워짐 · 잔존 0")]
    public void Reverted_after_verify_failure_removes_net()
    {
        var left = Run("S6", out var final, out _);
        Assert.Equal(SwapStates.Reverted, final.State);
        Assert.Equal(SwapReasons.VerifyFailed, final.Reason);
        Assert.Empty(left);
    }

    [Fact(DisplayName = "I-API6 🟢 reverted(S3 멈추기 실패) — 안전망 지워짐 · 잔존 0")]
    public void Reverted_after_stop_failure_removes_net()
    {
        var left = Run("S3", out var final, out _);
        Assert.Equal(SwapStates.Reverted, final.State);
        Assert.Equal(SwapReasons.StopFailed, final.Reason);
        Assert.Empty(left);
    }

    [Fact(DisplayName = "I-API6 🟢 refused(S2 안전망 확인 실패) — 만든 것 지우고 멈춘다 · 잔존 0")]
    public void Refused_at_s2_removes_net()
    {
        var left = Run("S2", out var final, out var calls);
        Assert.Equal(SwapStates.Refused, final.State);
        Assert.Equal(SwapReasons.SafetyNetFailed, final.Reason);
        Assert.Contains("/SC ONSTART", calls);
        Assert.Empty(left);
    }

    [Fact(DisplayName = "I-API6 🟢 refused(S1 재료 불량) — 안전망을 만들기 전에 끝난다 · ONSTART 호출 0 · 잔존 0")]
    public void Refused_at_s1_never_creates_net()
    {
        var left = Run("S1", out var final, out var calls);
        Assert.Equal(SwapStates.Refused, final.State);
        Assert.DoesNotContain("ONSTART", calls);
        Assert.Empty(left);
    }

    [Fact(DisplayName = "I-API6 🟢 이미 있던 안전망(자동 업데이트 UpdateProcessGate 몫)은 남의 것 — 새로 만들지도 지우지도 않는다")]
    public void Pre_existing_net_is_not_ours()
    {
        var left = Run(null, out var final, out var calls, netAlreadyThere: true);
        Assert.Equal(SwapStates.Success, final.State);
        Assert.DoesNotContain("ONSTART", calls);
        Assert.DoesNotContain("/Delete /TN \"" + NetTask + "\"", calls);
        Assert.Equal(new[] { NetTask }, left);
    }

    [Fact(DisplayName = "I-API6 ⚠️ broken(S6·S7 둘 다 실패) — 지금 코드 = 안전망 남김(계약 §6 · 재부팅 복구) · 1회용 작업은 지워짐 · PM 판정 대기")]
    public void Broken_keeps_net_by_current_design()
    {
        var left = Run("S6,S7", out var final, out _);
        Assert.Equal(SwapStates.Broken, final.State);
        Assert.Equal(SwapReasons.RevertFailed, final.Reason);
        // 남는 것은 부팅 안전망 하나뿐 — 1회용 교체 작업(HitPan-LocalSwap)은 finally 에서 지워진다
        Assert.Equal(new[] { NetTask }, left);
    }

    [Fact(DisplayName = "I-API6 대조군 🔴 success 의 지우기 줄을 뺀 일꾼 사본 → 안전망이 남는다(게이트가 FAIL 을 낸다)")]
    public void Control_without_delete_line_leaves_net()
    {
        var original = LocalSwapWorkerRig.OriginalScript();
        const string line = "    if ($script:CreatedNet) { Invoke-Schtasks ('/Delete /TN \"' + $RestoreTaskName + '\" /F') | Out-Null }\r\n}";
        var lf = line.Replace("\r\n", "\n", StringComparison.Ordinal);
        var target = original.Contains(line, StringComparison.Ordinal) ? line : lf;
        Assert.True(original.Contains(target, StringComparison.Ordinal), "Clear-AfterSuccess 의 지우기 줄을 못 찾았다 — 대조군을 같이 고쳐라");
        var copy = original.Replace(target, target.EndsWith("\r\n}", StringComparison.Ordinal) ? "\r\n}" : "\n}", StringComparison.Ordinal);

        var left = Run(null, out var final, out _, copy);
        Assert.Equal(SwapStates.Success, final.State);
        Assert.Equal(new[] { NetTask }, left); // 원본이면 Empty — Success_removes_net 이 이것을 잡는다
    }

    // ── 작1 §19 G-ENV1 — 부모 셸(CI 의 pwsh 7)이 물려준 PSModulePath 에 일꾼이 흔들리지 않는다 ──
    // 독 = pwsh 7.4.6 `Modules\Microsoft.PowerShell.Utility` 와 같은 모양 — psd1 하나 · 내보내기 목록 116개 그대로 · dll 은 폴더에 없음.
    // 5.1 이 이것을 먼저 찾으면 Get-Date·Add-Type 은 되고 Get-FileHash 만 「not recognized」(10/5 실측 · CI 와 같은 문구 ·
    // 선행검증서 20261002 PR449 CI75). ⚠️ Get-FileHash 하나만 내보내는 가짜는 일꾼이 먼저 쓰는 다른 명령에 정품이 불려 와 풀린다(실측).
    private static readonly string[] Pwsh7UtilityCmdlets =
    {
        "'Export-Alias','Get-Alias','Import-Alias','New-Alias','Remove-Alias','Set-Alias','Export-Clixml','Import-Clixml','Measure-Command'",
        "'Trace-Command','ConvertFrom-Csv','ConvertTo-Csv','Export-Csv','Import-Csv','Get-Culture','Format-Custom','Get-Date','Set-Date'",
        "'Write-Debug','Wait-Debugger','Register-EngineEvent','Write-Error','Get-Event','New-Event','Remove-Event','Unregister-Event','Wait-Event'",
        "'Get-EventSubscriber','Invoke-Expression','Out-File','Unblock-File','Get-FileHash','Export-FormatData','Get-FormatData','Update-FormatData','New-Guid'",
        "'Format-Hex','Get-Host','Read-Host','Write-Host','ConvertTo-Html','Write-Information','ConvertFrom-Json','ConvertTo-Json','Test-Json'",
        "'Format-List','Import-LocalizedData','Send-MailMessage','ConvertFrom-Markdown','Show-Markdown','Get-MarkdownOption','Set-MarkdownOption','Add-Member','Get-Member'",
        "'Compare-Object','Group-Object','Measure-Object','New-Object','Select-Object','Sort-Object','Tee-Object','Register-ObjectEvent','Write-Output'",
        "'Import-PowerShellDataFile','Write-Progress','Disable-PSBreakpoint','Enable-PSBreakpoint','Get-PSBreakpoint','Remove-PSBreakpoint','Set-PSBreakpoint','Get-PSCallStack','Export-PSSession'",
        "'Import-PSSession','Get-Random','Get-SecureRandom','Invoke-RestMethod','Debug-Runspace','Get-Runspace','Disable-RunspaceDebug','Enable-RunspaceDebug','Get-RunspaceDebug'",
        "'ConvertFrom-SddlString','Start-Sleep','Join-String','Out-String','Select-String','ConvertFrom-StringData','Format-Table','New-TemporaryFile','New-TimeSpan'",
        "'Get-TraceSource','Set-TraceSource','Add-Type','Get-TypeData','Remove-TypeData','Update-TypeData','Get-UICulture','Get-Unique','Get-Uptime'",
        "'Clear-Variable','Get-Variable','New-Variable','Remove-Variable','Set-Variable','Get-Verb','Write-Verbose','Write-Warning','Invoke-WebRequest'",
        "'Format-Wide','ConvertTo-Xml','Select-Xml','Get-Error','Update-List','Out-GridView','Show-Command','Out-Printer'",
    };

    private static string PoisonedParentPsModulePath(string root)
    {
        var dir = Path.Combine(root, "pwsh7-modules", "Microsoft.PowerShell.Utility");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Microsoft.PowerShell.Utility.psd1"),
            "@{ GUID = '1DA87E53-152B-403E-98DC-74D7B4D63D59'; ModuleVersion = '7.0.0.0'; CompatiblePSEditions = @('Core'); "
            + "PowerShellVersion = '3.0'; CmdletsToExport = @(" + string.Join(",", Pwsh7UtilityCmdlets) + "); "
            + "NestedModules = @('Microsoft.PowerShell.Commands.Utility.dll') }");
        return Path.Combine(root, "pwsh7-modules") + Path.PathSeparator + (Environment.GetEnvironmentVariable("PSModulePath") ?? "");
    }

    private static (SwapRequest Final, string Log, string[] Left) RunPoisoned(bool controlKeep)
    {
        using var rig = LocalSwapWorkerRig.Rollback(DateTime.UtcNow);
        rig.SimulatedParentPsModulePath = PoisonedParentPsModulePath(rig.Root);
        rig.ControlKeepPsModulePath = controlKeep;
        var exit = rig.Run(null, null);
        Assert.True(exit == 0, "일꾼 종료 코드 " + exit + " / " + rig.Log());
        return (rig.Final(), rig.Log(), rig.LeftoverTasks());
    }

    [Fact(DisplayName = "G-ENV1 🟢 부모가 pwsh 7 모듈 경로를 물려줘도 일꾼은 끝까지 간다 — success · 안전망 잔존 0")]
    public void Parent_pwsh7_module_path_does_not_break_worker()
    {
        var (final, log, left) = RunPoisoned(controlKeep: false);
        Assert.True(final.State == SwapStates.Success, "끝 " + final.State + "/" + final.Reason + " / " + log);
        Assert.DoesNotContain("Get-FileHash", log);
        Assert.Empty(left);
    }

    [Fact(DisplayName = "G-ENV1 대조군 🔴 PSModulePath 를 빼지 않고 넘김 → Get-FileHash 를 못 찾아 broken(게이트가 FAIL 을 낸다 · CI 75 실패의 모양)")]
    public void Control_keeping_module_path_breaks_worker()
    {
        var (final, log, _) = RunPoisoned(controlKeep: true);
        Assert.True(final.State == SwapStates.Broken, "끝 " + final.State + " — 독이 안 섰다(G-ENV1 초록은 무효) / " + log); // success 면 독이 안 선 것
        Assert.Contains("Get-FileHash", log);
    }
}
