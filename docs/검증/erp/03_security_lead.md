# 03. 보안팀장 정독 보고서

**작성:** 보안팀장 (안랩 40년, Harvard 석사)
**일자:** 2026-05-14 새벽

---

## 1. 헌법 위반 후보 표

| 헌법 | 위반 후보 | Line | 심각 | 상태 |
|---|---|---|---|---|
| #1 | tenant_id 파라미터 수신 | 없음 (HttpContext.Items만) | ✅ PASS | JWT 클레임 |
| #5 | AES Value Converter 미적용 | L:416, 610, 611, 614 | 🟡 PARTIAL | 4/5 적용 |
| #15 | 빈 catch 블록 | 없음 | ✅ PASS | 모두 로깅 |
| #18 | 본사 송신 코드 | 0건 확인 | ✅ PASS | HttpClient 무사용 |
| #22 | 데이터 최소주의 | 우려 | 🟡 MEDIUM | 후술 |
| #23 | 5중 검증 | 미확인 | 🔴 CRITICAL | AI 협업 코드 |

---

## 2. AES 5컬럼 적용 상태

| 컬럼 | 테이블 | Line | 상태 |
|---|---|---|---|
| resident_no_encrypted | employees | L:610 | ✅ EncryptToBytes |
| salary_encrypted | employees | L:611 | ✅ |
| salary_extra_encrypted | employees | L:614 | ✅ |
| ceo_resident_no_encrypted | partners | L:416 | ✅ |
| **raw_data** | **migration_errors** | **없음** | 🔴 **미구현** |

---

## 3. MDB 비번 보안 (AsyncLocal, L:33-34, 58, 69)
```csharp
private static readonly AsyncLocal<string?> _mdbPasswordContext = new();
_mdbPasswordContext.Value = mdbPassword;  // 시작
_mdbPasswordContext.Value = null;          // finally 종료
```
| 항목 | 평가 |
|---|---|
| 스택 고립 | ✅ AsyncLocal 비동기 격리 |
| 메모리 누수 | ✅ finally `= null` |
| 평문 전송 | ✅ HTTPS만 |
| 로그 마스킹 | ✅ 비번 로그 0건 |

⚠️ PreviewAsync는 finally 누락 (백엔드 함정 #1과 동일) → 컨텍스트 누수 가능

---

## 4. 로그 마스킹 / 본사 송신

| 영역 | 상태 |
|---|---|
| MdbMigrationService | ✅ 민감 필드 미로깅 |
| MigrationController | ✅ SensitiveFieldMasking |
| sensitive_access_log | 🟡 DDL만 명세, 구현 미추적 |

**본사 송신: HttpClient/RestClient/SendAsync 0건 확인.** 헌법 #18·#22 ✅

---

## 5. 옵션 B 보안 게이트 (5중 검증)

| # | 영역 | 현황 | 상태 |
|---|---|---|---|
| 1 | 단위 테스트 (라운드트립·NULL·키 분실) | SPEC §8 명시 | 🔴 코드 미발견 |
| 2 | migration_errors.raw_data AES INSERT | 미구현 | 🔴 CRITICAL |
| 3 | sensitive_access_log + step-up | 별도 지시서 | 🟡 W2 D2 미완 |
| 4 | SAST (CodeQL/Snyk/Roslyn/TruffleHog) | 결재 #23 | 🟡 CI/CD 미확인 |
| 5 | 데이터 최소주의 추적 | 정책 O / 운영 매뉴얼 X | 🟡 작성 필요 |

---

## 6. Try-Catch 검증

- Controller L:65-109 + L:132-175 + L:195-230: 모두 `_logger.LogWarning/Error` 필수 ✅
- Service L:206-211, L:216-223: try-finally 롤백·복원 로깅 OK ✅
- **헌법 #15 준수 ✅**

---

## 7. 보안 평점

**현 코드 보안: 75/100**

| 영역 | 점수 |
|---|---|
| tenant_id 격리 | ✅ 100 |
| AES 4컬럼 | ✅ 100 |
| 로깅·예외 | ✅ 100 |
| 본사 비노출 | ✅ 100 |
| **에러 추적 raw_data** | 🔴 0 |
| **감사로그 통합** | 🔴 0 |

---

## 8. 강행 항목 (마이그 진행 전 必수)

1. `migration_errors.raw_data` AES INSERT 로직 추가
2. 도메인 P0 실패 시 자동 감사로그
3. 단위 테스트 통과

---

## 9. 서브에이전트(보안 개발자 3명) 분담

| 담당 | 임무 | 일정 |
|---|---|---|
| 개발자 A | migration_errors.raw_data AES INSERT (도메인 catch→저장) | W2 D1 |
| 개발자 B | 단위 테스트 8개 (VALUE_CONVERTER_SPEC §8) + SAST 통합 | W2 D1-2 |
| 개발자 C | sensitive_access_log DDL + 감시 미들웨어 (step-up, 마스킹) | W2 D2-3 |

---

## 10. 사장님 결재 의결 사항

1. 5중 검증 중 2·3번 미완료 상태로 W2 진입 승인할지 → ⚠️ 재검토
2. raw_data B안(VARBINARY 암호화) 확정됐으나 코드 미구현 → ⚠️ 즉시 추가 개발
3. 로그 마스킹 정책 O / 감사로그 테이블 미완 → W2 D2 일정 재확인
