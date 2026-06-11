# 운영 시크릿 5종 배포 가이드 (2026-06-11)

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
