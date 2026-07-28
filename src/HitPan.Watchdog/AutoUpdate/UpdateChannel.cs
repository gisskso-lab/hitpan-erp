namespace HitPan.Watchdog.AutoUpdate;

// 사장님 결재 2026-06-09 (결재 4: 메이저 업데이트 정책 A안)
//
// 업데이트 채널 3분기 (사장님 결재 Plan cicd-velvety-reef 정합):
//   - Emergency: 본사 강제 일괄 (5분 안내 후 즉시 적용)
//   - Normal:    워치독 자동 (매일 새벽 3시)
//   - Major:     고객 동의 후 예약 (영업시간 외) — 동의 무응답 시 옛 버전 90+30일 유지 후 본사 CS
//
// 헌법 정합:
//   #28·#30 — 고객 손 0번
//   #25 — 쉽게·정확하게·안전하게
//   #34 — 베타부터 정식 완성도
public enum UpdateChannel
{
    Emergency,
    Normal,
    Major
}

public sealed record UpdateManifest(
    string Version,
    UpdateChannel Channel,
    string DownloadUrl,
    string Sha256,
    long SizeBytes,
    DateTime ReleasedAt,
    string? ReleaseNotes,
    // Major 채널 전용: DB 스키마 변경 여부 + 동의 요청 메시지
    bool RequiresMigration,
    string? ConsentMessage,

    // ── 서명 (2026-07-16, 작1 W4-0/서명 — 사장님 결재) ──────────────────────────────────
    //
    // 왜 sha256 만으로는 부족한가:
    //   sha256 은 manifest '안에' 있다. 즉 manifest 를 바꿀 수 있는 자는 sha256 도 같이 바꾼다
    //   (자기참조라 무결성을 스스로 증명하지 못한다). 게다가 feed 주소는 HITPAN_UPDATE_FEED
    //   환경변수로 바뀔 수 있다. 지금은 적용 코드가 없어 표면이 "다운로드까지"로 막혀 있을 뿐,
    //   실제 파일 교체(W4-2)가 붙는 순간 "환경변수 하나 → SYSTEM 권한 임의 코드 실행"이 완성된다.
    //   그래서 2층으로 나눈다: sha256 = zip 무결성 / Signature = manifest 진위.
    //
    // 서명 대상 = manifest 본문의 정규화 JSON(Signature·Kid 제외, sha256 포함) — UpdateManifestSigning 참조.
    //   개인키는 NCP(600 root)에만 있고 CI·레포·EXE 어디에도 없다. EXE 는 공개키만 갖는다(노출 무해).
    //
    // Kid = 공개키 식별자. 개인키가 유출돼도 새 kid 로 롤오버하고 옛 kid 를 폐기할 수 있다.
    //   시리얼 증표(SerialProofVerifier)의 kid 와 분리한다("upd-v1") — 시리얼 키가 새도
    //   그게 곧 코드 실행 권한이 되면 안 된다(권한 분리).
    string? Signature = null,
    string? Kid = null
);
