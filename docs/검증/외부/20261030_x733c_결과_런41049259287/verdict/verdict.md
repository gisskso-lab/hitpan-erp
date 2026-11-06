# X733c MySqlConnector 실행 대조 — 비교 결과

> 🔴 합성 대조군 ② 생존 = 장치가 「`--` 바로 뒤 줄바꿈 다음 줄의 매개변수」라는 **그 특정 차이**를 잡는다는 증거일 뿐이다. 실제 SQL 집합 전체에서 차이가 없다는(누락 없음) 증명이 아니다.
> 🔴 「같음(비교 항목 N개)」 = 아래 표의 N개 항목이 칸 사이 같다는 뜻뿐이다. 「정상 적용」「호환」「안전」 판정이 아니다. 같은 거부 · 같은 적용 수는 「같다」 일 뿐 「정상」 이 아니다.
> 🔴 항목마다 「보장하는 범위 / 못 보는 범위」 를 함께 읽는다. 못 보는 범위는 이 런이 재지 않은 것이다.
> 🔴 무효여도 네 칸 원결과 · 실제 로드된 조립품 판 · 줄끝 증거는 칸 산출물(x733c-cell-*)에 그대로 남는다.

## 판정: **같음(비교 항목 24개)** (종료 코드 0)

## 칸 × 전제

| 칸 | 전제 | 로드된 조립품 | 줄끝 | NU1902 | 대조군 ② | 실패한 전제 |
|---|---|---|---|---|---|---|
| mariadb-2.5.0-crlf | 통과 | 2.5.0+a86c07e8d04166a0334be6a2ce16351cf3804c9e | crlf · 단언 True | 4 | a=7,b=7 |  |
| mariadb-2.5.0-lf | 통과 | 2.5.0+a86c07e8d04166a0334be6a2ce16351cf3804c9e | lf · 단언 True | 4 | a=7,b=7 |  |
| mariadb-2.6.2-crlf | 통과 | 2.6.2+775689bb3e1ab55d16171e0795264fa746e364c4 | crlf · 단언 True | 0 | a=7,b=7 |  |
| mariadb-2.6.2-lf | 통과 | 2.6.2+775689bb3e1ab55d16171e0795264fa746e364c4 | lf · 단언 True | 0 | a=7,b=NULL |  |
| mysql8-2.5.0-lf | 통과 | 2.5.0+a86c07e8d04166a0334be6a2ce16351cf3804c9e | lf · 단언 True | 4 | a=7,b=7 |  |
| mysql8-2.6.2-lf | 통과 | 2.6.2+775689bb3e1ab55d16171e0795264fa746e364c4 | lf · 단언 True | 0 | a=7,b=NULL |  |

## 비교 항목 (항목마다 보장하는 범위 / 못 보는 범위)

| # | 항목 | 축 | 결과 | 보장하는 범위 | 못 보는 범위 | 상세 |
|---|---|---|---|---|---|---|
| E1 | ERP 깨짐 집합 | 4칸 전체 | **같음** | 113 마이그를 출하 DDL 위에서 G-W13-5 순서(번호순 · 한 건씩 · 두 바퀴)로 실물 MigrationRunner 에 태운 결과가 칸 사이 같음 | 성공 = 예외 없음일 뿐 「정상 적용」 아님 · 번호순 한 순서만(역순·다른 순서 미측정) · 업무 SQL(C# 안 문장) 미포함 · 윈도우 고객 PC·실제 설치판 미포함 | 기준 mariadb-2.5.0-crlf |
| E2 | ERP 깨짐 사유(회차 · 오류 번호 · 예외 형 · 메시지 첫 줄) | 4칸 전체 | **같음** | 깨진 파일마다 러너가 보고한 사유 모양이 칸 사이 같음 | 메시지 첫 줄만 봄(둘째 줄 이하 미비교) · 사유가 같아도 원인이 같다는 증명 아님 | 기준 mariadb-2.5.0-crlf |
| E3 | ERP 적용 건수 | 4칸 전체 | **같음** | 러너가 「적용」 으로 보고한 건수가 칸 사이 같음 | 건수만 봄 — 적용된 내용의 의미 동일성은 E4·E5·B1 로만 일부 봄 | 기준 mariadb-2.5.0-crlf |
| E4 | ERP 파일별 회차 결과(성공 · 적용 · 실패 위치) | 4칸 전체 | **같음** | 113 파일 각각의 1·2회차 성공 여부와 실패 파일 위치가 칸 사이 같음 | 결과 표지만 봄(실행 시간·경고·영향 행 수 미비교) | 기준 mariadb-2.5.0-crlf |
| E5 | ERP 확장 모양(표 · 칸 형 · 널 · DEFAULT · COMMENT · 인덱스) | 4칸 전체 | **같음** | information_schema 의 표·칸(형·널·기본값·설명)·인덱스(이름·고유·칸 순서) 집합이 칸 사이 같음 (schema_migrations 제외) | 트리거·루틴·뷰·이벤트·FK 조건·CHECK·데이터 행·표 엔진/정렬 규칙·AUTO_INCREMENT 값 미비교 | 기준 mariadb-2.5.0-crlf |
| E6 | 대조군 ① DB-16 원본 거부 대조 | 4칸 전체 | **같음** | 원본 DB-16(줄끝 축만 바뀜)을 한 건만 실물 러너에 태웠을 때 거부 모양(성공 여부·적용 0·예외 형·메시지 첫 줄)이 칸 사이 같음 | 실제 설치/업데이트 경로의 기존 한계(DELIMITER 를 제품 적용기가 못 돌림)는 이 칸이 재지 않는다 — 아래 별도 칸 | 기준 mariadb-2.5.0-crlf |
| E7 | 백오피스 적용 파일 수 · _schema_migrations 파일명 집합 | 4칸 전체 | **같음** | 실물 SchemaMigrator.ApplyAsync(빈 DB · installer/backoffice)가 남긴 이력 파일명·건수가 칸 사이 같음 | `${VAR}` 치환 파일은 칸에 환경 변수가 없어 건너뜀(운영 동작 미측정) · 운영 MySQL 8 설정·연결 옵션 미포함 · MariaDB 칸은 통째 실행 경로만, MySQL 8 칸은 자체 분리 경로만 돈다 | 기준 mariadb-2.5.0-crlf |
| E8 | 백오피스 적용 오류 | 4칸 전체 | **같음** | 적용기가 던진 예외 유무·모양이 칸 사이 같음 | 메시지 첫 줄만 봄 · `${VAR}` 치환 파일은 칸에 환경 변수가 없어 건너뜀(운영 동작 미측정) · 운영 MySQL 8 설정·연결 옵션 미포함 · MariaDB 칸은 통째 실행 경로만, MySQL 8 칸은 자체 분리 경로만 돈다 | 기준 mariadb-2.5.0-crlf |
| E9 | 백오피스 확장 모양 | 4칸 전체 | **같음** | 백오피스 DB 의 표·칸(형·널·기본값·설명)·인덱스 집합이 칸 사이 같음 (_schema_migrations 제외) | 트리거·루틴·뷰·FK 조건·데이터 행 미비교 · `${VAR}` 치환 파일은 칸에 환경 변수가 없어 건너뜀(운영 동작 미측정) · 운영 MySQL 8 설정·연결 옵션 미포함 · MariaDB 칸은 통째 실행 경로만, MySQL 8 칸은 자체 분리 경로만 돈다 | 기준 mariadb-2.5.0-crlf |
| B1-lf | ERP 서버 수신 문장 바이트(general_log · 파일 · 회차별 sha256 열) | 판 축 · lf | **같음** | 실물 러너 연결이 서버에 보낸 COM_QUERY 를 general_log 가 저장한 바이트가 판 사이 같음 — 드라이버가 문장을 바꿨다면 여기서 드러남 | general_log argument 칸 문자셋 변환(칸 기록 server.general_log_argument 참조 · utf8mb3 등이면 4바이트 글자는 저장 전에 바뀜) · 준비문(prepared) 경로 · 매개변수 있는 업무 문장(대조군 ② 한 줄만) · 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mariadb-2.5.0-lf |
| B2-lf | ERP 러너 체크섬(schema_migrations.checksum) | 판 축 · lf | **같음** | 러너가 읽은 문자열의 sha256(줄끝 포함)이 판 사이 같음 | 줄끝이 다른 칸끼리는 당연히 다름 — 비교 안 함 | 기준 mariadb-2.5.0-lf |
| B3-lf | SQL 파일 sha256(변환 뒤) | 판 축 · lf | **같음** | 실행기에 들어간 파일 바이트가 판 사이 같음 | 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mariadb-2.5.0-lf |
| BB1-lf | 백오피스 content_hash | 판 축 · lf | **같음** | 백오피스 적용기가 읽은 원본 문자열 sha256 이 판 사이 같음 | 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mariadb-2.5.0-lf |
| BB2-lf | 백오피스 서버 수신 문장 바이트(적용 전체의 문장 순서 sha256 열) | 판 축 · lf | **같음** | SchemaMigrator 연결이 서버에 보낸 문장을 general_log 가 저장한 바이트 열이 판 사이 같음 | 파일별로 끊지 않은 전체 순서 · general_log 문자셋 변환 · 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mariadb-2.5.0-lf |
| B1-crlf | ERP 서버 수신 문장 바이트(general_log · 파일 · 회차별 sha256 열) | 판 축 · crlf | **같음** | 실물 러너 연결이 서버에 보낸 COM_QUERY 를 general_log 가 저장한 바이트가 판 사이 같음 — 드라이버가 문장을 바꿨다면 여기서 드러남 | general_log argument 칸 문자셋 변환(칸 기록 server.general_log_argument 참조 · utf8mb3 등이면 4바이트 글자는 저장 전에 바뀜) · 준비문(prepared) 경로 · 매개변수 있는 업무 문장(대조군 ② 한 줄만) · 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mariadb-2.5.0-crlf |
| B2-crlf | ERP 러너 체크섬(schema_migrations.checksum) | 판 축 · crlf | **같음** | 러너가 읽은 문자열의 sha256(줄끝 포함)이 판 사이 같음 | 줄끝이 다른 칸끼리는 당연히 다름 — 비교 안 함 | 기준 mariadb-2.5.0-crlf |
| B3-crlf | SQL 파일 sha256(변환 뒤) | 판 축 · crlf | **같음** | 실행기에 들어간 파일 바이트가 판 사이 같음 | 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mariadb-2.5.0-crlf |
| BB1-crlf | 백오피스 content_hash | 판 축 · crlf | **같음** | 백오피스 적용기가 읽은 원본 문자열 sha256 이 판 사이 같음 | 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mariadb-2.5.0-crlf |
| BB2-crlf | 백오피스 서버 수신 문장 바이트(적용 전체의 문장 순서 sha256 열) | 판 축 · crlf | **같음** | SchemaMigrator 연결이 서버에 보낸 문장을 general_log 가 저장한 바이트 열이 판 사이 같음 | 파일별로 끊지 않은 전체 순서 · general_log 문자셋 변환 · 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mariadb-2.5.0-crlf |
| M7 | 백오피스 적용 파일 수 · _schema_migrations 파일명 집합 | MySQL 8 · 판 축 · lf | **같음** | 실물 SchemaMigrator.ApplyAsync(빈 DB · installer/backoffice)가 남긴 이력 파일명·건수가 칸 사이 같음 | `${VAR}` 치환 파일은 칸에 환경 변수가 없어 건너뜀(운영 동작 미측정) · 운영 MySQL 8 설정·연결 옵션 미포함 · MariaDB 칸은 통째 실행 경로만, MySQL 8 칸은 자체 분리 경로만 돈다 | 기준 mysql8-2.5.0-lf |
| M8 | 백오피스 적용 오류 | MySQL 8 · 판 축 · lf | **같음** | 적용기가 던진 예외 유무·모양이 칸 사이 같음 | 메시지 첫 줄만 봄 · `${VAR}` 치환 파일은 칸에 환경 변수가 없어 건너뜀(운영 동작 미측정) · 운영 MySQL 8 설정·연결 옵션 미포함 · MariaDB 칸은 통째 실행 경로만, MySQL 8 칸은 자체 분리 경로만 돈다 | 기준 mysql8-2.5.0-lf |
| M9 | 백오피스 확장 모양 | MySQL 8 · 판 축 · lf | **같음** | 백오피스 DB 의 표·칸(형·널·기본값·설명)·인덱스 집합이 칸 사이 같음 (_schema_migrations 제외) | 트리거·루틴·뷰·FK 조건·데이터 행 미비교 · `${VAR}` 치환 파일은 칸에 환경 변수가 없어 건너뜀(운영 동작 미측정) · 운영 MySQL 8 설정·연결 옵션 미포함 · MariaDB 칸은 통째 실행 경로만, MySQL 8 칸은 자체 분리 경로만 돈다 | 기준 mysql8-2.5.0-lf |
| BB1-lf (MySQL 8) | 백오피스 content_hash | 판 축 · lf (MySQL 8) | **같음** | 백오피스 적용기가 읽은 원본 문자열 sha256 이 판 사이 같음 | 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mysql8-2.5.0-lf |
| BB2-lf (MySQL 8) | 백오피스 서버 수신 문장 바이트(적용 전체의 문장 순서 sha256 열) | 판 축 · lf (MySQL 8) | **같음** | SchemaMigrator 연결이 서버에 보낸 문장을 general_log 가 저장한 바이트 열이 판 사이 같음 | 파일별로 끊지 않은 전체 순서 · general_log 문자셋 변환 · 줄끝이 다른 칸끼리는 비교 안 함 | 기준 mysql8-2.5.0-lf |

## 기록만 (판정 안 함)

- **대조군 ② 결과(MariaDB)**: {"2.5.0×crlf": "a=7,b=7", "2.5.0×lf": "a=7,b=7", "2.6.2×crlf": "a=7,b=7", "2.6.2×lf": "a=7,b=NULL"}
- **대조군 ② 결과(MySQL 8)**: {"2.5.0×lf": "a=7,b=7", "2.6.2×lf": "a=7,b=NULL"}
- **대조군 ②′ .sql 경로 변형(MariaDB · 기록만)**: {"mariadb-2.5.0-crlf": {"applied": false, "err_no": null, "ex_msg1": null, "ex_type": null, "ok": true, "runner_msg1": null}, "mariadb-2.5.0-lf": {"applied": false, "err_no": null, "ex_msg1": null, "ex_type": null, "ok": true, "runner_msg1": null}, "mariadb-2.6.2-crlf": {"applied": false, "err_no": null, "ex_msg1": null, "ex_type": null, "ok": true, "runner_msg1": null}, "mariadb-2.6.2-lf": {"applied": false, "err_no": null, "ex_msg1": null, "ex_type": null, "ok": true, "runner_msg1": null}}
- **DB-16 — 실제 설치/업데이트 경로의 기존 한계**: 실제 설치/업데이트 경로의 기존 한계 (판정 안 함 · 이 런이 재는 것 아님): DB-16 은 `DELIMITER` 를 쓰는 유일한 마이그이고, 제품 적용기(MigrationRunner → MySqlConnector)로는 판과 무관하게 돌지 않는다(G-W13-5 명단 `KnownNotReapplyable` · 정적 회신 §1-3). 표의 「대조군 ① DB-16 원본 거부 대조」 는 이 한계를 재는 칸이 아니라, 그 거부 모양이 판·줄끝 사이에서 바뀌는지만 본다.

