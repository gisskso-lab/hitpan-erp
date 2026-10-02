using HitPan.API.Services.ManualUpdate;

namespace HitPan.API.Services.LocalRollback;

// 20260930작1 1.3.50 확대 갈래 N1 — 설계 §19-3 [계약] 그대로(N1 이 만들고 N2 가 쓴다).
//   PC 에 남겨 둔 「서명 확인된 업데이트 안내 파일(manifest)」 저장본을 꺼내, 기존 서명 검증기로 **다시** 확인한 뒤
//   쓸 값만 돌려준다. 저장본 폴더 안 파일이 바뀌어도 서명이 맞지 않으면 null 이므로 위조로 쓰이지 않는다.
//   구현 = WatchdogUpdateCoreAdapter(워치독 형식은 그 파일 밖으로 나가지 않는다).

/// <summary>되돌리기 세 번째 길(NCP 에서 이전 판 받기)이 쓸 「이전 판 안내」 창구.</summary>
public interface IPreviousPackageFeed
{
    /// <summary>
    /// <paramref name="version"/>(<c>M.m.b</c>) 판의 저장본을 읽어 서명을 다시 확인한다.
    /// 저장본 없음 · 서명 불량 · 안의 판이 요청 판과 다름 · 형식 불량(3자리 판·sha256 64자·주소) = null + 경고 로그
    /// (구분은 로그로만 · 화면은 모두 「이전 판 없음」).
    /// </summary>
    FeedPackage? LoadVerified(string version);
}
