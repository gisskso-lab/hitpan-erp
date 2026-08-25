-- DB-110 — 창고에 우편번호·주소 추가 (20260825작19)
--
-- 🔴 사장님 지시 (2026-08-25)
--   "창고관리 : 창고추가-> 위치에 카카오맵 zip코드로 정확한 주소 넣고,
--    카카오네비 자동연결(업체마스터 참고)"
--
-- 🔴 왜 컬럼을 늘리나
--   warehouses 에는 자유입력 `location` 한 칸뿐이다. 우편번호로 찾은 주소를
--   거기 뭉뚱그려 넣으면 우편번호만 따로 쓸 수가 없고, 네비 연동도
--   "주소처럼 생긴 문자열" 을 추측해야 한다.
--   업체마스터(partners)는 이미 zip_code · address · address_detail 로 나눠 갖고 있고
--   사장님이 "업체마스터 참고" 라 하셨다 — 같은 축으로 맞춘다.
--
-- 🔴 기존 `location` 은 지우지 않는다 (헌법 #1 · #37)
--   이미 손으로 적어둔 위치 메모가 들어 있을 수 있다. "안 읽힌다 ≠ 잔재" 다.
--   새 컬럼은 **추가만** 하고, 화면은 우편번호로 찾은 주소가 있으면 그것을 우선 보여준다.
--
-- 🔴 왜 NULL 허용인가
--   이미 등록된 창고에는 주소가 없다. NOT NULL 로 잡으면 기존 행이 마이그에서 막힌다.
--   주소는 나중에 채워도 되는 값이라 없는 상태가 정상이다.
--
-- ⚠️ 멱등 — 같은 마이그가 두 번 돌아도 안전해야 한다.
--   ALTER TABLE ... ADD COLUMN 은 IF NOT EXISTS 를 MariaDB 10.0+ 에서 지원한다.
--   (DB-105 가 같은 방식을 이미 쓴다)

ALTER TABLE `warehouses`
  ADD COLUMN IF NOT EXISTS `zip_code` VARCHAR(10) NULL COMMENT '우편번호 (우편번호 찾기로 채운다)' AFTER `location`,
  ADD COLUMN IF NOT EXISTS `address` VARCHAR(255) NULL COMMENT '주소 (우편번호 찾기로 채운다 · 사람이 직접 고치지 않는다)' AFTER `zip_code`,
  ADD COLUMN IF NOT EXISTS `address_detail` VARCHAR(255) NULL COMMENT '상세주소 (사람이 입력한다)' AFTER `address`;
