namespace HitPan.Web.Services;

/// <summary>
/// 서버 실패 응답에서 <b>사람이 읽을 문장</b>만 꺼낸다.
/// </summary>
/// <remarks>
/// 🔴 20260827작8 W3 — 1.3.28 실측 반려의 원인이 여기다.
///
/// <para>
/// 서버 가드는 <c>"매입명세서를 삭제할 수 없습니다. 확정된 반품전표(매반-…)가 연결돼 있습니다"</c>
/// 처럼 <b>전표번호가 실린 문장</b>을 정확히 만들어 보낸다. 그런데 화면이 그 본문을
/// 읽지 않고 <c>"삭제에 실패했습니다"</c> 로 덮어써서 사장님께 도달하지 못했다.
/// 사장님이 요구한 <i>"틀린 데이터를 빠르게 발견"</i> 은 <b>번호가 보여야</b> 성립한다.
/// </para>
///
/// <para>
/// 원래 <c>PurchaseReturnList</c> 안에 private 으로 있던 것을 끌어올렸다.
/// 한 화면에만 있으니 <b>나머지 화면이 각자 다르게</b> 처리했고, 그게 이번 사고다.
/// </para>
/// </remarks>
public static class ApiErrorText
{
    /// <summary>
    /// 실패 본문 → 표시 문장. 서버는 <c>{"message":"…"}</c> 또는 <c>{"error":"…"}</c> 로 준다.
    /// </summary>
    /// <param name="body">응답 본문</param>
    /// <param name="statusCode">HTTP 상태(본문이 비었을 때 대체 표시용)</param>
    public static string Extract(string? body, int? statusCode = null)
    {
        if (string.IsNullOrWhiteSpace(body))
            return statusCode is null ? "알 수 없는 오류" : $"서버 오류 ({statusCode})";

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var key in new[] { "message", "error", "title", "detail" })
                {
                    if (doc.RootElement.TryGetProperty(key, out var v)
                        && v.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s!.Trim();
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // JSON 이 아니면 원문을 쓴다 — 아무것도 안 보여주는 것보다 낫다.
        }

        // 🔴 파싱 실패 본문이 JSON 모양이면 그대로 뿌리지 않는다.
        //    고객 화면에 개발 흔적이 보이면 안 된다(헌법 #23 계열).
        var raw = body.Trim();
        if (raw.StartsWith('{') || raw.StartsWith('['))
            return statusCode is null ? "요청을 처리하지 못했습니다." : $"요청을 처리하지 못했습니다. ({statusCode})";

        return raw.Length > 200 ? raw[..200] : raw;
    }
}
