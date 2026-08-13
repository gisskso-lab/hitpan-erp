using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 그룹웨어 단계9 사내 메신저 게이트. 작(2026-08-13).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 이 시험들은 <b>되돌아가는 것을 막는다.</b> 사장님이 8/13 하루에 범위를 두 번 자르셨고,
/// 잘라낸 것들은 전부 <b>"넣어주면 좋잖아"</b> 가 다시 나오기 쉬운 것들이다:
/// </para>
/// <list type="bullet">
///   <item><i>"연결까지만 해도 충분함"</i> — 메신저에서 문서 <b>생성·결재</b> 안 만든다</item>
///   <item><i>"통신이 안정적이지 않다면 안되는걸로 생각하자"</i> — <b>조각 업로드</b> 안 만든다(100MB 를 접으셨다)</item>
///   <item><i>"본인 대화만 열람"</i> — <b>부모계정도</b> 남의 1:1 을 못 본다</item>
///   <item><i>"파일전송은 최소한으로"</i> — 3중 한도를 지운다면 ERP 백업이 무너진다</item>
/// </list>
/// </remarks>
public sealed class ChatGuardTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석을 걷어낸 코드만 남긴다. 설명문에 걸려 헛통과·헛실패하지 않게.</summary>
    private static string StripComments(string source)
        => string.Join('\n', source.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("///", StringComparison.Ordinal)
                && !t.StartsWith("--", StringComparison.Ordinal)
                && !t.StartsWith("*", StringComparison.Ordinal)
                && !t.StartsWith("@*", StringComparison.Ordinal);
        }));

    private static string ChatService() => ReadSource("src", "HitPan.Application", "Services", "ChatService.cs");
    private static string ChatController() => ReadSource("src", "HitPan.API", "Controllers", "ChatController.cs");
    private static string ChatFileStore() => ReadSource("src", "HitPan.API", "Services", "ChatFileStore.cs");
    private static string ChatDdl() => ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-101_chat.sql");

    // ═══════════════════════════════════════════════════════════════
    // 경계 — 사장님이 자르신 범위 (V11 · V12)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// V11 — 🔴 메신저가 <b>문서를 만들지 않는다.</b>
    /// 사장님: <i>"생성해서 만들고 결재까지 기능을 넣을 필요까진 없음. 연결까지만 해도 충분함"</i>
    /// </summary>
    [Fact]
    public void 메신저는_문서를_만들지_않는다()
    {
        var code = StripComments(ChatService());

        // 그룹웨어 문서 표에 INSERT 하면 안 된다 — 메신저는 길만 놓는다.
        string[] documentTables =
        {
            "approval_documents", "leave_requests", "hr_expense_requests",
            "expenses", "payroll_slips", "labor_contracts", "hr_reports"
        };

        foreach (var table in documentTables)
        {
            Assert.False(
                Regex.IsMatch(code, $@"INSERT\s+INTO\s+`?{table}`?", RegexOptions.IgnoreCase),
                $"메신저가 {table} 에 INSERT 하면 안 된다 — 사장님: \"연결까지만 해도 충분함\". " +
                "만드는 일은 원래 그 화면이 한다(규칙이 두 벌이 되면 갈라진다).");

            Assert.False(
                Regex.IsMatch(code, $@"UPDATE\s+`?{table}`?", RegexOptions.IgnoreCase),
                $"메신저가 {table} 을 UPDATE 하면 안 된다 — 메신저는 문서를 고치지 않는다.");
        }
    }

    /// <summary>
    /// V12 — 🔴 메신저 API 에 <b>결재를 태우는 주소가 없다.</b>
    /// </summary>
    [Fact]
    public void 메신저_API_에_결재_상신이나_승인이_없다()
    {
        var code = StripComments(ChatController());

        string[] forbidden = { "approve", "reject", "process", "submit" };

        foreach (var word in forbidden)
        {
            Assert.False(
                Regex.IsMatch(code, $@"\[Http(Post|Put|Patch)\(""[^""]*{word}", RegexOptions.IgnoreCase),
                $"메신저에 '{word}' 주소가 있으면 안 된다 — 결재는 결재 화면에서 한다.");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 열람 — 본인 대화만 (V1 · V2 · V3)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// V2 — 🔴 <b>부모계정도 남의 대화를 못 본다.</b>
    /// 사장님 결재: <i>"본인 대화만 열람"</i>.
    /// 급여(단계8)는 <c>tenant_admin</c> 바이패스가 있지만 <b>메신저에는 없어야 한다</b> —
    /// 급여는 줘야 하니까 사장이 다 보지만, 대화는 줄 것이 없다.
    /// </summary>
    [Fact]
    public void 부모계정_바이패스가_없다()
    {
        var service = StripComments(ChatService());
        var controller = StripComments(ChatController());

        foreach (var (name, code) in new[] { ("ChatService", service), ("ChatController", controller) })
        {
            Assert.False(code.Contains("tenant_admin", StringComparison.Ordinal),
                $"{name} 에 tenant_admin 판정이 있으면 안 된다 — " +
                "사장님이 \"본인 대화만 열람\" 을 고르셨다. 부모계정도 남의 1:1 은 못 본다.");
        }
    }

    /// <summary>
    /// V1 · V3 — 🔴 대화를 읽고 쓰기 전에 <b>방에 낀 사람인지</b> 본다.
    /// </summary>
    [Fact]
    public void 대화_접근은_방_참여를_먼저_본다()
    {
        var code = StripComments(ChatService());

        // 읽기·쓰기·파일 3곳 모두 관문을 지나야 한다.
        var gateCount = Regex.Matches(code, @"IsMemberAsync\(").Count;

        Assert.True(gateCount >= 4,
            $"방 참여 검사가 최소 4번(정의 1 + 읽기·쓰기·파일) 나와야 하는데 {gateCount} 번이다. " +
            "하나라도 빠지면 방 ID 를 주소에 넣어 남의 대화를 볼 수 있다.");
    }

    /// <summary>
    /// V25 — 🔴 <b>파일 내려받기도 방 참여를 본다.</b>
    /// 업로드만 막고 내려받기를 열어두면 "본인 대화만 열람"이 통째로 무너진다.
    /// </summary>
    [Fact]
    public void 파일_내려받기도_권한을_본다()
    {
        var code = StripComments(ChatService());

        var download = Regex.Match(code,
            @"DownloadFileAsync\([^)]*\)\s*\{(?:[^{}]|\{[^{}]*\})*\}",
            RegexOptions.Singleline);

        Assert.True(download.Success, "DownloadFileAsync 를 찾아야 한다");
        Assert.Contains("IsMemberAsync", download.Value, StringComparison.Ordinal);
    }

    /// <summary>V3 — 🔴 모든 쿼리가 <c>tenant_id</c> 로 걸러진다(헌법 #2).</summary>
    [Fact]
    public void 모든_쿼리가_테넌트로_걸러진다()
    {
        var code = StripComments(ChatService());

        // chat_* 표를 읽고 쓰는 문장은 전부 tenant 조건을 달아야 한다.
        var statements = Regex.Matches(code,
            @"(SELECT|UPDATE|INSERT INTO|DELETE FROM)\s+[^;""]*?chat_(rooms|room_members|messages|files)[^""]*?(?="""")",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        Assert.True(statements.Count > 0, "메신저 쿼리를 찾아야 한다");

        foreach (Match statement in statements)
        {
            var sql = statement.Value;

            // INSERT 는 컬럼 목록에 tenant_id 가 있으면 된다.
            Assert.True(
                sql.Contains("tenant_id", StringComparison.OrdinalIgnoreCase),
                $"테넌트 조건이 없는 메신저 쿼리가 있다(헌법 #2):\n{sql[..Math.Min(200, sql.Length)]}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 계정 — 계정 있는 사원만 (V4)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// V4 — 🔴 <b>계정 없는 사원은 초대할 수 없다.</b>
    /// 사장님(2026-08-12): <i>"메신저는 계정이 있는 사원에게만 권한을"</i>
    /// </summary>
    [Fact]
    public void 계정_없는_사원은_대화_대상이_아니다()
    {
        var code = StripComments(ChatService());

        var checks = Regex.Matches(code, @"user_id\s+IS\s+NOT\s+NULL", RegexOptions.IgnoreCase).Count;

        Assert.True(checks >= 3,
            $"계정 확인이 최소 3곳(부서방 채우기·초대 거르기·상대 목록)에 있어야 하는데 {checks} 곳이다. " +
            "빠지면 계정 없는 사원이 대화방에 들어가 아무것도 못 하는 자리가 된다.");
    }

    // ═══════════════════════════════════════════════════════════════
    // 문서 연결 — 제목만 (V5 · V13)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// V5 — 🔴 <b>문서 본문을 메시지에 복사하지 않는다.</b>
    /// 급여·경비 금액이 대화방에 복사본으로 남으면 <b>권한을 우회한다.</b>
    /// </summary>
    [Fact]
    public void 문서는_제목만_저장한다()
    {
        var ddl = ChatDdl();

        // 메시지 표에 금액·본문 컬럼이 생기면 안 된다.
        string[] forbidden = { "ref_amount", "ref_body", "ref_content", "ref_detail" };

        foreach (var column in forbidden)
        {
            Assert.False(ddl.Contains(column, StringComparison.OrdinalIgnoreCase),
                $"chat_messages 에 {column} 이 있으면 안 된다 — 문서는 참조만 한다. " +
                "본문을 복사하면 원본이 바뀔 때 틀린 내용이 남고, 금액이 권한을 우회한다.");
        }

        Assert.Contains("ref_title", ddl, StringComparison.Ordinal);
    }

    /// <summary>
    /// V13 — 🔴 <b>없는 문서는 붙일 수 없다.</b>
    /// 제목을 화면에서 받지 않고 <b>서버가 조회해서</b> 채운다.
    /// </summary>
    [Fact]
    public void 첨부_제목은_서버가_조회해서_채운다()
    {
        var dto = ReadSource("src", "HitPan.Application", "DTOs", "Chat", "ChatDtos.cs");
        var request = Regex.Match(StripComments(dto),
            @"class SendMessageRequest\s*\{(?:[^{}]|\{[^{}]*\})*\}", RegexOptions.Singleline);

        Assert.True(request.Success, "SendMessageRequest 를 찾아야 한다");

        // 화면이 제목을 보내면 아무 제목이나 붙일 수 있다.
        Assert.DoesNotContain("RefTitle", request.Value, StringComparison.Ordinal);

        // 서버가 조회해서 채운다.
        Assert.Contains("ResolveDocTitleAsync", StripComments(ChatService()), StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // 읽음 (V7)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// V7 — 🔴 <b>보낸 사람은 읽은 수에서 뺀다.</b>
    /// 안 그러면 아무도 안 읽어도 <c>1</c> 이 뜬다.
    /// </summary>
    [Fact]
    public void 읽은_수에서_보낸사람을_뺀다()
    {
        var code = StripComments(ChatService());

        Assert.Matches(@"rm\.employee_id\s*<>\s*msg\.sender_id", code);
    }

    /// <summary>🔴 읽음 전용 표를 만들지 않는다 — 100명 × 1000메시지 = 10만 줄이 된다.</summary>
    [Fact]
    public void 읽음은_별도_표를_두지_않는다()
    {
        var ddl = ChatDdl();

        Assert.False(
            Regex.IsMatch(ddl, @"CREATE TABLE[^;]*chat_(reads|message_reads|read_receipts)",
                RegexOptions.IgnoreCase),
            "읽음 전용 표를 만들면 안 된다 — last_read_at 한 컬럼으로 판정한다. " +
            "메시지 × 사람 표는 방 하나에 수만 줄이 된다.");

        Assert.Contains("last_read_at", ddl, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // 삭제 — 숨김만 (V6)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// V6 — 🔴 <b>메시지를 정말로 지우지 않는다.</b> 사장님 결재: <i>"삭제는 숨김만"</i>.
    /// 헌법 #3(원장 INSERT ONLY) 정신 — 주고받은 기록을 한쪽이 없앨 수 없다.
    /// </summary>
    [Fact]
    public void 메시지_삭제는_숨김만_한다()
    {
        var code = StripComments(ChatService());

        Assert.False(
            Regex.IsMatch(code, @"DELETE\s+FROM\s+`?chat_messages`?", RegexOptions.IgnoreCase),
            "chat_messages 를 DELETE 하면 안 된다 — 사장님 결재 \"삭제는 숨김만\". " +
            "원문은 남고 상대 화면에는 그대로 있어야 한다.");

        Assert.Contains("deleted_at = NOW(6)", code, StringComparison.Ordinal);
    }

    /// <summary>🔴 방을 나가도 참여자 줄을 지우지 않는다 — 나가기 전 대화는 계속 보여야 한다.</summary>
    [Fact]
    public void 방_나가기는_줄을_지우지_않는다()
    {
        var code = StripComments(ChatService());

        Assert.False(
            Regex.IsMatch(code, @"DELETE\s+FROM\s+`?chat_room_members`?", RegexOptions.IgnoreCase),
            "chat_room_members 를 DELETE 하면 안 된다 — left_at 만 찍는다.");
    }

    // ═══════════════════════════════════════════════════════════════
    // 파일 (V21 ~ V27)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// V22 · V23 — 🔴 <b>실행파일을 막는다. 이름을 바꿔도 막는다.</b>
    /// 사내라도 한 명이 감염되면 메신저가 전 직원에게 퍼뜨리는 통로가 된다.
    /// </summary>
    [Fact]
    public void 실행파일을_막는다()
    {
        var code = ChatFileStore();

        // 확장자
        foreach (var ext in new[] { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".dll", ".msi" })
        {
            Assert.Contains($"\"{ext}\"", code, StringComparison.OrdinalIgnoreCase);
        }

        // 🔴 시그니처 — 확장자만 보면 .exe 를 .xlsx 로 바꿔 통과한다.
        Assert.Contains("0x4D, 0x5A", code, StringComparison.Ordinal);   // MZ
        Assert.Contains("BlockedSignatures", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// V24 — 🔴 <b>저장 이름은 우리가 정한다.</b>
    /// 원래 이름을 쓰면 <c>..\..\</c> 경로 조작이 들어온다.
    /// </summary>
    [Fact]
    public void 저장_이름은_원래_이름을_쓰지_않는다()
    {
        var code = StripComments(ChatFileStore());

        // 파일명은 fileId 로 만든다.
        Assert.Matches(@"fileId\s*\+\s*safeExtension", code);

        // 저장 루트를 벗어나는지 확인한다.
        Assert.Contains("StartsWith(root", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// V21 · V27 — 🔴 <b>3중 한도.</b> 사장님: <i>"파일전송은 최소한으로"</i>
    /// 파일이 쌓이면 백업이 느려지고 → 업데이트 전 백업이 실패하고 → 업데이트가 차단된다.
    /// </summary>
    [Fact]
    public void 파일_한도가_3중이다()
    {
        var code = StripComments(ChatService());

        Assert.Contains("MaxFileMb", code, StringComparison.Ordinal);    // ① 한 개
        Assert.Contains("MaxRoomMb", code, StringComparison.Ordinal);    // ② 방
        Assert.Contains("MaxTenantMb", code, StringComparison.Ordinal);  // ③ 회사 전체
    }

    /// <summary>
    /// 🔴 사장님이 정하신 <b>20MB</b>. 100MB 를 <b>알고 접으셨다</b> —
    /// <i>"통신이 안정적이지 않다면 안되는걸로 생각하자"</i>
    /// </summary>
    [Fact]
    public void 파일_기본_한도는_20MB_다()
    {
        var ddl = ChatDdl();

        Assert.Matches(@"`max_file_mb`\s+int\(11\)\s+NOT NULL DEFAULT 20\b", ddl);
        Assert.Matches(@"`max_room_mb`\s+int\(11\)\s+NOT NULL DEFAULT 500\b", ddl);
        Assert.Matches(@"`max_tenant_mb`\s+int\(11\)\s+NOT NULL DEFAULT 5120\b", ddl);
    }

    /// <summary>
    /// 🔴 <b>조각 업로드를 만들지 않는다.</b> 사장님이 100MB 를 접으신 근거다 —
    /// 되는 것처럼 보이다가 느린 날 90% 에서 터진다.
    /// </summary>
    [Fact]
    public void 조각_업로드를_만들지_않는다()
    {
        var service = StripComments(ChatService());
        var store = StripComments(ChatFileStore());
        var controller = StripComments(ChatController());

        string[] forbidden = { "chunk", "resumable", "partNumber", "uploadId" };

        foreach (var (name, code) in new[] { ("ChatService", service), ("ChatFileStore", store), ("ChatController", controller) })
        {
            foreach (var word in forbidden)
            {
                Assert.False(code.Contains(word, StringComparison.OrdinalIgnoreCase),
                    $"{name} 에 '{word}' 가 있으면 안 된다 — 사장님이 100MB 를 알고 접으셨다: " +
                    "\"통신이 안정적이지 않다면 안되는걸로 생각하자\".");
            }
        }
    }

    /// <summary>
    /// V28 — 🔴 <b>파일 한도가 업무를 막으면 안 된다.</b>
    /// 한도 검사는 파일 보내기에만 있어야 한다.
    /// </summary>
    [Fact]
    public void 파일_한도는_업무를_막지_않는다()
    {
        var code = StripComments(ChatService());

        // 한도 검사는 SendFileAsync 안에만 있다.
        var sendFile = Regex.Match(code,
            @"SendFileAsync\((?:[^)]|\n)*?\)(?:[^{]|\n)*?\{(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*\}",
            RegexOptions.Singleline);

        Assert.True(sendFile.Success, "SendFileAsync 를 찾아야 한다");
        Assert.Contains("MaxTenantMb", sendFile.Value, StringComparison.Ordinal);

        // 메시지 보내기에는 한도가 없다 — 글은 용량과 무관하다.
        var sendMessage = Regex.Match(code,
            @"SendMessageAsync\((?:[^)]|\n)*?\)(?:[^{]|\n)*?\{(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*\}",
            RegexOptions.Singleline);

        Assert.True(sendMessage.Success, "SendMessageAsync 를 찾아야 한다");
        Assert.DoesNotContain("MaxTenantMb", sendMessage.Value, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // 결재 결과 메시지 (V14 ~ V20)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// V14 · V15 — 🔴 <b>승인·반려가 신청자에게 간다.</b>
    /// 사장님: <i>"승인 혹은 반려시, 최초 발신인(신청자)에게 메시지 보내야됨"</i> /
    /// <i>"반려되었습니다. 혹은 승인되었습니다."</i>
    /// </summary>
    [Fact]
    public void 결재_결과가_신청자에게_간다()
    {
        var code = ReadSource("src", "HitPan.API", "Controllers", "ApprovalController.cs");

        Assert.Contains("SendApprovalMessageAsync", code, StringComparison.Ordinal);
        Assert.Contains("승인되었습니다.", code, StringComparison.Ordinal);
        Assert.Contains("반려되었습니다.", code, StringComparison.Ordinal);

        // 🔴 받는 사람은 기안자다.
        Assert.Contains("RequesterId", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// V19 — 🔴 <b>메시지 발송 실패가 결재를 막으면 안 된다.</b>
    /// 결재는 이미 저장됐는데 메시지 때문에 되돌리면 그게 더 큰 사고다(헌법 #15).
    /// </summary>
    [Fact]
    public void 메시지_실패가_결재를_막지_않는다()
    {
        var code = ReadSource("src", "HitPan.API", "Controllers", "ApprovalController.cs");

        // 메서드 정의부터 파일 끝까지를 본다(중첩 괄호 정규식은 깨지기 쉽다).
        var definition = code.IndexOf("private async Task NotifyRequesterAsync",
            StringComparison.Ordinal);
        Assert.True(definition > 0, "NotifyRequesterAsync 정의를 찾아야 한다");

        var body = code[definition..];
        var end = body.IndexOf("private async Task NotifyNextApproverAsync", StringComparison.Ordinal);
        if (end > 0) body = body[..end];

        Assert.Contains("catch", body, StringComparison.Ordinal);
        Assert.Contains("LogWarning", body, StringComparison.Ordinal);   // 헌법 #15 — 빈 catch 금지

        // 상신 알림도 같아야 한다 — 실패해도 상신은 그대로다.
        var next = code.IndexOf("private async Task NotifyNextApproverAsync", StringComparison.Ordinal);
        Assert.True(next > 0, "NotifyNextApproverAsync 정의를 찾아야 한다");
        Assert.Contains("LogWarning", code[next..], StringComparison.Ordinal);
    }

    /// <summary>
    /// V16 — 🔴 <b>메시지인지 결재안내인지 갈린다.</b>
    /// 사장님: <i>"결재봇, 메시지봇 공유해도 될듯. 인스타나 페이스북 처럼.
    /// 다만 메시지인지, 결재안내인지는 안내"</i>
    /// </summary>
    [Fact]
    public void 결재와_메시지가_딱지로_갈린다()
    {
        Assert.Contains("msg_kind", ChatDdl(), StringComparison.Ordinal);

        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "ChatPage.razor");
        Assert.Contains("\"결재\"", page, StringComparison.Ordinal);
        Assert.Contains("\"메시지\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// V20 — 🔴 <b>알림과 메시지를 둘 다 보내지 않는다.</b>
    /// 같은 소식이 두 번 오면 직원이 헷갈린다.
    /// </summary>
    [Fact]
    public void 결재_결과를_알림으로_또_보내지_않는다()
    {
        var code = StripComments(ReadSource("src", "HitPan.API", "Controllers", "ApprovalController.cs"));

        Assert.False(code.Contains("INotificationService", StringComparison.Ordinal),
            "ApprovalController 가 알림 서비스를 직접 부르면 안 된다 — " +
            "결재 결과는 메신저 메시지 하나로 간다(사장님 지시). 알림까지 보내면 두 번 온다.");
    }

    // ═══════════════════════════════════════════════════════════════
    // 방 (V8 · V10)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// V10 — 🔴 <b>1:1 은 두 번 생기지 않는다.</b> A→B 와 B→A 가 같은 방이어야 한다.
    /// </summary>
    [Fact]
    public void 일대일_방은_두_번_생기지_않는다()
    {
        Assert.Matches(@"UNIQUE KEY\s+`uq_chat_direct`\s+\(`tenant_id`,`direct_key`\)", ChatDdl());

        var code = StripComments(ChatService());
        Assert.Contains("OrderBy(x => x, StringComparer.Ordinal)", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// V8 — 🔴 <b>부서방을 자동으로 만들지 않는다.</b>
    /// 사장님: <i>"부서는 고객사가 설정할 일"</i> · 반자동 원칙.
    /// 부서가 0건이어도 1:1·단체는 돌아가야 한다.
    /// </summary>
    [Fact]
    public void 부서방을_자동으로_만들지_않는다()
    {
        var department = ReadSource("src", "HitPan.API", "Controllers", "DepartmentController.cs");

        Assert.False(department.Contains("chat_rooms", StringComparison.OrdinalIgnoreCase),
            "부서를 만들 때 대화방을 자동 생성하면 안 된다 — 안 쓰는 방이 부서 수만큼 쌓이고, " +
            "부서 개편 때 그 방을 어떻게 할지 아무도 모른다. 사람이 만든다(반자동 원칙).");
    }

    // ═══════════════════════════════════════════════════════════════
    // 표 구조
    // ═══════════════════════════════════════════════════════════════

    /// <summary>🔴 헌법 #17 · Collation 통일 · 출하 DDL 동반(#36).</summary>
    [Fact]
    public void DDL_이_헌법을_지킨다()
    {
        var ddl = ChatDdl();

        var tableCount = Regex.Matches(ddl, @"CREATE TABLE IF NOT EXISTS", RegexOptions.IgnoreCase).Count;
        var engineCount = Regex.Matches(ddl, @"ENGINE=InnoDB", RegexOptions.IgnoreCase).Count;
        var collationCount = Regex.Matches(ddl, @"COLLATE=utf8mb4_unicode_ci", RegexOptions.IgnoreCase).Count;

        Assert.Equal(tableCount, engineCount);       // 헌법 #17
        Assert.Equal(tableCount, collationCount);    // Collation 통일

        // 헌법 #36 — 출하 DDL 이 신규설치 단일 진실원이다.
        var clean = ReadSource("installer", "hitpan_db_clean.sql");
        foreach (var table in new[] { "chat_rooms", "chat_room_members", "chat_messages", "chat_files", "chat_file_settings" })
        {
            Assert.Contains($"CREATE TABLE `{table}`", clean, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 🔴 마이그는 <b>고객에게 가는 자리</b>에만 둔다.
    /// [[project_migration_real_path]] — 다른 자리에 두면 배포본에 안 실린다.
    /// </summary>
    [Fact]
    public void 마이그가_제_자리에_있다()
    {
        var path = Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL", "DB-101_chat.sql");
        Assert.True(File.Exists(path),
            "메신저 마이그는 src/HitPan.API/Migrations/SQL/ 에 있어야 한다 — " +
            "다른 자리에 두면 배포본에 안 실려 고객 화면이 죽는다(2026-08-12 실제 사고).");
    }
}
