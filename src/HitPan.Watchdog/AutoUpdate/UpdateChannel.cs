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
    string? ConsentMessage
);
