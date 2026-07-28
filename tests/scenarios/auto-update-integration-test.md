# 자동 업데이트 + EXE 통합 시험 시나리오

**작성**: 2026-06-09 PM
**근거**: Plan Day 18~19 (`cicd-velvety-reef.md`)
**대상**: NCP `updates.hitpan.kr` 박힌 후 시험

---

## 시험 환경

- 시험 PC 1대 (Watchdog 설치된 깨끗한 환경)
- 환경변수: `HITPAN_UPDATE_FEED=https://updates.hitpan.kr`
- 환경변수: `HITPAN_AUTO_UPDATE_ENABLED=true`

---

## 시나리오 A — Normal 채널 (일반 업데이트)

### 흐름
1. 현 버전 `1.0.0` 설치
2. 본사가 `updates.hitpan.kr/manifest.json`에 v1.0.1 (channel=Normal) 박음
3. 워치독이 매일 새벽 3시 체크 → 새 버전 감지
4. 야간 윈도우 (03:00~04:00) 자동 다운로드 + 검증 + 적용
5. ERP 서비스 재시작 → 새 버전 작동

### 통과 기준
- [ ] `Get-Content $env:HITPAN_INSTALL_DIR\logs\watchdog.log` 에 `[Update] 새 버전 발견` 박힘
- [ ] 04:00 이전 적용 완료
- [ ] ERP API 작동 정상 (HTTP 200)
- [ ] 고객 화면에 알림 없음 (고객 손 0번)

---

## 시나리오 B — Emergency 채널 (긴급 패치)

### 흐름
1. 현 버전 `1.0.1` 설치
2. 본사가 `updates.hitpan.kr/manifest.json`에 v1.0.2 (channel=Emergency) 박음
3. 워치독이 1분 자가 진단에서 새 버전 감지
4. ERP 화면에 5분 안내 ("긴급 패치 안내, 5분 후 자동 적용")
5. 5분 후 자동 다운로드 + 검증 + 적용

### 통과 기준
- [ ] 5분 안내 화면 표시 박힘
- [ ] 5분 이내 적용 완료
- [ ] sha256 검증 박힘
- [ ] 실패 시 자동 롤백 (직전 EXE로 복원)

---

## 시나리오 C — Major 채널 (스키마 변경)

### 흐름
1. 현 버전 `1.0.2` 설치
2. 본사가 `updates.hitpan.kr/manifest.json`에 v1.1.0 (channel=Major, requiresMigration=true) 박음
3. ERP 화면에 동의 요청 다이얼로그 ("DB 스키마 변경 — 30분 다운타임 — 영업시간 외 예약 동의 필요")
4. 고객 동의 → 영업시간 외 시간 예약 (예: 23:00)
5. 예약 시각에 자동 다운로드 + DB 마이그 + 적용

### 통과 기준
- [ ] 동의 다이얼로그 표시 박힘
- [ ] 동의 무응답 시 90일 옛 버전 유지 (사장님 결재 4 A안)
- [ ] 예약 시각 ±5분 이내 적용 시작
- [ ] DB 마이그 사고 시 자동 롤백 (백업 복원)

---

## 시나리오 D — 롤백 시험

### 흐름
1. 본사가 의도적으로 깨진 v1.0.3 박음 (시작 시 즉시 크래시)
2. 워치독이 새 버전 다운로드 + 적용
3. ERP 시작 시 5초 이내 크래시 감지
4. 워치독이 자동 롤백 → 직전 v1.0.2로 복원
5. 본사 메타 ping에 롤백 사고 박힘

### 통과 기준
- [ ] 크래시 감지 5초 이내
- [ ] 자동 롤백 30초 이내
- [ ] 롤백 후 ERP 정상 작동
- [ ] 본사 알림 박힘 (메타정보만, 헌법 #22 정합)

---

## manifest.json 샘플 (본사 박는 영역)

```json
{
  "version": "1.0.1",
  "channel": "Normal",
  "downloadUrl": "https://updates.hitpan.kr/packages/hitpan-1.0.1.zip",
  "sha256": "a3b2c1d4e5f6...",
  "sizeBytes": 52428800,
  "releasedAt": "2026-07-15T03:00:00Z",
  "releaseNotes": "버그 수정 + 성능 개선",
  "requiresMigration": false,
  "consentMessage": null
}
```

### Emergency 사례
```json
{
  "version": "1.0.2",
  "channel": "Emergency",
  "downloadUrl": "https://updates.hitpan.kr/packages/hitpan-1.0.2.zip",
  "sha256": "...",
  "sizeBytes": 53000000,
  "releasedAt": "2026-07-20T14:30:00Z",
  "releaseNotes": "보안 패치",
  "requiresMigration": false,
  "consentMessage": null
}
```

### Major 사례
```json
{
  "version": "1.1.0",
  "channel": "Major",
  "downloadUrl": "https://updates.hitpan.kr/packages/hitpan-1.1.0.zip",
  "sha256": "...",
  "sizeBytes": 75000000,
  "releasedAt": "2026-08-01T10:00:00Z",
  "releaseNotes": "신규 BOM 모듈 + DB 스키마 변경",
  "requiresMigration": true,
  "consentMessage": "신규 기능을 위해 DB 스키마 변경이 필요합니다. 약 30분의 다운타임이 발생하며, 영업시간 외 시간에 자동 예약됩니다."
}
```

---

## 시험 자동화 (PowerShell)

```powershell
# tests/scenarios/AutoUpdate-Simulate.ps1
.\AutoUpdate-Simulate.ps1 -Channel Normal -Version 1.0.1
.\AutoUpdate-Simulate.ps1 -Channel Emergency -Version 1.0.2
.\AutoUpdate-Simulate.ps1 -Channel Major -Version 1.1.0
.\AutoUpdate-Simulate.ps1 -Channel Rollback -Version 1.0.3-broken
```
