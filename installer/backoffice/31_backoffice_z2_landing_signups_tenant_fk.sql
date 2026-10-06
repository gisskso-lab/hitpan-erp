-- ════════════════════════════════════════════════════════════════════════
-- 31_backoffice_z2_landing_signups_tenant_fk.sql
-- 작업지시서 20261006작8 §1 (Z2) 갈래 가 — 대리점 꼬리표 사슬: FK 신설 + backfill 1회
--
-- 🔧 번호 교정 2026-10-07 ([4] 작업리뷰서 §5-1 · 머지 전 필수):
--   이 파일은 갈래 가에서 `30_backoffice_z2_...` 로 났고, 같은 사이클 갈래 다가
--   `30_backoffice_tenants_extra_accounts.sql` 로 **같은 번호 30** 을 썼다.
--   SchemaMigrator 는 파일명 **전체** Ordinal 정렬 + _schema_migrations 의 file_name PK 라
--   skip 사고는 없지만, 작업지시서 §6 「번호 겹침 금지」 위반이다.
--   멱등 키가 **파일명** 이므로 배포 후 개명은 재실행을 부른다 ⇒ 미배포 상태인 머지 전에 31 로 개명.
--
-- 적용 방식 (백오피스 진실원 — SchemaMigrator.cs 실측 2026-10-06):
--   - installer/backoffice/*.sql 을 파일명 Ordinal 번호순으로 적용
--     (실측 2026-10-07 현재 순서: 00→10→11→20→30→31→90→91).
--   - _schema_migrations (파일명 PK + 내용 SHA256) 로 같은 내용은 1회만 — 멱등.
--   - 파일 자체도 재실행 안전해야 한다: 전부 IF NOT EXISTS · backfill 은 tenant_id IS NULL 행만.
--   - ERP 의 DB-NN 방식(src/HitPan.API/Migrations/SQL)이 아니다 — 백오피스는 이 폴더가 진실원.
--
-- 왜 (선행검증 C-1): landing_signups ↔ tenants 가 회사명 "글자"로만 이어져 있어
--   동명 회사 2건이면 가입서·시리얼·메일이 엇갈린다. 키(FK)로 잇는다.
--
-- 헌법 정합:
--   #1·#37 — 추가만. 기존 컬럼·키 제거 0. 글자 조인 폴백 경로도 코드에 보존(#20).
--   #13 — DESCRIBE 선행 실측 2026-10-06: tenants.tenant_id = varchar(36) PK (00_core:105).
--       🔴 DB명세서 §2 의 「BIGINT」 는 실물과 다르다 — BIGINT FK → varchar(36) PK 는
--       errno 150 으로 생성 자체가 불가. FK 성립이 설계 의도이므로 varchar(36) 으로 작성.
--       (개발명세서에 보고 — docs/개발/백오피스/20261006작8_갈래가_Z2_개발명세서.md)
--   #17 — 신규 테이블 없음(기존 ALTER 만).
--   ON DELETE SET NULL — SignupsAdminController.Delete 가 pending tenants 를 물리삭제한다.
--       FK 가 그 삭제를 막으면 안 되고, 연결만 끊겨야 한다(가입서 행은 정산 추적용으로 보존).
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE landing_signups
    ADD COLUMN IF NOT EXISTS tenant_id varchar(36) NULL DEFAULT NULL
        COMMENT 'Z2 키 연결(승인 시 기록) — NULL = 옛 행(글자 조인 폴백) 또는 모호·고아';

ALTER TABLE landing_signups
    ADD KEY IF NOT EXISTS ix_landing_signups_tenant (tenant_id);

-- MariaDB 문법 실측(11.4.10): IF NOT EXISTS 는 FOREIGN KEY 뒤 —
--   ADD CONSTRAINT symbol FOREIGN KEY IF NOT EXISTS (col) REFERENCES …
ALTER TABLE landing_signups
    ADD CONSTRAINT fk_landing_signups_tenant
        FOREIGN KEY IF NOT EXISTS (tenant_id) REFERENCES tenants (tenant_id)
        ON DELETE SET NULL ON UPDATE CASCADE;

-- ── backfill 1회 (PM 결재 방침 2026-10-06 — 반자동 원칙·자동 추정 금지) ──
--   같은 회사명 tenants 후보가 정확히 1건 → 그 키로 연결(후보 1건이면 시각 최근접 = 그 1건).
--   후보 2건 이상(동명 회사) = 모호 → NULL 유지. 0건(고아) → NULL 유지.
--   tenant_id IS NULL 행만 건드리므로 재실행 안전.
--
-- 🔧 교정 2026-10-07 ([3-V] I-2 · PM 결재 = 「좁히는 방향으로 채택」):
--   종전엔 가입서 status 를 안 봤다 ⇒ 반려·취소·삭제된 **타사** 가입서가 동명 tenant 에 키로
--   굳어, 라이선스 사업자번호 검증(LandingPublicController ClaimLicense — tenant_id = @TenantId)이
--   타사 biz_no_hash 로 통과할 수 있었다. 글자 추정을 키로 **영구화**하는 길이라 교정 경로가 없다.
--   ⇒ 후보를 **정상 계열(승인·완료) 가입서로 한정**한다. 그 밖(반려·취소·미승인)은 NULL 유지.
--     (사업자번호 해시·이메일 2차 대조는 이번 범위 밖 — 후속)
--
--   #13 status 실측 2026-10-07 (추측 금지):
--     · DESCRIBE landing_signups → `status varchar(20) NOT NULL DEFAULT 'submitted'` + KEY ix_status.
--       **enum 이 아니다** ⇒ DB 가 값을 제한하지 않는다. 그래서 값 목록은 코드 전수로 확정했다.
--     · 실제 행 DISTINCT: hitpan_backoffice = **0건**(표가 비어 공집합) ·
--       hitpan_trgtest = 2건 전부 `approved`. 실데이터로는 값 목록을 못 얻는다.
--     · 코드가 **쓰는** 값 전수(grep 2026-10-07):
--         'submitted' (DDL 기본값 · LandingSignupController:265 INSERT)
--         'paid'      (LandingSignupController:374 — 결제 완료 · 아직 미승인)
--         'approved'  (SignupsAdminController:169 — 관리자 승인)
--         'rejected'  (SignupsAdminController:384)
--         'deleted'   (SignupsAdminController:571 — 논리삭제)
--         'active'    — **쓰는 곳 0** · 읽기만(옛 판 잔재). 아래 4곳이 'approved' 와 한 묶음으로 읽는다.
--     · 「정상 계열」 정의는 코드 자신의 정의를 그대로 쓴다 — `('approved','active')`:
--         LandingSignupController:194 (중복 가입 판정) · :229 (정리 대상 제외) ·
--         SignupsAdminController:131 · :290 (이미 승인 처리됨 판정).
--     · 'submitted'·'paid' 를 제외해도 사슬은 안 끊긴다(#20) — 그 행들은 승인 시점에
--       SignupsAdminController:169 가 tenant_id 를 직접 기록한다. backfill 은 **옛 승인분**용이다.
UPDATE landing_signups s
JOIN (
    SELECT s2.signup_id,
           (SELECT t.tenant_id FROM tenants t WHERE t.company_name = s2.company_name LIMIT 1) AS only_tenant_id
    FROM landing_signups s2
    WHERE s2.tenant_id IS NULL
      AND s2.status IN ('approved', 'active')   -- 🔧 I-2 — 반려·취소·미승인 가입서는 후보 제외
      AND (SELECT COUNT(*) FROM tenants t2 WHERE t2.company_name = s2.company_name) = 1
) m ON m.signup_id = s.signup_id
SET s.tenant_id = m.only_tenant_id
WHERE s.tenant_id IS NULL;

-- ── 건수 기록 (PM 결재: 모호 건수는 로그로 남긴다 · G-4 행 수 검산 근거) ──
--   🔧 교정 2026-10-07 (I-2): 검산식에 excluded_by_status 를 더한다.
--     total = linked + ambiguous_null_kept + orphan_null + excluded_by_status
--     NULL 로 남은 행의 tenants 후보 수는 0 · 1 · 2+ 로 **빠짐없이 갈린다**:
--       0  → orphan_null · 2+ → ambiguous_null_kept ·
--       1  → status 가 정상 계열이면 연결됐으니 NULL 이 아니다 ⇒ NULL 로 남은 1건짜리 = excluded_by_status.
--     세 분류가 겹치지 않으므로 합이 total 과 맞는다(G-4 가 이 식을 센다).
--   bo_audit_log 는 actor_email·actor_role NOT NULL (00_core:281-297 실측) — 'system' 으로 채운다.
INSERT INTO bo_audit_log
    (actor_user_id, actor_email, actor_role, action, target_type, target_id, detail_json, ip_address, created_at)
SELECT
    'system', 'system', 'system',
    'z2.signup_tenant_backfill', 'landing_signups', 'backfill-20261006',
    JSON_OBJECT(
        'total',  (SELECT COUNT(*) FROM landing_signups),
        'linked', (SELECT COUNT(*) FROM landing_signups WHERE tenant_id IS NOT NULL),
        'ambiguous_null_kept',
            (SELECT COUNT(*) FROM landing_signups s
             WHERE s.tenant_id IS NULL
               AND (SELECT COUNT(*) FROM tenants t WHERE t.company_name = s.company_name) >= 2),
        'orphan_null',
            (SELECT COUNT(*) FROM landing_signups s
             WHERE s.tenant_id IS NULL
               AND (SELECT COUNT(*) FROM tenants t WHERE t.company_name = s.company_name) = 0),
        -- 🔧 I-2 교정 2026-10-07 — 후보 1건이었지만 가입서 status 가 정상 계열이 아니라 **제외**한 건수.
        --   반려·취소·미승인분을 키로 굳히지 않았다는 증거. 0 이면 제외된 것이 없다는 뜻.
        'excluded_by_status',
            (SELECT COUNT(*) FROM landing_signups s
             WHERE s.tenant_id IS NULL
               AND s.status NOT IN ('approved', 'active')
               AND (SELECT COUNT(*) FROM tenants t WHERE t.company_name = s.company_name) = 1)
    ),
    NULL, UTC_TIMESTAMP();
