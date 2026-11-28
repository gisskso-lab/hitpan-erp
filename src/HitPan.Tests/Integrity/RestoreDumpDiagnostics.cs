using System.Text;
using HitPan.Application.Services;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2② 고침 2 — <b>시험 전용 진단</b>: 변환기(걸음 ③ 첫 바퀴)가 덤프의 어느 문장에서 왜 멈췄나를 실패 글에 싣는다.
/// </summary>
/// <remarks>
/// <para>CI 런 46113509650 의 G14 · G3 실패 글은 「stage_failed 변환기 멈춤」 뿐이라 멈춘 줄 꼴을 알 수 없었다(제품은 덤프 내용을 응답 · 로그에 싣지 않음 · #18 · #22).
/// 이 도우미는 <b>시험이 이미 쥔 시험 DB 덤프</b>(고객 자료 0)를 제품 변환기에 한 번 더 태워 ① 단계(1차 변환기 / 2차 검사) ② 제품 사유 글
/// ③ 멈춘 자리 앞 80 바이트 ④ 그 문장 머리 80 바이트를 돌려준다. 제품 코드 손 0 — 응답 · 로그 · 화면 글은 그대로.</para>
/// <para>멈춘 자리 = 한 번에 1 바이트만 내주는 흐름으로 감싸 「나눔기가 읽은 바이트 수」 를 센다(BufferedStream 이 더 읽어 두지 못함).
/// 변환기는 나눔기가 구분자를 읽은 직후 그 문장을 보므로, 멈춘 자리 = 멈춘 문장의 구분자 바로 뒤(나눔기 끝 검사면 덤프 끝).</para>
/// </remarks>
internal static class RestoreDumpDiagnostics
{
    /// <summary>제품 <c>RestoreSafePath.DecideTable</c> 과 같은 판정(시험 쪽 복사 · 판 비교 표 = 부음 · 분류 = 부음/건너뜀 · 나머지 = 멈춤).</summary>
    public static RestoreStageDecision ProductLikeDecide(string name)
    {
        if (RestoreSafePath.VersionCompareTables.Contains(name, StringComparer.OrdinalIgnoreCase)) return RestoreStageDecision.Stage;
        return RestoreTableClassification.Classify(name) switch
        {
            RestoreTableClass.FromBackup => RestoreStageDecision.Stage,
            RestoreTableClass.KeepCurrent => RestoreStageDecision.Skip,
            _ => RestoreStageDecision.Stop,
        };
    }

    /// <summary>덤프 바이트를 변환기 + 2차 검사에 태워 멈춘 곳을 글로 돌려준다(멈추지 않으면 「멈춤 재현 0」).</summary>
    public static string Describe(byte[] dump, Func<string, RestoreStageDecision> decide)
    {
        using var src = new OneByteStream(dump);
        var stage = "1차 변환기";
        var count = 0;
        try
        {
            foreach (var s in RestoreDumpConverter.Convert(src, decide))
            {
                stage = "2차 검사(RestoreStageOutputCheck)";
                RestoreStageOutputCheck.Verify(s.Sql);
                stage = "1차 변환기";
                count++;
            }
            return $"[진단] 멈춤 재현 0(나온 문장 {count} · 덤프 {dump.Length} 바이트)";
        }
        catch (RestoreDumpRejectedException ex)
        {
            var at = (int)Math.Min(src.Position, dump.Length);
            var head = StatementHeadBefore(dump, at);
            return $"[진단] 단계={stage} · 사유=「{ex.Message}」 · 앞서 나온 문장 {count} · 멈춘 자리 {at}/{dump.Length} 바이트"
                 + $" · 멈춘 자리 앞 80=「{Show(dump, Math.Max(0, at - 80), Math.Min(80, at))}」"
                 + $" · 그 문장 머리 80=「{Show(dump, head, Math.Min(80, Math.Max(0, at - head)))}」";
        }
    }

    /// <summary>열린 손잡이를 처음부터 다 읽어 <see cref="Describe(byte[], Func{string, RestoreStageDecision})"/>(손잡이 자리는 0 으로 되돌림).</summary>
    public static string Describe(Stream dump, Func<string, RestoreStageDecision> decide)
    {
        dump.Position = 0;
        using var ms = new MemoryStream();
        dump.CopyTo(ms);
        dump.Position = 0;
        return Describe(ms.ToArray(), decide);
    }

    // 멈춘 자리 바로 앞 구분자(;)와 그 앞 구분자 사이 = 멈춘 문장 — 앞 구분자 뒤 공백 · 주석 줄(-- …)은 건너뛰어 문장 머리를 잡는다(진단용 어림)
    private static int StatementHeadBefore(byte[] d, int at)
    {
        var end = at - 1;
        while (end > 0 && d[end] is (byte)';' or (byte)'\n' or (byte)'\r' or (byte)' ') end--;
        var i = end;
        while (i > 0 && d[i - 1] != (byte)';') i--;
        while (i < end)
        {
            if (d[i] is (byte)'\n' or (byte)'\r' or (byte)' ' or (byte)'\t') { i++; continue; }
            if (d[i] == (byte)'-' && i + 1 < d.Length && d[i + 1] == (byte)'-')
            {
                while (i < end && d[i] != (byte)'\n') i++;
                continue;
            }
            break;
        }
        return i;
    }

    // 보이는 글로 — 줄바꿈 = \n · 탭 = \t · 제어 바이트 = \xNN · 그 밖 바이트는 UTF-8 로(깨진 바이트 = �)
    private static string Show(byte[] d, int start, int len)
    {
        if (len <= 0) return "";
        var text = Encoding.UTF8.GetString(d, start, len);
        var sb = new StringBuilder(text.Length + 16);
        foreach (var ch in text)
        {
            if (ch == '\n') sb.Append("\\n");
            else if (ch == '\r') sb.Append("\\r");
            else if (ch == '\t') sb.Append("\\t");
            else if (ch < 0x20 || ch == 0x7F) sb.Append($"\\x{(int)ch:X2}");
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>읽기마다 1 바이트만 내준다 — 나눔기가 실제로 읽은 바이트 수 = <see cref="Position"/>.</summary>
    private sealed class OneByteStream(byte[] data) : Stream
    {
        private long _pos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0 || _pos >= data.Length) return 0;
            buffer[offset] = data[_pos++];
            return 1;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
