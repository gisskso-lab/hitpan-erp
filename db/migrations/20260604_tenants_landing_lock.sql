-- tenants 잠금 플래그 (사장님 결재 2026-06-04, 헌법 #35 자동 반영 + 수정 금지)
--
-- 옵션 C 정합: 본사 평문 보관 0건.
-- ERP 첫 실행 시 라이선스 키 + 사업자번호 입력 → 백오피스 API가 해시 검증만 →
-- 통과 시 ERP가 사업자등록증 정보를 자체적으로 tenants에 박제 + is_locked_from_landing=1
-- 잠긴 필드: company_name, biz_no, ceo_name (랜딩 가입에서 들어온 회사정보)
-- 수정하려면 랜딩에서 사업자등록증 재등록 (헌법 #35 정합)

ALTER TABLE tenants
  ADD COLUMN is_locked_from_landing TINYINT(1) NOT NULL DEFAULT 0
    COMMENT '1=랜딩에서 들어온 회사정보, ERP 내 수정 금지 (헌법 #35)' AFTER status,
  ADD COLUMN bootstrap_at DATETIME(6) NULL
    COMMENT 'ERP 첫 부팅 자동 반영 시점 (헌법 #20 워크플로우 검증용)' AFTER is_locked_from_landing;
