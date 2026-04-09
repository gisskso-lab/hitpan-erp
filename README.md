# 히트판 SaaS ERP

> 공영정보 히트판 컨버전 프로젝트 | VB/Access → C# ASP.NET Core + Blazor + MariaDB

---

## 팀 구조

| 역할 | 담당 |
|---|---|
| 오너 | 사장님 — 방향 결정, 최종 승인 |
| 기획설계 | Claude 팀 — 아키텍처, DB, API, 보안 설계, 코드 리뷰 |
| 개발 | Cursor 개발팀 — 설계서 기반 구현 |
| 저장소 | GitHub — 단일 진실 공급원 |

---

## 기술 스택

- **Backend** ASP.NET Core 8 (C#)
- **Frontend** Blazor WebAssembly + MudBlazor
- **Database** MariaDB 10.11 LTS (고객사 로컬 설치)
- **ORM** EF Core + Pomelo.EntityFrameworkCore.MySql
- **Scheduler** Hangfire
- **Auth** JWT + Refresh Token (멀티테넌트 클레임)
- **Payment** 토스페이먼츠 + 카카오페이 (빌링키)

---

## 빠른 시작

```bash
# 1. 레포 클론
git clone https://github.com/[org]/hitpan-erp.git

# 2. 환경변수 설정 (.env 파일 생성)
cp .env.example .env
# ERP_ENCRYPTION_KEY, DB_CONNECTION 등 설정

# 3. DB 초기화
mysql -u root -p < docs/design/hitpan_db_ddl_FINAL_v1.0.sql

# 4. 실행
cd src && dotnet run --project HitPan.API
```

---

## 폴더 구조

```
hitpan-erp/
├── .cursorrules              # Cursor 개발 규칙 (필독)
├── .env.example              # 환경변수 샘플
├── docs/
│   ├── design/               # DB 설계서, ERD, 아키텍처 문서
│   ├── api/                  # API 명세서
│   └── guides/               # 개발 가이드, 온보딩
├── src/
│   ├── HitPan.Domain/        # 엔티티, 도메인 이벤트
│   ├── HitPan.Application/   # 서비스, 인터페이스, DTO
│   ├── HitPan.Infrastructure/# EF Core, 외부 API
│   ├── HitPan.API/           # Web API, 미들웨어
│   └── HitPan.Web/           # Blazor WebAssembly
└── .github/
    ├── ISSUE_TEMPLATE/       # 작업 지시서 템플릿
    └── PULL_REQUEST_TEMPLATE/# PR 템플릿
```

---

## 브랜치 전략

```
main          # 프로덕션. 직접 푸시 금지
develop       # 통합 브랜치
feature/[#이슈번호]-[작업명]   # 기능 개발
hotfix/[#이슈번호]-[작업명]    # 긴급 수정
```

---

## 핵심 설계 원칙

1. **멀티테넌트** — 모든 테이블 tenant_id 포함, Global Query Filter 자동 적용
2. **INSERT ONLY 원장** — stock_ledger, journal_lines는 UPDATE/DELETE 금지
3. **워크플로우 유연성** — 발주 없이 매입, 수주 없이 판매 허용 (설정 기반)
4. **개인정보 보호** — 급여·계좌·주민번호 AES-256 암호화

> 자세한 내용은 `.cursorrules` 및 `docs/design/` 참고

---

## 관련 문서

- [DB 설계 명세서](docs/design/hitpan_db_design.docx)
- [DB ERD](docs/design/erd/)
- [DDL SQL](docs/design/hitpan_db_ddl_FINAL_v1.0.sql)
- [아키텍처 발표자료](docs/design/hitpan_db_design.pptx)
