using System.Text;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261015작B1 갈래 D · 조건2 — 소스 숫자를 세는 게이트가 <b>실행되는 코드만</b> 보게 하는 공용 벗기기.
///
/// <para><b>왜 있나</b>: 「주석을 세는 자리」는 이 트랙 6번째 재발이다. <c>File.ReadAllText</c> 원문을
/// 그대로 세면 <b>주석·문자열 리터럴</b>에 같은 글자가 있을 때 게이트가 거짓으로 판정한다
/// (봉합을 끝낸 코드가 빨강이 되거나, 되돌려진 코드가 초록이 된다).</para>
///
/// <para><b>왜 정규식 한 줄이 아닌가</b>: <c>Regex.Replace(src, "//[^\n]*", "")</c> 는
/// ① 블록주석 <c>/* */</c> 을 못 다루고 ② <b>문자열 안의</b> <c>"http://x"</c> 를 주석으로 오인해
/// 그 줄의 뒤를 통째로 날린다(= 뒤에 있던 실코드까지 안 세어진다). 그래서 한 글자씩 훑는
/// 상태기계로 쓴다 — 문자열 안의 <c>//</c>, 주석 안의 <c>"</c> 를 둘 다 틀리지 않게.</para>
///
/// <para><b>본인 게이트</b>: <see cref="SourceTextStripGateTests"/> — 이 벗기기가 틀리면 위 게이트 전부가
/// 조용히 거짓이 되므로, 벗기기 자체에 자물쇠를 둔다.</para>
///
/// <para><b>줄 수는 보존한다</b>: 지워진 주석·리터럴 안의 줄바꿈은 그대로 내보내므로
/// 결과의 줄 번호가 원본과 1:1 로 맞는다(게이트가 위반 줄 번호를 보고할 수 있게).</para>
/// </summary>
internal static class SourceText
{
    /// <summary>
    /// 주석(<c>//</c> · <c>/* */</c>)만 떼고 <b>문자열 리터럴은 원문 그대로</b> 남긴다.
    /// 리터럴 <i>속 글자</i>를 세야 하는 자리(예: <c>_config["Smtp:Host"]</c> 가 코드에 있나)에 쓴다.
    /// </summary>
    public static string StripComments(string src) => Scan(src, keepLiteralContents: true);

    /// <summary>
    /// 주석 + <b>문자열·보간 문자열·축자 문자열·원시 문자열·문자 리터럴의 내용</b>까지 비운 「실행코드」.
    /// 구분자(<c>""</c> · <c>''</c>)는 남기므로 <c>_config[""]</c> 같은 호출 <i>모양</i>은 유지된다.
    /// 코드 토큰을 세야 하는 자리(예: <c>RecordAsync(</c> 가 몇 곳인가)에 쓴다.
    /// </summary>
    public static string ExecutableCode(string src) => Scan(src, keepLiteralContents: false);

    // ══════════════════════════════════════════════════════════════════════
    // 상태기계 — 한 글자씩
    // ══════════════════════════════════════════════════════════════════════

    private static string Scan(string src, bool keepLiteralContents)
    {
        ArgumentNullException.ThrowIfNull(src);

        var outp = new StringBuilder(src.Length);
        var i = 0;
        var n = src.Length;

        while (i < n)
        {
            var c = src[i];

            // ① 줄주석 — 줄바꿈은 지우지 않는다(다음 바퀴에서 코드로 복사된다).
            if (c == '/' && i + 1 < n && src[i + 1] == '/')
            {
                i += 2;
                while (i < n && src[i] != '\n') i++;
                continue;
            }

            // ② 블록주석 — 여러 줄에 걸칠 수 있다. 안의 `"` 는 문자열이 아니다.
            if (c == '/' && i + 1 < n && src[i + 1] == '*')
            {
                i += 2;
                while (i < n && !(src[i] == '*' && i + 1 < n && src[i + 1] == '/'))
                {
                    if (src[i] == '\n') outp.Append('\n');
                    i++;
                }
                i = Math.Min(i + 2, n);
                continue;
            }

            // ③ 문자열 계열 — `"` · `$"` · `@"` · `$@"` · `"""` 모두 여기서 걸린다.
            if (TryStringStart(src, i, out var prefixLen, out var verbatim, out var quoteRun))
            {
                i = SkipString(src, i, prefixLen, verbatim, quoteRun, keepLiteralContents, outp);
                continue;
            }

            // ④ 문자 리터럴 — `'"'` 안의 따옴표를 문자열 시작으로 오인하지 않게 여기서 먹는다.
            if (c == '\'')
            {
                i = SkipCharLiteral(src, i, keepLiteralContents, outp);
                continue;
            }

            outp.Append(c);
            i++;
        }

        return outp.ToString();
    }

    /// <summary>
    /// <paramref name="i"/> 가 문자열 리터럴의 시작인가. 접두사 <c>$</c>·<c>@</c>(순서 무관, <c>$$</c> 포함)를
    /// 먹고 바로 <c>"</c> 가 와야 참이다. 축자 식별자(<c>@class</c>)는 따옴표가 없으니 걸리지 않는다.
    /// </summary>
    private static bool TryStringStart(string src, int i, out int prefixLen, out bool verbatim, out int quoteRun)
    {
        prefixLen = 0;
        verbatim = false;
        quoteRun = 0;

        var j = i;
        while (j < src.Length && (src[j] == '$' || src[j] == '@'))
        {
            if (src[j] == '@') verbatim = true;
            j++;
        }

        if (j >= src.Length || src[j] != '"') return false;

        prefixLen = j - i;

        var k = j;
        while (k < src.Length && src[k] == '"') k++;
        quoteRun = k - j;
        return true;
    }

    /// <summary>여는 자리부터 닫는 구분자 다음까지 먹고, 남길 모양만 내보낸다. 반환 = 다음 인덱스.</summary>
    private static int SkipString(string src, int i, int prefixLen, bool verbatim, int quoteRun,
                                  bool keepLiteralContents, StringBuilder outp)
    {
        var n = src.Length;
        int end;

        if (!verbatim && quoteRun >= 3)
        {
            // 원시 문자열 `"""…"""` — 여는 따옴표 개수와 같은(이상) 개수로 닫는다. 안의 `\` 는 탈출이 아니다.
            var j = i + prefixLen + quoteRun;
            end = n;
            while (j < n)
            {
                if (src[j] != '"') { j++; continue; }

                var run = 0;
                while (j + run < n && src[j + run] == '"') run++;
                if (run >= quoteRun) { end = j + quoteRun; break; }
                j += run;
            }
        }
        else if (verbatim)
        {
            // 축자 `@"…"` — 안에서 `""` 는 따옴표 한 개이고, `\` 는 탈출이 아니다.
            var j = i + prefixLen + 1;
            end = n;
            while (j < n)
            {
                if (src[j] != '"') { j++; continue; }
                if (j + 1 < n && src[j + 1] == '"') { j += 2; continue; }
                end = j + 1;
                break;
            }
        }
        else
        {
            // 일반/보간 `"…"` · `$"…"` — `\"` 는 탈출. 줄바꿈을 못 넘는다.
            var j = i + prefixLen + 1;
            end = n;
            while (j < n)
            {
                var c = src[j];
                if (c == '\\') { j += 2; continue; }
                if (c == '"') { end = j + 1; break; }
                if (c == '\n') { end = j; break; }   // 깨진 소스 — 거기서 끊는다
                j++;
            }
        }

        Emit(src, i, end, keepLiteralContents, outp, '"');
        return end;
    }

    private static int SkipCharLiteral(string src, int i, bool keepLiteralContents, StringBuilder outp)
    {
        var n = src.Length;
        var j = i + 1;
        var end = n;
        while (j < n)
        {
            var c = src[j];
            if (c == '\\') { j += 2; continue; }
            if (c == '\'') { end = j + 1; break; }
            if (c == '\n') { end = j; break; }
            j++;
        }

        Emit(src, i, end, keepLiteralContents, outp, '\'');
        return end;
    }

    /// <summary>원문 유지면 그대로, 아니면 구분자 2개 + 안에 있던 줄바꿈만 내보낸다(줄 번호 보존).</summary>
    private static void Emit(string src, int start, int end, bool keepLiteralContents,
                             StringBuilder outp, char delimiter)
    {
        if (keepLiteralContents)
        {
            outp.Append(src, start, end - start);
            return;
        }

        outp.Append(delimiter).Append(delimiter);
        for (var k = start; k < end; k++)
            if (src[k] == '\n') outp.Append('\n');
    }
}
