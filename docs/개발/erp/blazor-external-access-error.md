# 히트판 ERP — 외부 접속 장애 대응 매뉴얼

> 작성일: 2026-05-06 | 교훈: 베타 운영 중 실제 발생한 장애 기반

---

## 증상

- 개발 PC에서는 정상 작동
- 외부 PC / 모바일(사파리·크롬)에서 아래 중 하나 발생:
  - "서버에 연결할 수 없습니다. API 서버를 확인해주세요."
  - "오류가 발생했습니다. 새로고침" (Blazor 흰 화면)
  - 로그인 화면 자체가 뜨지 않음

---

## 진단 순서

### 1단계 — API 서버 살아있는지 확인
브라우저 주소창에 직접 입력:
```
https://api-demo.hitpan.kr/health
```
→ `{"status":"healthy"...}` 가 나오면 API 정상.
→ 연결 안 되면 터널 또는 API 서버 문제 (3단계로).

### 2단계 — Blazor 설정 파일 확인
브라우저 주소창에 직접 입력:
```
https://demo.hitpan.kr/appsettings.json
```
→ `{"ApiBaseUrl":"https://api-demo.hitpan.kr"}` 가 나와야 정상.
→ 빈 파일이거나 `localhost` 가 나오면 4단계로.

```
https://demo.hitpan.kr/appsettings.Development.json
```
→ 위와 동일하게 `{"ApiBaseUrl":"https://api-demo.hitpan.kr"}` 가 나와야 정상.
→ **404가 나오면 즉시 5단계로** — 이게 "오류가 발생했습니다" 크래시의 원인.

### 3단계 — 터널 상태 확인 (서버 PC에서)
```powershell
netstat -ano | findstr ":5257"
```
→ LISTENING이 나오면 API 프로세스 살아있음.

```powershell
# cloudflared 프로세스 확인
Get-Process cloudflared -ErrorAction SilentlyContinue
```
→ 없으면 터널 재시작 필요 (6단계로).

---

## 해결 방법

### 4단계 — appsettings.json 내용 수정
`c:\hitpan-api\wwwroot\appsettings.json` 내용 확인:
```json
{
  "ApiBaseUrl": "https://api-demo.hitpan.kr"
}
```
다른 값이면 위 내용으로 교체.

### 5단계 — appsettings.Development.json 복구 (★ 가장 중요)
**이 파일이 없으면 Blazor가 아예 시작을 못 한다.**

`c:\hitpan-api\wwwroot\appsettings.Development.json` 파일 생성:
```json
{
  "ApiBaseUrl": "https://api-demo.hitpan.kr"
}
```

> ⚠️ 절대 원칙: 이 두 파일은 삭제하거나 `localhost`로 바꾸면 외부 접속 전체 차단됨.

### 6단계 — 터널 재시작
```powershell
$cf = "C:\Program Files (x86)\cloudflared\cloudflared.exe"
Get-Process cloudflared -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Start-Process $cf -ArgumentList "tunnel --config `"C:\Users\소순근\.cloudflared\config.yml`" run hitpan-demo" -WindowStyle Hidden
```

---

## 고객사 배포 시 체크리스트

고객사 서버에 히트판 배포 후 반드시 확인:

- [ ] `wwwroot\appsettings.json` — `ApiBaseUrl` 이 고객사 도메인으로 설정됐는가
- [ ] `wwwroot\appsettings.Development.json` — 동일한 `ApiBaseUrl` 로 존재하는가
- [ ] 외부 기기(모바일)에서 `/health` 접속 확인
- [ ] 외부 기기(모바일)에서 로그인 확인

---

## 원인 분석 (2026-05-05 장애)

| 원인 | 결과 |
|------|------|
| `appsettings.Development.json` 삭제 | Blazor 부트 크래시 → "오류가 발생했습니다" |
| `appsettings.json` 에 `localhost` 값 | 로그인 시 "서버에 연결할 수 없습니다" |
| cloudflared `config.yml` 잘못 설정 | 터널 503 또는 라우팅 불가 |

---

## 핵심 파일 위치

| 파일 | 위치 | 역할 |
|------|------|------|
| appsettings.json | `c:\hitpan-api\wwwroot\appsettings.json` | Blazor API URL 설정 |
| appsettings.Development.json | `c:\hitpan-api\wwwroot\appsettings.Development.json` | Blazor 부트 필수 파일 |
| config.yml | `C:\Users\소순근\.cloudflared\config.yml` | 터널 라우팅 설정 |
| .env | `c:\hitpan-api\.env` | JWT·DB·암호화 키 |
