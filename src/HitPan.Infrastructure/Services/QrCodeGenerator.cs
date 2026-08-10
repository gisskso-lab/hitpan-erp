using System.Text;
using QRCoder;

namespace HitPan.Infrastructure.Services;

/// <summary>
/// QR 코드 생성기 (20260811작1 (D)).
///
/// ■ 왜 라이브러리(QRCoder)인가 — 사장님 결재 2026-08-11
///   사장님 지시는 "무료 API로 쓸수 있다면 쓰고, 아니면 직접 생성기 만들어" 였다.
///   → 먼저 **직접 구현했다.** ISO/IEC 18004 대로 짜서 화면에 QR 모양이 제대로 나왔다.
///     그런데 독립 디코더(jsQR)로 확인하니 **안 읽혔다.** 검증된 구현과 한 칸씩 대조하니
///     21×21 = 441칸 중 122칸이 달랐다. 포맷 비트 배치와 예약 영역을 고쳐도 그대로였다.
///   → 사장님께 보고 후 (나) 결재: 검증된 라이브러리를 쓴다.
///
///   ⚠️ 이것은 "외부 API" 가 아니다 — 우리 프로그램 안에 들어오는 코드이고 **인터넷을 쓰지 않는다.**
///     QR 에 담기는 등록 토큰이 밖으로 나가지 않으므로 데이터 주권 원칙(헌법 #18·#22·#30)에 어긋나지 않는다.
///     고객 사무실이 인터넷과 끊겨도 사내에서 QR 이 뜬다.
///
/// ■ 왜 눈으로만 보고 넘기면 안 되는가 (교훈)
///   자체 구현본은 **화면상 완벽한 QR 이었다.** 위치 검출 패턴·타이밍·정렬 패턴이 다 제자리였다.
///   폰으로 찍어야만 드러나는 결함이었다. "그려졌다" 와 "읽힌다" 는 다르다 —
///   QR 을 손대면 반드시 독립 디코더로 확인한다.
/// </summary>
public static class QrCodeGenerator
{
    /// <summary>
    /// 문자열을 QR 모듈 격자로 만든다. true = 검은 칸.
    /// 오류정정 레벨 M — 25% 손상까지 복원. 화면에 띄우는 용도라 이 정도면 충분하고,
    /// 레벨을 올리면 QR 이 조밀해져 오히려 찍기 어려워진다.
    /// </summary>
    public static bool[,] Encode(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

        var modules = data.ModuleMatrix;
        int size = modules.Count;

        // QRCoder 는 BitArray 목록(행 우선)으로 준다. 우리 규약은 [x, y] 이므로 뒤집어 담는다.
        var result = new bool[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                result[x, y] = modules[y][x];

        return result;
    }

    /// <summary>
    /// QR 격자를 PNG 로 만들어 data URI 로 돌려준다 — 화면에서 그대로 &lt;img src&gt; 에 넣는다.
    ///
    /// PNG 를 직접 조립한다(이미지 라이브러리 의존 0). 이 부분은 자체 구현본에서
    /// 정상 동작이 확인된 코드라 그대로 살렸다 — 디코딩 실패의 원인은 격자 생성 쪽이었다.
    /// </summary>
    /// <param name="matrix">QR 격자</param>
    /// <param name="scale">모듈 하나를 몇 픽셀로 그릴지</param>
    /// <param name="quietZone">테두리 여백(모듈 수). 규격 권장은 4 — 이게 없으면 스캐너가 QR 을 못 찾는다.</param>
    public static string ToPngDataUri(bool[,] matrix, int scale = 8, int quietZone = 4)
    {
        int modules = matrix.GetLength(0);
        int size = (modules + quietZone * 2) * scale;

        var raw = new byte[size * (size + 1)];
        int p = 0;
        for (int y = 0; y < size; y++)
        {
            raw[p++] = 0;                                   // 필터 타입: None
            for (int x = 0; x < size; x++)
            {
                int mx = x / scale - quietZone;
                int my = y / scale - quietZone;
                bool dark = mx >= 0 && my >= 0 && mx < modules && my < modules && matrix[mx, my];
                raw[p++] = dark ? (byte)0 : (byte)255;
            }
        }

        var idat = Deflate(raw);

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });   // PNG 시그니처

        var ihdr = new List<byte>();
        ihdr.AddRange(BeInt(size));
        ihdr.AddRange(BeInt(size));
        ihdr.AddRange(new byte[] { 8, 0, 0, 0, 0 });                 // 8비트 회색조
        WriteChunk(ms, "IHDR", ihdr.ToArray());
        WriteChunk(ms, "IDAT", idat);
        WriteChunk(ms, "IEND", Array.Empty<byte>());

        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>zlib 컨테이너 + 무압축 deflate 블록. 압축률은 포기하고 정확성을 취한다.</summary>
    private static byte[] Deflate(byte[] data)
    {
        var outBytes = new List<byte> { 0x78, 0x01 };
        const int maxBlock = 65535;
        for (int i = 0; i < data.Length; i += maxBlock)
        {
            int len = Math.Min(maxBlock, data.Length - i);
            bool last = i + len >= data.Length;
            outBytes.Add(last ? (byte)1 : (byte)0);
            outBytes.Add((byte)(len & 0xFF));
            outBytes.Add((byte)((len >> 8) & 0xFF));
            outBytes.Add((byte)(~len & 0xFF));
            outBytes.Add((byte)((~len >> 8) & 0xFF));
            for (int j = 0; j < len; j++) outBytes.Add(data[i + j]);
        }
        uint a = 1, b = 0;
        foreach (var by in data) { a = (a + by) % 65521; b = (b + a) % 65521; }
        outBytes.AddRange(BeInt((int)((b << 16) | a)));
        return outBytes.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        s.Write(BeInt(data.Length));
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crcInput = new byte[typeBytes.Length + data.Length];
        Array.Copy(typeBytes, crcInput, typeBytes.Length);
        Array.Copy(data, 0, crcInput, typeBytes.Length, data.Length);
        s.Write(BeInt((int)Crc32(crcInput)));
    }

    private static byte[] BeInt(int v) =>
        new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
