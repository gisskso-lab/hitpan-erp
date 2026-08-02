namespace HitPan.Backoffice.API.Services;

/// <summary>
/// 사업자등록번호 체크섬 — 한국 국세청 표준 알고리즘.
///
/// 왜 별도 파일로 꺼냈나 (2026-08-02):
///   종전엔 BizNoVerifyController 안에 private 으로만 있었다.
///   그래서 실제 가입 처리(LandingSignupController)는 이 검증을 쓸 수 없었고,
///   국세청 API 가 응답을 못 주면 '아무 검증도 없이 거부'하는 수밖에 없었다.
///   ⇒ 국세청 한 곳이 멈추면 신규 가입이 전면 차단된다(2026-08-02 실제 발생).
///
///   체크섬은 오프라인 계산이라 외부 의존이 0이다. 국세청이 죽어도 항상 동작한다.
///   물론 체크섬만으로는 휴업·폐업을 걸러내지 못한다 — 그건 국세청만 안다.
///   따라서 이건 '국세청 대체'가 아니라 '국세청이 응답 못 할 때의 최소 관문'이다.
///
/// 가중치: 1,3,7,1,3,7,1,3,5
///   9번째 자리는 (d9*5)/10 의 몫을 추가로 더한다.
///   합 % 10 == 0 이면 검증자리는 0, 아니면 10-(합%10) 이 10번째 자리와 같아야 한다.
/// </summary>
public static class BizNoChecksum
{
    /// <summary>숫자 10자리 사업자번호의 체크섬 유효성. 형식이 다르면 false.</summary>
    public static bool IsValid(string? bizNo)
    {
        if (string.IsNullOrWhiteSpace(bizNo)) return false;
        var bn = new string(bizNo.Where(char.IsDigit).ToArray());
        if (bn.Length != 10) return false;

        ReadOnlySpan<int> w = stackalloc int[] { 1, 3, 7, 1, 3, 7, 1, 3, 5 };
        int sum = 0;
        for (int i = 0; i < 9; i++) sum += (bn[i] - '0') * w[i];
        sum += ((bn[8] - '0') * 5) / 10;
        int expected = (10 - (sum % 10)) % 10;
        return expected == (bn[9] - '0');
    }

    /// <summary>로그용 마스킹 — 220**62517 형태. 평문 전체를 로그에 남기지 않는다.</summary>
    public static string Mask(string? bizNo)
    {
        var bn = new string((bizNo ?? "").Where(char.IsDigit).ToArray());
        return bn.Length >= 6 ? bn[..3] + "**" + bn[5..] : "***";
    }
}
