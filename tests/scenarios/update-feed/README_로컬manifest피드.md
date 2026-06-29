# 로컬 manifest 피드 — 버전 업데이트 검증용 (A갈래 준비물)

> 작성: 네트워크 매니저 마이클 / 2026-06-29
> 사장님 결재: manifest 피드 = **B안(로컬 파일)**. NCP 아님, 비용 0, demo 완전 분리.
> 헌법 #39: 운영(demo)·`updates.hitpan.kr` 절대 안 건드림. 이 폴더는 **로컬 격리** 검증 자산이다.

---

## 1. 로컬 피드가 뭔가

워치독(`HitPan.Watchdog`)이 버전 업데이트를 확인할 때 읽는 `manifest.json`을,
**본사 서버(`https://updates.hitpan.kr`) 대신 테스트 PC의 로컬 경로에서 읽게** 바꾸는 것이다.

- 운영 흐름: 워치독 → `GET https://updates.hitpan.kr/manifest.json`
- 테스트 흐름: 워치독 → 로컬 피드(예: `C:\HitPanTest\update-feed\manifest.json`)

이렇게 하면 **운영 서버를 전혀 건드리지 않고**, 가짜 "새 버전" manifest 하나를 로컬에 두는 것만으로
업데이트 동선(로그인 Y/N, Major 동의, 백업 실패 차단, 마이그 롤백)을 끝까지 검증할 수 있다.

**운영 `updates.hitpan.kr` 과 무관하다.** 이 폴더의 파일은 실제 게시·배포 대상이 아니라,
test 슬롯에서만 바라보는 검증 자료다.

---

## 2. manifest 구조 정본

정본 = `src/HitPan.Watchdog/AutoUpdate/UpdateChannel.cs` 의 `UpdateManifest` record.
필드 9개, 순서·타입 그대로:

| JSON 키 (camelCase) | 타입 | 설명 |
|---|---|---|
| `version` | string | 새 버전 문자열. 현재 설치 버전과 **다르면** "새 버전"으로 인식 |
| `channel` | **정수** | 0=Emergency, 1=Normal, 2=Major (아래 §3 필독) |
| `downloadUrl` | string | 패키지 zip URL. 로컬 검증은 더미/로컬 HTTP |
| `sha256` | string | 패키지 sha256 (소문자 hex 64자). 실제 zip 만들면 채움 |
| `sizeBytes` | long | 패키지 바이트 크기 |
| `releasedAt` | DateTime | ISO8601 UTC (예: `2026-06-29T03:00:00Z`) |
| `releaseNotes` | string? | 릴리스 노트 (null 허용) |
| `requiresMigration` | bool | DB 스키마 변경 여부. Major 마이그 롤백 검증용 |
| `consentMessage` | string? | Major 동의 요청 메시지 (Normal/Emergency 는 null) |

역직렬화 코드: `UpdateClient.GetLatestManifestAsync` →
`http.GetFromJsonAsync<UpdateManifest>(...)`. 옵션 지정 없음 = **`JsonSerializerDefaults.Web`**.
- 프로퍼티 이름: **대소문자 무시** → camelCase/PascalCase 둘 다 OK.
- enum: 기본 Web 옵션엔 문자열 enum 변환기가 **없다** → §3.

---

## 3. ⚠️ 채널은 문자열이 아니라 **숫자**다 (역직렬화 핵심)

`UpdateClient` 가 `JsonSerializerDefaults.Web` 만 쓰고 `JsonStringEnumConverter` 를
**등록하지 않았다.** 그래서 `channel` 은 enum 인덱스 **정수**로만 역직렬화된다.

| 채널 | enum 인덱스 | manifest 값 |
|---|---|---|
| Emergency | 0 | `"channel": 0` |
| Normal | 1 | `"channel": 1` |
| Major | 2 | `"channel": 2` |

- ✅ `"channel": 1` → `Normal` 로 역직렬화 성공 (실측 확인)
- ❌ `"channel": "Normal"` → **JsonException 으로 실패** (실측 확인)

> 참고: 기존 `installer/updates/manifest-sample.json` 은 `"channel": "Normal"` (문자열)이라
> 현재 `UpdateClient` 코드로는 **역직렬화 실패한다.** (별도 보고 — 본 작업 범위 밖, 코드 미수정)

이 폴더의 3개 샘플은 모두 숫자 채널로 작성했고, 실제 `UpdateManifest` 로 역직렬화되는 것을 실측 검증했다.

---

## 4. 샘플 파일

| 파일 | 채널 | requiresMigration | 검증 목적 |
|---|---|---|---|
| `manifest-normal.json` | 1 (Normal) | false | 야간 자동 적용 동선 |
| `manifest-major.json` | 2 (Major) | true | 로그인 Y/N 동의 + 백업 + **마이그 롤백** |
| `manifest-emergency.json` | 0 (Emergency) | false | 5분 안내 후 강제 적용 |

`sha256` 은 전부 자리표시(0 64자). **실제 검증 시** 테스트용 zip 패키지를 만든 뒤
그 파일의 sha256 을 계산해 채워 넣어야 한다 (안 맞으면 `VerifySha256Async` 가 false → 적용 차단).

```powershell
# 테스트 zip 의 sha256 계산 (PowerShell)
(Get-FileHash C:\HitPanTest\update-feed\packages\hitpan-9.9.1-test-normal.zip -Algorithm SHA256).Hash.ToLower()
```

---

## 5. test 슬롯이 로컬 피드를 바라보게 하는 방법

`UpdateClient` 는 피드 주소를 **환경변수 `HITPAN_UPDATE_FEED`** 에서 먼저 읽고,
없으면 `https://updates.hitpan.kr` 로 폴백한다 (`UpdateClient` 생성자, 라인 38~39).

```
_feedUrl = Environment.GetEnvironmentVariable("HITPAN_UPDATE_FEED") ?? "https://updates.hitpan.kr";
```

그리고 manifest 는 `$"{_feedUrl}/manifest.json"` 으로 **`GetFromJsonAsync`(HTTP GET)** 으로 가져온다.

### 5-1. 현재 구조의 제약 (미결 = 작1 고리1)

`GetFromJsonAsync` 는 **HTTP(S) URL 만** 받는다. `file://` 로컬 파일 경로는
`HttpClient` 가 받지 않으므로, 다음 둘 중 하나가 필요하다:

- **(권장) 로컬 정적 HTTP 서버**: test 슬롯 PC에서 `C:\HitPanTest\update-feed\` 를
  루트로 하는 간단한 정적 서버를 띄우고(예: `127.0.0.1:8099`),
  `HITPAN_UPDATE_FEED=http://127.0.0.1:8099` 로 설정.
  → 코드 변경 0. 샘플 `downloadUrl` 도 이 주소(`http://127.0.0.1:8099/packages/...`)로 맞춰 둠.

  ```powershell
  # 정적 HTTP 서버 예시 (Python 있으면)
  cd C:\HitPanTest\update-feed
  python -m http.server 8099 --bind 127.0.0.1
  # 그 다음 (해당 슬롯 프로세스 환경에서)
  $env:HITPAN_UPDATE_FEED = "http://127.0.0.1:8099"
  ```

- **(대안) 코드 고리1: `file://` 로컬 경로 지원** — `UpdateClient` 의 feedUrl 처리를
  `file://` 또는 로컬 절대경로일 때 `File.ReadAllTextAsync` + `JsonSerializer.Deserialize`
  로 분기. 이러면 `HITPAN_UPDATE_FEED=C:\HitPanTest\update-feed` 만으로 동작.
  → **코드 변경 필요. 작1 작업지시서 고리1(UpdateClient feedUrl 변경)에서 다룰 항목.**
  (본 A갈래 = 파일 작성만이므로 여기서는 코드 미수정.)

### 5-2. 검증 절차 (가짜 새버전으로)

1. 테스트 슬롯 PC에 `C:\HitPanTest\update-feed\` 폴더 생성.
2. 이 폴더의 샘플 중 하나를 골라 그 PC의 `C:\HitPanTest\update-feed\manifest.json` 으로 복사.
   - 단, `version` 을 **현재 설치 버전과 다르게** 둬야 "새 버전"으로 인식된다
     (`GetLatestManifestAsync` 가 같으면 null 반환, 라인 57~61). 샘플은 `9.9.x-test-*` 로 일부러 높게 둠.
3. (5-1 권장안) 로컬 HTTP 서버 기동 + `HITPAN_UPDATE_FEED` 설정.
4. 테스트용 zip 패키지를 `packages/` 에 두고 그 sha256 을 manifest 에 채움.
5. 워치독/오케스트레이터를 테스트 슬롯에서 돌려 동선 확인:
   - **Normal**: 야간 자동 적용되는지.
   - **Major**: 로그인 시 Y/N 다이얼로그 → 동의 → 백업 → (백업 실패 시 **차단**) → 마이그 → 실패 시 **롤백**.
   - **Emergency**: 5분 안내 후 강제 적용.
6. 끝나면 환경변수 해제·로컬 서버 종료. **운영 `updates.hitpan.kr` 은 처음부터 끝까지 무관.**

---

## 6. 헌법 정합

- **#39**: 운영(demo)·`updates.hitpan.kr` 직접 수술 0. 전 과정 로컬 격리 테스트 환경.
- **#22**: 워치독은 다운로드만(고객 데이터 본사 전송 0).
- **#23**: sha256 검증 = 5중 검증 정합. 자리표시 sha256 은 실제 zip 으로 교체해야 통과.
- **#34**: 연결축(업데이트) = 베타부터 정식 완성도. 본 자산은 그 검증용 준비물.
