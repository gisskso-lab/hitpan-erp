// W-G1-51 갈래 Z — 원시 바이트 이름 훑기 (시험 쪽 · 제품 h4 와 따로 짠 것)
//   쓰임 ⑴ 결과 PDF · 정규화 사본에 능동 내용 이름이 있나(설계 §7-2 ⓔ 「판독기로」)
//        ⑵ ⑨ 표본 유효 조건 — 원시 바이트 글자 확인(E7 계열 `/Encrypt` 부분 글자 0 · U1 `trailer` · `XRef` 0 · E8 · P2 `trailer` 0)
//   🔴 이 함수는 ⑨ 의 독립 기준(⒜ 만드는 법 · ⒞ qpdf 명령)이 아니다 — 기록 · 유효 조건용. 제품 h4 와 같은 코드를 쓰면
//      제품 결함을 시험이 같이 놓치므로 제품 코드를 참조하지 않는다(새로 짬 · 문법 = 설계 §7-2 ㉮-h4 (가)~(라) 글자).
using System.Text;

namespace PrintProbe;

public static class NameScan
{
    private static bool IsWhite(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;
    private static bool IsDelim(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static int Hex(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };

    /// <summary>파일 안 모든 `/` 에서 이름을 끊고(문맥 무시) `#hh` 를 푼 이름들을 센다.</summary>
    public static Dictionary<string, int> DecodedNames(ReadOnlySpan<byte> data)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var i = 0;
        while (i < data.Length)
        {
            if (data[i] != (byte)'/') { i++; continue; }
            // (가) 시작 = 모든 `/` · (나) 끝 = 공백 6종 + 구분자(끝 글자 `/` 는 먹지 않음)
            var start = i + 1;
            var end = start;
            while (end < data.Length && !IsWhite(data[end]) && !IsDelim(data[end])) end++;
            var token = data[start..end];
            // (라) 끊은 뒤 해독
            var decoded = new List<byte>(token.Length);
            for (var k = 0; k < token.Length; k++)
            {
                // (다) `#` + 16진 두 자리(대 · 소문자) = 그 바이트 · 잘못된 `#` = 글자 그대로
                if (token[k] == (byte)'#' && k + 2 < token.Length)
                {
                    var h1 = Hex(token[k + 1]);
                    var h2 = Hex(token[k + 2]);
                    if (h1 >= 0 && h2 >= 0)
                    {
                        decoded.Add((byte)((h1 << 4) | h2));
                        k += 2;
                        continue;
                    }
                }
                decoded.Add(token[k]);
            }
            var name = Encoding.Latin1.GetString(decoded.ToArray());
            counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
            i = end;
        }
        return counts;
    }

    /// <summary>원시 바이트에 글자 그대로(풀기 없이) 있는 횟수.</summary>
    public static int RawCount(ReadOnlySpan<byte> data, string ascii)
    {
        var needle = Encoding.ASCII.GetBytes(ascii);
        var n = 0;
        var i = 0;
        while (i < data.Length)
        {
            var at = data[i..].IndexOf(needle);
            if (at < 0) break;
            n++;
            i += at + 1;
        }
        return n;
    }

    /// <summary>능동 내용 이름(설계 §7-2 ⓔ) — 풀린 이름 기준.</summary>
    public static readonly string[] ActiveNames = { "JavaScript", "JS", "Launch", "EmbeddedFile", "EmbeddedFiles", "AA", "OpenAction", "Annots", "RichMedia", "SubmitForm", "ImportData" };

    public static Dictionary<string, int> ActiveContent(ReadOnlySpan<byte> data)
    {
        var all = DecodedNames(data);
        var hit = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in ActiveNames)
            if (all.TryGetValue(n, out var c)) hit[n] = c;
        return hit;
    }
}
