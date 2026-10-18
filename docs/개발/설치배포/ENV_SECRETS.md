# 운영 시크릿 5종 배포 가이드 (2026-06-11)

> # 🔴🔴 2026-10-18 정정 — **이 문서의 이름 2개로는 값이 들어가지 않는다**
> (아래 원문은 **한 글자도 지우지 않았다** · #1 · 값은 어디에도 적지 않는다)
>
> | 이 문서가 적은 이름 | 코드에 닿나 | **정본 이름** |
> |---|---|---|
> | `BACKOFFICE_License__Pepper` | 🔴 **안 닿는다** | **`License__Pepper`** |
> | `BACKOFFICE_Backoffice__BizNoPepper` | 🔴 **안 닿는다** | **`Backoffice__BizNoPepper`** |
> | `HITPAN_BO_JWT_SECRET` · `HITPAN_BOOTSTRAP_TOKEN_KEY` · `HITPAN_BO_MFA_KEY` | 🟢 닿는다(코드가 이 이름을 직접 읽는다) | 그대로 |
>
> **왜 안 닿나** — 백오피스 API 는 접두사 환경변수 공급자를 **등록하지 않는다**(`src/HitPan.Backoffice.API/Program.cs:20` 기본 빌더뿐). 그래서 `BACKOFFICE_License__Pepper` 는 설정키 **`BACKOFFICE_License:Pepper`** 가 되고, 소비자가 읽는 키는 **`License:Pepper`** 다. ⇒ 값이 들어 있어도 **`License:Pepper` 를 요구하는 자리 11곳 / 8컨트롤러가 전부 throw** 한다(그중 **설치 경로 4곳 / 3컨트롤러** ⇒ 🔴 **원클릭 설치 500**).
>
> 🔴 **그리고 스크립트가 이 두 줄을 조용히 버린다** — `scripts/deploy-ncp-secrets.sh:42` 의 키 판정이 `^([A-Z_][A-Z0-9_]*)=` (**대문자·숫자·밑줄만**)이라, 소문자가 섞인 두 이름은 **오류 한 줄 없이 건너뛰어진다.** ⇒ 스크립트를 돌려도 그 2개는 **애초에 서버에 안 들어간다.**
> 🔴 **§4 검증(`curl … /api/credentials/status` 「5개 모두 configured: true」)으로는 확정할 수 없다** — 건강화면은 **환경변수 ∨ 설정파일**을 보는데 소비자는 **설정파일만** 본다(거짓초록). **확정 방법은 systemd override 의 이름 목록을 직접 보는 것**이다:
> ```bash
> sudo cat /etc/systemd/system/hitpan-backoffice-api.service.d/*.conf 2>/dev/null | cut -d'"' -f2 | cut -d= -f1
> ```
> (값은 출력되지 않는다 — `=` 뒤를 잘라낸다.)
>
> **그래서 지금 올바른 넣는 법** — 스크립트 대신 **override 파일에 손으로** 한 줄씩 넣는다(파일 이름은 `release-version.conf` **가 아닌** 이름으로 · 배포가 덮어쓴다):
> ```
> [Service]
> Environment="License__Pepper=<값>"
> Environment="Backoffice__BizNoPepper=<값>"
> ```
> 그 뒤 `sudo systemctl daemon-reload && sudo systemctl restart hitpan-backoffice-api`.
>
> 🔴 **스크립트(`deploy-ncp-secrets.sh:42`) 자체의 교정은 「배포 코드」라 이 문서로 하지 않는다** — 별건 P0 트랙에서 작업지시서·결재를 거친다.
> **근거** = [1-V] `docs/검증/선행/20261018_1V_B2_0을1로_게이트①②_선행검증서.md` §1-2~§1-4 · 처분 = `docs/운영기록/20261018_1V종합_B2착수전제_추가상신_PM전결정정.md` §4
> **따라 하기(사장님용)** = `docs/운영기록/20261018_사장님손_따라하기매뉴얼_10건.md` 1번·2번

## 시크릿 목록

| 키 | 용도 | 길이 |
|---|---|---|
| `HITPAN_BO_JWT_SECRET` | JWT 서명 키 | 64자 |
| `HITPAN_BOOTSTRAP_TOKEN_KEY` | 백오피스→ERP 부트스트랩 서명 키 | 64자 |
| `HITPAN_BO_MFA_KEY` | MFA(TOTP) AES 키 | 64자 |
| `BACKOFFICE_License__Pepper` | 시리얼 키 HMAC Pepper | 44자 |
| `BACKOFFICE_Backoffice__BizNoPepper` | 사업자번호 HMAC Pepper | 44자 |

## 자동 생성된 값

`secrets/.generated-secrets-20260611.txt` 참조. **외부 공개 절대 금지**.

## 사장님 직접 영역 (PM 권한 외, 헌법 #29)

### 1. 로컬 개발 PC — Windows 환경변수

PowerShell 관리자 권한:
```powershell
[Environment]::SetEnvironmentVariable("HITPAN_BO_JWT_SECRET", "<값>", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_BOOTSTRAP_TOKEN_KEY", "<값>", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_BO_MFA_KEY", "<값>", "Machine")
[Environment]::SetEnvironmentVariable("BACKOFFICE_License__Pepper", "<값>", "Machine")
[Environment]::SetEnvironmentVariable("BACKOFFICE_Backoffice__BizNoPepper", "<값>", "Machine")
```

또는 `setx` 1회만:
```cmd
setx HITPAN_BO_JWT_SECRET "<값>" /M
```

### 2. GitHub Actions Secrets

`scripts/setup-github-secrets.ps1` 1회 실행:
```powershell
.\scripts\setup-github-secrets.ps1
```

사전: `gh CLI` 설치 + `gh auth login`.

### 3. NCP 서버 (systemd)

NCP 서버 SSH 접속 후:
```bash
# 1) 로컬 PC에서 SCP 업로드
scp secrets/.generated-secrets-20260611.txt ncp-user@<NCP IP>:/tmp/

# 2) NCP 서버에서
sudo bash scripts/deploy-ncp-secrets.sh /tmp/.generated-secrets-20260611.txt
shred -u /tmp/.generated-secrets-20260611.txt
```

### 4. 검증

API 가동 후:
```bash
curl https://back.hitpan.kr/api/credentials/status
```

5개 모두 `configured: true` 면 정합.

## 사고 시

환경변수 미설정 → API 가동 시 `InvalidOperationException` 즉시 throw → 서비스 가동 안 됨. 안전 정합.

## 시크릿 회전 (1년 주기 권장)

1. 새 시크릿 생성 (`secrets/`)
2. 위 3단계 재실행
3. 기존 토큰 사용자에게 재로그인 안내 (JWT/MFA 키 변경 시)
