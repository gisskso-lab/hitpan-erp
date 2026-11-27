#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────
# G16 교체 시간 실측 장치 — W-G1-52 (가-2) 한 트랜잭션 지우고 붓기
#
# 근거: 설계 §15-2 · §15-9 G16 · R11 / 작업지시서 §7-1 K2 · §7-3 #12
#       (문턱 X 숫자 = PM 이 이 결과를 보고 정함 · 이 장치는 재기만 하고 판정 0)
#
# 🔴 CI 러너 전용. 개발 PC · 고객 PC · 운영 DB 에서 돌리지 않는다.
#    docker 로 mariadb:11.4 컨테이너를 직접 띄운다(서비스 컨테이너가 아니어야 강제 종료 · 재기동이 된다).
#    사용: G16_OUT=<출력 디렉터리> bash scripts/measure/g16-swap-measure.sh
#
# 표본마다(빈 datadir 에서 새로):
#   ① root = 준비만: 출하 DDL 적재 · 출하 GRANT 꼴 제한 계정 만들기 · 자료 붓기(가짜 업무 자료)
#   ② G0: 제한 계정 CREATE DATABASE → 1044 여야 함 (아니면 측정 무효 · 멈춤)
#   ③ 제한 계정: rs_new__<표> 만들기(LIKE · 외래키 없음 확인) · 붓기
#   ④ 설정마다(버퍼풀 · binlog) 재기동 → 교체 트랜잭션 → 시간 · 1206 · undo · ibdata1 · ibtmp1 · binlog · 디스크 최대
#   ⑤ 가장 큰 표본: R11 — 교체 도중 docker kill → 재기동 → 되돌림 끝 시간 · 행 수 · 체크섬 대조
# ──────────────────────────────────────────────────────────────────────────
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
GEN=(python3 "$HERE/g16_gen.py")
DDL="$ROOT/installer/hitpan_db_clean.sql"
OUT=${G16_OUT:-$ROOT/g16-out}
IMAGE=${G16_IMAGE:-mariadb:11.4}
SAMPLES=${G16_SAMPLES:-"50 250 1024 2048"}          # 명목 MB(교체 대상 업무 자료)
CONFIGS=${G16_CONFIGS:-"bp1g_nobin bp128m_nobin bp1g_bin"}
SEED_BP=${G16_SEED_BP:-2G}
CALIB_ROWS=${G16_CALIB_ROWS:-20000}
SWAP_TIMEOUT=${G16_SWAP_TIMEOUT:-2700}              # 교체 1회 상한(초) — 넘으면 「시간 초과」 로 기록
SKIP_OVER=${G16_SKIP_OVER:-1800}                    # 이 초를 넘은 설정은 더 큰 표본에서 건너뜀
PURGE_WAIT=${G16_PURGE_WAIT:-900}
ROLLBACK_WAIT=${G16_ROLLBACK_WAIT:-3600}
R11=${G16_R11:-1}
R11_CONFIG=${G16_R11_CONFIG:-bp1g_nobin}
KILL_FRAC=${G16_KILL_FRAC:-0.6}
CN=g16db
DB=g16
RSU=hitpan_rs_t

mkdir -p "$OUT"
RES="$OUT/results.jsonl"
: > "$RES"
LOG="$OUT/run.log"
log() { echo "[$(date -u +%H:%M:%S)] $*" | tee -a "$LOG"; }
# 중간에 멈춰도 그때까지 잰 것은 표로 남긴다
finish() { "${GEN[@]}" report --results "$RES" --md-out "$OUT/result.md" --json-out "$OUT/result.json" 2>/dev/null || true; }
trap finish EXIT

# 비번은 실행 때마다 새로 뽑는다(코드에 값 0)
ROOTPW=$(openssl rand -hex 16)
RSPW=$(openssl rand -hex 16)

# ── 자리: 가장 빈 파일시스템 ──
pick_base() {
  local a_root a_mnt
  a_root=$(df -B1 --output=avail / | tail -1 | tr -d ' ')
  a_mnt=0
  if [ -d /mnt ]; then a_mnt=$(df -B1 --output=avail /mnt | tail -1 | tr -d ' '); fi
  if [ "$a_mnt" -gt "$a_root" ]; then echo /mnt/g16; else echo "$HOME/g16"; fi
}
BASE=${G16_BASE:-$(pick_base)}
DATA="$BASE/data"
TMPD="$BASE/tmp"
sudo mkdir -p "$BASE"
sudo chown "$(id -u):$(id -g)" "$BASE"

cfg_args() {
  case "$1" in
    bp1g_nobin)   echo "--innodb-buffer-pool-size=1G" ;;
    bp128m_nobin) echo "--innodb-buffer-pool-size=128M" ;;
    bp1g_bin)     echo "--innodb-buffer-pool-size=1G --log-bin=g16-bin --server-id=1" ;;
    bp1g_binrow)  echo "--innodb-buffer-pool-size=1G --log-bin=g16-bin --server-id=1 --binlog-format=ROW" ;;
    seed)         echo "--innodb-buffer-pool-size=$SEED_BP" ;;
    *) echo "::error::모르는 설정 $1" >&2; return 1 ;;
  esac
}

qr() { docker exec -i -e MYSQL_PWD="$ROOTPW" "$CN" mariadb -uroot --default-character-set=utf8mb4 -N -B "$@"; }
qu() { docker exec -i -e MYSQL_PWD="$RSPW" "$CN" mariadb -u"$RSU" --default-character-set=utf8mb4 -N -B "$@"; }

wait_ready() { # $1 = since(유닉스 초) $2 = 상한 초 → 걸린 초를 READY_SEC 에
  local since=$1 lim=$2 t0 now
  t0=$(date +%s.%N)
  while :; do
    if docker logs --since "$since" "$CN" 2>&1 | grep -q "port: 3306"; then
      if qr -e "SELECT 1" >/dev/null 2>&1; then break; fi
    fi
    now=$(date +%s)
    if [ $((now - ${t0%.*})) -gt "$lim" ]; then log "::error::DB 기동 대기 상한 넘음"; docker logs --tail 30 "$CN" 2>&1 | tee -a "$LOG"; return 1; fi
    sleep 1
  done
  READY_SEC=$(awk -v a="$t0" -v b="$(date +%s.%N)" 'BEGIN{printf "%.1f", b-a}')
}

start_db() { # $1 = 설정 이름 (datadir 그대로 · 컨테이너만 새로)
  local args since
  args=$(cfg_args "$1") || return 1
  docker rm -f "$CN" >/dev/null 2>&1
  since=$(( $(date +%s) - 1 ))
  # shellcheck disable=SC2086
  docker run -d --name "$CN" -e MARIADB_ROOT_PASSWORD="$ROOTPW" \
    -v "$DATA:/var/lib/mysql" -v "$TMPD:/g16tmp" "$IMAGE" \
    --tmpdir=/g16tmp --character-set-server=utf8mb4 --collation-server=utf8mb4_unicode_ci \
    --max-connections=100 $args >/dev/null || return 1
  wait_ready "$since" 900
}

stop_db() { docker stop -t 900 "$CN" >/dev/null 2>&1; docker rm -f "$CN" >/dev/null 2>&1; }

fresh_datadir() {
  stop_db
  sudo rm -rf "$DATA" "$TMPD"
  mkdir -p "$DATA" "$TMPD"
  chmod 1777 "$TMPD"
}

sumglob() { sudo find "$DATA" -maxdepth 1 -name "$1" -printf '%s\n' 2>/dev/null | awk '{s+=$1} END{printf "%d", s+0}'; }

snap() { # $1 = 출력 JSON 파일
  local ib tmp redo undo bin tot tmpd used
  ib=$(sumglob ibdata1); tmp=$(sumglob ibtmp1); redo=$(sumglob 'ib_logfile*')
  undo=$(sumglob 'undo0*'); bin=$(sumglob 'g16-bin.*')
  tot=$(sudo du -sb "$DATA" | cut -f1); tmpd=$(sudo du -sb "$TMPD" | cut -f1)
  used=$(df -B1 --output=used "$BASE" | tail -1 | tr -d ' ')
  printf '{"ibdata1":%s,"ibtmp1":%s,"redo":%s,"undo":%s,"binlog":%s,"datadir":%s,"tmpdir":%s,"fs_used":%s}\n' \
    "$ib" "$tmp" "$redo" "$undo" "$bin" "$tot" "$tmpd" "$used" > "$1"
}

sampler_start() { # $1 = 샘플 파일
  rm -f "$OUT/.stop"
  ( while [ ! -f "$OUT/.stop" ]; do
      echo "$(df -B1 --output=used "$BASE" | tail -1 | tr -d ' ') $(sudo du -sb "$DATA" 2>/dev/null | cut -f1)" >> "$1"
      sleep 1
    done ) &
  SAMP_PID=$!
}
sampler_stop() { touch "$OUT/.stop"; wait "$SAMP_PID" 2>/dev/null; rm -f "$OUT/.stop"; }

wait_purge() { # → PURGE_SEC
  local t0 h
  t0=$(date +%s)
  while :; do
    h=$(qr -e "SHOW ENGINE INNODB STATUS" 2>/dev/null | grep -o 'History list length [0-9]*' | awk '{print $4}')
    if [ -n "$h" ] && [ "$h" -lt 100 ]; then break; fi
    if [ $(( $(date +%s) - t0 )) -gt "$PURGE_WAIT" ]; then PURGE_SEC="\">${PURGE_WAIT} (history=${h:-?})\""; return; fi
    sleep 2
  done
  PURGE_SEC=$(( $(date +%s) - t0 ))
}

wait_rollback() { # $1 = since · $2 = 기준 시각(초.나노) → RB_TRX0_SEC · RB_LOG_SEC · RB_LOCK_SEC
  local since=$1 tk=$2 t0 n
  t0=$(date +%s)
  RB_TRX0_SEC='"-"'; RB_LOG_SEC='"-"'; RB_LOCK_SEC='"-"'
  while :; do
    # 기능 탐침: 가장 큰 잎 표를 잠금 읽기로 잡을 수 있게 된 시각(되돌림이 쥔 줄 잠금이 풀림)
    if [ "$RB_LOCK_SEC" = '"-"' ] && qr "$DB" -e "SET SESSION innodb_lock_wait_timeout=2; START TRANSACTION; SELECT 1 FROM \`$PROBE_T\` LIMIT 20000 FOR UPDATE; ROLLBACK;" >/dev/null 2>&1; then
      RB_LOCK_SEC=$(awk -v a="$tk" -v b="$(date +%s.%N)" 'BEGIN{printf "%.1f", b-a}')
    fi
    if [ "$RB_LOG_SEC" = '"-"' ] && docker logs --since "$since" "$CN" 2>&1 | grep -qE "Rollback of non-prepared transactions completed|Rolled back recovered transaction"; then
      RB_LOG_SEC=$(awk -v a="$tk" -v b="$(date +%s.%N)" 'BEGIN{printf "%.1f", b-a}')
    fi
    n=$(qr -e "SELECT COUNT(*) FROM information_schema.INNODB_TRX" 2>/dev/null)
    if [ "$RB_TRX0_SEC" = '"-"' ] && [ "${n:-x}" = "0" ]; then
      RB_TRX0_SEC=$(awk -v a="$tk" -v b="$(date +%s.%N)" 'BEGIN{printf "%.1f", b-a}')
    fi
    if [ "$RB_TRX0_SEC" != '"-"' ] && [ "$RB_LOCK_SEC" != '"-"' ] && [ "$RB_LOG_SEC" != '"-"' ]; then return 0; fi
    if [ "$RB_TRX0_SEC" != '"-"' ] && [ "$RB_LOCK_SEC" != '"-"' ] && [ $(( $(date +%s) - t0 )) -gt 120 ]; then return 0; fi
    if [ $(( $(date +%s) - t0 )) -gt "$ROLLBACK_WAIT" ]; then return 1; fi
    sleep 1
  done
}

count_map() { # $1 = prefix · $2 = 출력 파일
  "${GEN[@]}" counts --plan "$OUT/plan.json" --prefix "$1" > "$OUT/counts_q.sql"
  qr "$DB" < "$OUT/counts_q.sql" | sort > "$2"
}

prep_db() { # 출하 DDL · 제한 계정 · G0
  qr -e "CREATE DATABASE $DB CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci; CREATE DATABASE g16_aux;" || return 1
  qr "$DB" < "$DDL" 2> "$OUT/ddl.err" || { log "::error::출하 DDL 적재 실패"; tail -5 "$OUT/ddl.err" | tee -a "$LOG"; return 1; }
  # 출하 GRANT 꼴 (HitPan-Universal.iss :2259 — GRANT ALL ON {자기DB}.* TO '{계정}'@'localhost')
  #   docker exec 안 소켓 접속 = localhost 이므로 출하와 같은 @'localhost' 그대로 쓴다.
  qr <<SQL || return 1
CREATE USER '$RSU'@'localhost' IDENTIFIED BY '$RSPW';
GRANT ALL ON \`$DB\`.* TO '$RSU'@'localhost';
SQL
  local got verdict
  got=$(qu -e "CREATE DATABASE g16_g0_probe" 2>&1 | head -1)
  if echo "$got" | grep -q "ERROR 1044"; then verdict="초록"; else verdict="빨강"; fi
  qu -e "SHOW GRANTS" > "$OUT/grants.txt" 2>&1
  "${GEN[@]}" mkrec --kv kind=g0 --kv "got=\"$(echo "$got" | tr -d '"\\' | cut -c1-120)\"" --kv "verdict=\"$verdict\"" \
    --kv "grants=$(python3 -c 'import json,sys;print(json.dumps(open(sys.argv[1],encoding="utf-8").read()))' "$OUT/grants.txt")" >> "$RES"
  log "G0: $verdict ($got)"
  [ "$verdict" = "초록" ] || { log "::error::G0 빨강 — 제한 계정이 DB 를 만들 수 있다 = 출하 꼴 아님 · 측정 무효"; return 1; }
}

dump_meta() {
  mkdir -p "$OUT/meta"
  local W="TABLE_SCHEMA='$DB'"
  qr -e "SELECT TABLE_NAME FROM information_schema.TABLES WHERE $W AND TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME" > "$OUT/meta/tables.tsv"
  qr -e "SELECT c.TABLE_NAME,c.COLUMN_NAME,c.ORDINAL_POSITION,c.DATA_TYPE,c.COLUMN_TYPE,c.IS_NULLABLE,c.COLUMN_KEY,c.EXTRA,IFNULL(c.CHARACTER_MAXIMUM_LENGTH,0),IFNULL(c.NUMERIC_PRECISION,0),IFNULL(c.NUMERIC_SCALE,0),IFNULL(c.IS_GENERATED,'NEVER') FROM information_schema.COLUMNS c JOIN information_schema.TABLES t ON t.TABLE_SCHEMA=c.TABLE_SCHEMA AND t.TABLE_NAME=c.TABLE_NAME WHERE c.$W AND t.TABLE_TYPE='BASE TABLE' ORDER BY c.TABLE_NAME,c.ORDINAL_POSITION" > "$OUT/meta/cols.tsv"
  qr -e "SELECT TABLE_NAME,COLUMN_NAME,SEQ_IN_INDEX FROM information_schema.STATISTICS WHERE $W AND INDEX_NAME='PRIMARY' ORDER BY TABLE_NAME,SEQ_IN_INDEX" > "$OUT/meta/pk.tsv"
  qr -e "SELECT DISTINCT TABLE_NAME,COLUMN_NAME FROM information_schema.STATISTICS WHERE $W AND NON_UNIQUE=0 AND INDEX_NAME<>'PRIMARY'" > "$OUT/meta/uniq.tsv"
  qr -e "SELECT CONSTRAINT_NAME,TABLE_NAME,COLUMN_NAME,ORDINAL_POSITION,REFERENCED_TABLE_NAME,REFERENCED_COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE $W AND REFERENCED_TABLE_NAME IS NOT NULL ORDER BY TABLE_NAME,CONSTRAINT_NAME,ORDINAL_POSITION" > "$OUT/meta/fk.tsv"
  qr -e "SELECT TRIGGER_NAME,EVENT_MANIPULATION,EVENT_OBJECT_TABLE,ACTION_TIMING FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA='$DB'" > "$OUT/meta/triggers.tsv"
}

make_nums() { # $1 = 최대 n — 필요한 자릿수만큼만 digits 를 곱한다(8단 고정이면 1억 조합을 훑음)
  local k=${#1} i sel="1" frm="" whr="0" p=1
  for ((i=0; i<k; i++)); do
    sel="$sel + $p*d$i.d"; whr="$whr + $p*d$i.d"
    frm="${frm:+$frm, }digits d$i"; p=$((p * 10))
  done
  qr g16_aux <<SQL
DROP TABLE IF EXISTS digits; DROP TABLE IF EXISTS nums;
CREATE TABLE digits (d TINYINT PRIMARY KEY) ENGINE=InnoDB;
INSERT INTO digits VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9);
CREATE TABLE nums (n INT PRIMARY KEY) ENGINE=InnoDB;
INSERT INTO nums SELECT $sel AS n FROM $frm WHERE $whr < $1;
SQL
}

seed_and_measure_sizes() { # $1 = seed SQL 파일 · $2 = 꼬리표
  local t0 t1
  t0=$(date +%s)
  qr --force "$DB" < "$1" > "$OUT/seed_$2.out" 2> "$OUT/seed_$2.err"
  t1=$(date +%s)
  log "자료 붓기 $2: $((t1 - t0))초 · 오류 줄 $(grep -c '^ERROR' "$OUT/seed_$2.err")"
  "${GEN[@]}" analyze --plan "$OUT/plan.json" --all > "$OUT/analyze.sql"
  qr "$DB" < "$OUT/analyze.sql" > /dev/null
  qr -e "SELECT TABLE_NAME, DATA_LENGTH+INDEX_LENGTH FROM information_schema.TABLES WHERE TABLE_SCHEMA='$DB' AND TABLE_TYPE='BASE TABLE'" > "$OUT/sizes_$2.tsv"
  count_map "" "$OUT/counts_$2.tsv"
}

# ═════════════════════════════ 0. 환경 ═════════════════════════════
log "자리=$BASE · 표본=$SAMPLES · 설정=$CONFIGS · 교체 상한=${SWAP_TIMEOUT}s"
docker pull -q "$IMAGE" >/dev/null || { log "::error::이미지 받기 실패"; exit 1; }
"${GEN[@]}" mkrec --kv kind=env --kv "image=\"$IMAGE\"" --kv "nproc=$(nproc)" \
  --kv "mem_mb=$(free -m | awk '/Mem:/{print $2}')" \
  --kv "disk_avail_gb=$(df -BG --output=avail "$BASE" | tail -1 | tr -dc 0-9)" \
  --kv "kernel=\"$(uname -r)\"" >> "$RES"

# ═════════════════════════════ 1. 보정(행당 바이트) ═════════════════════════════
fresh_datadir
start_db seed || exit 1
qr -e "SELECT VERSION(), @@innodb_undo_tablespaces, @@innodb_log_file_size, @@binlog_format, @@innodb_undo_log_truncate" > "$OUT/server_vars.tsv"
prep_db || exit 1
dump_meta
"${GEN[@]}" plan --meta "$OUT/meta" > "$OUT/plan.json" || exit 1
python3 - "$OUT/plan.json" <<'PY' | tee -a "$LOG"
import json,sys; p=json.load(open(sys.argv[1],encoding='utf-8'))
print('표:', p['counts']); [print('메모:', n) for n in p['notes']]
PY
PROBE_T=$(python3 -c 'import json,sys;p=json.load(open(sys.argv[1],encoding="utf-8"));print((p["leaf_big"] or p["swap_order"])[0])' "$OUT/plan.json")
make_nums "$CALIB_ROWS"
"${GEN[@]}" seed --meta "$OUT/meta" --plan "$OUT/plan.json" --calib "$CALIB_ROWS" > "$OUT/seed_calib.sql"
seed_and_measure_sizes "$OUT/seed_calib.sql" calib
"${GEN[@]}" calib --sizes "$OUT/sizes_calib.tsv" --counts "$OUT/counts_calib.tsv" > "$OUT/bpr.json"
stop_db

# ═════════════════════════════ 2. 표본별 ═════════════════════════════
declare -A SKIP=()
declare -A LASTSEC=()
LAST_SAMPLE=$(echo "$SAMPLES" | awk '{print $NF}')

for S in $SAMPLES; do
  log "══ 표본 ${S}MB ══"
  fresh_datadir
  start_db seed || exit 1
  prep_db || exit 1
  TB=$((S * 1048576))
  "${GEN[@]}" seed --meta "$OUT/meta" --plan "$OUT/plan.json" --target-bytes "$TB" --bpr "$OUT/bpr.json" \
    --targets-out "$OUT/targets_$S.json" > "$OUT/seed_$S.sql"
  MAXN=$(python3 -c 'import json,sys;print(max(json.load(open(sys.argv[1])).values())+1)' "$OUT/targets_$S.json")
  make_nums "$MAXN"
  seed_and_measure_sizes "$OUT/seed_$S.sql" "$S"
  SWAP_BYTES=$(python3 - "$OUT/plan.json" "$OUT/sizes_$S.tsv" <<'PY'
import json,sys; p=json.load(open(sys.argv[1],encoding='utf-8')); sw=set(p['swap'])
print(sum(int(l.split('\t')[1]) for l in open(sys.argv[2],encoding='utf-8') if l.strip() and l.split('\t')[0] in sw))
PY
)
  SWAP_ROWS=$(awk -F'\t' '{s+=$3} END{print s+0}' "$OUT/counts_$S.tsv")
  log "교체 대상 실제 ${SWAP_BYTES}B · ${SWAP_ROWS}행"

  # ③ 제한 계정이 rs_new__ 를 만든다 (설계 §15-3 ③-2 의 「외래키 · 트리거 줄 뺀 표」 흉내 = LIKE)
  "${GEN[@]}" copy --meta "$OUT/meta" --plan "$OUT/plan.json" > "$OUT/copy.sql"
  t0=$(date +%s); qu "$DB" < "$OUT/copy.sql" 2> "$OUT/copy_$S.err"; COPY_RC=$?; t1=$(date +%s)
  log "rs_new__ 붓기(INSERT…SELECT 대용 · 실제 덤프 붓기 아님) $((t1 - t0))초 rc=$COPY_RC"
  FKN=$(qr -e "SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA='$DB' AND TABLE_NAME LIKE 'rs\\_new\\_\\_%' AND REFERENCED_TABLE_NAME IS NOT NULL")
  if [ "${FKN:-1}" != "0" ]; then
    log "rs_new__ 에 외래키 ${FKN}개 — 지움(설계: 변환기가 외래키 줄을 뺌)"
    qr -N -e "SELECT CONCAT('ALTER TABLE \`',TABLE_NAME,'\` DROP FOREIGN KEY \`',CONSTRAINT_NAME,'\`;') FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA='$DB' AND TABLE_NAME LIKE 'rs\\_new\\_\\_%'" | qu "$DB"
  fi
  # 대조군: rs_new__ 잎 표에서 1% 를 빼 「교체됨」 이 행 수로 보이게 한다(root = 준비)
  "${GEN[@]}" diverge --meta "$OUT/meta" --plan "$OUT/plan.json" --slice 0 > "$OUT/diverge0.sql"
  qr "$DB" < "$OUT/diverge0.sql"
  "${GEN[@]}" swap --meta "$OUT/meta" --plan "$OUT/plan.json" > "$OUT/swap.sql"
  stop_db

  for C in $CONFIGS; do
    if [ -n "${SKIP[$C]:-}" ]; then
      "${GEN[@]}" mkrec --kv kind=skip --kv "sample_mb=$S" --kv "config=\"$C\"" --kv "why=\"${SKIP[$C]}\"" >> "$RES"
      log "건너뜀 $S/$C — ${SKIP[$C]}"; continue
    fi
    log "── 교체 $S MB · $C ──"
    start_db "$C" || exit 1
    count_map "rs_new__" "$OUT/cnt_new_${S}_$C.tsv"         # 데움 겸 기대 행 수
    qr -e "SELECT @@innodb_buffer_pool_size, @@log_bin, @@binlog_format" > "$OUT/vars_${S}_$C.tsv"
    snap "$OUT/before_${S}_$C.json"
    SAMPF="$OUT/samples_${S}_$C.txt"; : > "$SAMPF"
    sampler_start "$SAMPF"
    w0=$(date +%s.%N)
    timeout --signal=KILL "$SWAP_TIMEOUT" docker exec -i -e MYSQL_PWD="$RSPW" "$CN" \
      mariadb -u"$RSU" --default-character-set=utf8mb4 --unbuffered -N -B "$DB" < "$OUT/swap.sql" \
      > "$OUT/swap_${S}_$C.out" 2> "$OUT/swap_${S}_$C.err"
    RC=$?
    w1=$(date +%s.%N)
    sampler_stop
    WALL=$(awk -v a="$w0" -v b="$w1" 'BEGIN{printf "%.2f", b-a}')
    RESULT="정상"; EXTRA_KV=()
    if [ "$RC" = "137" ]; then
      RESULT="시간초과(>${SWAP_TIMEOUT}s)"
      log "시간 초과 → 강제 종료 · 재기동 · 되돌림 대기"
      tk=$(date +%s.%N); docker kill "$CN" >/dev/null; since=$(( $(date +%s) - 1 ))
      docker start "$CN" >/dev/null; wait_ready "$since" 1800
      wait_rollback "$since" "$tk"
      EXTRA_KV=(--kv "timeout_ready_sec=$READY_SEC" --kv "timeout_rollback_trx0_sec=$RB_TRX0_SEC" --kv "timeout_rollback_log_sec=$RB_LOG_SEC" --kv "timeout_rollback_lockprobe_sec=$RB_LOCK_SEC")
      SKIP[$C]="더 작은 표본($S MB)에서 시간 초과"
    elif [ "$RC" != "0" ]; then
      if grep -q "ERROR 1206" "$OUT/swap_${S}_$C.err"; then RESULT="1206"; else RESULT="오류(rc=$RC)"; fi
      # 오류로 연결이 끊기면 서버가 트랜잭션을 되돌린다 — 그 시간도 잰다
      e0=$(date +%s)
      while [ "$(qr -e "SELECT COUNT(*) FROM information_schema.INNODB_TRX" 2>/dev/null)" != "0" ]; do
        [ $(( $(date +%s) - e0 )) -gt "$ROLLBACK_WAIT" ] && break; sleep 1
      done
      EXTRA_KV=(--kv "err_rollback_sec=$(( $(date +%s) - e0 ))")
    fi
    snap "$OUT/after_${S}_$C.json"
    count_map "" "$OUT/cnt_live_${S}_$C.tsv"
    if [ "$RESULT" = "정상" ]; then
      if cmp -s "$OUT/cnt_live_${S}_$C.tsv" "$OUT/cnt_new_${S}_$C.tsv"; then CC='"같음"'; else CC='"다름"'; fi
    else CC='"-"'; fi
    PURGE_SEC='"-"'; [ "$RESULT" = "정상" ] && wait_purge
    snap "$OUT/afterpurge_${S}_$C.json"
    PEAK=$(awk -v base="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["fs_used"])' "$OUT/before_${S}_$C.json")" \
      '{if($1>m)m=$1} END{printf "%d", (m>base?m-base:0)}' "$SAMPF")
    PEAKDD=$(awk '{if($2>m)m=$2} END{printf "%d", m+0}' "$SAMPF")
    VARS=$(tr -s '[:space:]' ' ' < "$OUT/vars_${S}_$C.tsv" | tr -dc '[:alnum:] _.-')
    "${GEN[@]}" mkrec --kv kind=swap --kv "sample_mb=$S" --kv "config=\"$C\"" --kv "result=\"$RESULT\"" \
      --kv "rc=$RC" --kv "wall_sec=$WALL" --kv "swap_bytes=$SWAP_BYTES" --kv "swap_rows=$SWAP_ROWS" \
      --kv "vars=\"$VARS\"" --kv "peak_fs_delta=$PEAK" --kv "peak_datadir=$PEAKDD" --kv "purge_sec=$PURGE_SEC" --kv "count_check=$CC" \
      "${EXTRA_KV[@]}" \
      --jsonfile "before=$OUT/before_${S}_$C.json" --jsonfile "after=$OUT/after_${S}_$C.json" \
      --jsonfile "after_purge=$OUT/afterpurge_${S}_$C.json" \
      --marks "$OUT/swap_${S}_$C.out" --err "$OUT/swap_${S}_$C.err" >> "$RES"
    LASTSEC[$C]=$WALL
    log "결과 $S/$C: $RESULT · ${WALL}s · 행 수 $CC"
    if [ "$RESULT" = "정상" ] && awk -v w="$WALL" -v s="$SKIP_OVER" 'BEGIN{exit !(w>s)}'; then
      SKIP[$C]="더 작은 표본($S MB)에서 ${WALL}s > ${SKIP_OVER}s"
    fi
    stop_db
  done

  # ═══════════ R11 — 가장 큰 표본에서만 ═══════════
  if [ "$S" = "$LAST_SAMPLE" ] && [ "$R11" = "1" ]; then
    log "── R11 $S MB · $R11_CONFIG ──"
    start_db "$R11_CONFIG" || exit 1
    "${GEN[@]}" diverge --meta "$OUT/meta" --plan "$OUT/plan.json" --slice 1 > "$OUT/diverge1.sql"
    qr "$DB" < "$OUT/diverge1.sql"
    count_map "" "$OUT/r11_cnt_before.tsv"
    count_map "rs_new__" "$OUT/r11_cnt_new.tsv"
    "${GEN[@]}" checksum --plan "$OUT/plan.json" > "$OUT/checksum.sql"
    qr "$DB" < "$OUT/checksum.sql" | sort > "$OUT/r11_ck_before.tsv"
    BASESEC=${LASTSEC[$R11_CONFIG]:-60}
    NOTE="-"; MARK_AT_KILL="-"
    for FR in "$KILL_FRAC" "$(awk -v f="$KILL_FRAC" 'BEGIN{print f/2}')"; do
      KD=$(awk -v b="$BASESEC" -v f="$FR" 'BEGIN{d=b*f; if(d<3)d=3; if(d>1500)d=1500; printf "%.1f", d}')
      : > "$OUT/r11.out"
      ( docker exec -i -e MYSQL_PWD="$RSPW" "$CN" mariadb -u"$RSU" --default-character-set=utf8mb4 --unbuffered -N -B "$DB" \
          < "$OUT/swap.sql" > "$OUT/r11.out" 2> "$OUT/r11.err" ) &
      BGP=$!
      sleep "$KD"
      if ! kill -0 "$BGP" 2>/dev/null; then
        NOTE="${KD}s 전에 교체가 끝남(비율 $FR) — 비율을 줄여 다시"
        log "$NOTE"; wait "$BGP"
        # 다시 갈라 둔다: 이번엔 slice 2
        "${GEN[@]}" diverge --meta "$OUT/meta" --plan "$OUT/plan.json" --slice 2 > "$OUT/diverge2.sql"
        qr "$DB" < "$OUT/diverge2.sql"
        count_map "" "$OUT/r11_cnt_before.tsv"; count_map "rs_new__" "$OUT/r11_cnt_new.tsv"
        qr "$DB" < "$OUT/checksum.sql" | sort > "$OUT/r11_ck_before.tsv"
        continue
      fi
      MARK_AT_KILL=$(awk -F'\t' '$1=="MARK"{m=$2} END{print (m==""?"none":m)}' "$OUT/r11.out")
      tk=$(date +%s.%N); docker kill "$CN" >/dev/null; since=$(( $(date +%s) - 1 ))
      wait "$BGP" 2>/dev/null
      log "R11 강제 종료 (교체 시작 뒤 ${KD}s · 마지막 표시=$MARK_AT_KILL)"
      docker start "$CN" >/dev/null
      wait_ready "$since" 1800; R_READY=$(awk -v a="$tk" -v b="$(date +%s.%N)" 'BEGIN{printf "%.1f", b-a}')
      wait_rollback "$since" "$tk"; RBRC=$?
      snap "$OUT/r11_after.json"
      count_map "" "$OUT/r11_cnt_after.tsv"
      qr "$DB" < "$OUT/checksum.sql" | sort > "$OUT/r11_ck_after.tsv"
      cmp -s "$OUT/r11_cnt_after.tsv" "$OUT/r11_cnt_before.tsv" && VB='"같음"' || VB='"다름"'
      cmp -s "$OUT/r11_cnt_after.tsv" "$OUT/r11_cnt_new.tsv" && VN='"같음"' || VN='"다름"'
      cmp -s "$OUT/r11_ck_after.tsv" "$OUT/r11_ck_before.tsv" && VC='"같음"' || VC='"다름"'
      HINT="되돌려짐(교체 전과 같음)"
      [ "$VB" = '"같음"' ] && [ "$VC" = '"같음"' ] || HINT="교체 전과 다름 — 표 보고 판단"
      [ "$VN" = '"같음"' ] && HINT="교체가 커밋된 꼴(새 값과 같음) — kill 이 커밋 뒤였을 수 있음"
      [ "$RBRC" = "0" ] || HINT="$HINT · 되돌림 대기 상한 넘음"
      "${GEN[@]}" mkrec --kv kind=r11 --kv "sample_mb=$S" --kv "config=\"$R11_CONFIG\"" --kv "kill_after_sec=$KD" \
        --kv "mark_at_kill=\"$MARK_AT_KILL\"" --kv "ready_sec=$R_READY" --kv "rollback_trx0_sec=$RB_TRX0_SEC" \
        --kv "rollback_log_sec=$RB_LOG_SEC" --kv "rollback_lockprobe_sec=$RB_LOCK_SEC" --kv "probe_table=\"$PROBE_T\"" --kv "counts_vs_before=$VB" --kv "counts_vs_new=$VN" \
        --kv "checksum_vs_before=$VC" --kv "verdict_hint=\"$HINT\"" --kv "note=\"$NOTE\"" \
        --jsonfile "after=$OUT/r11_after.json" --err "$OUT/r11.err" >> "$RES"
      log "R11: 준비 ${R_READY}s · 되돌림(INNODB_TRX 0) $RB_TRX0_SEC · 로그 $RB_LOG_SEC · $HINT"
      break
    done
    stop_db
  fi
done

# ═════════════════════════════ 3. 정리 ═════════════════════════════
"${GEN[@]}" report --results "$RES" --md-out "$OUT/result.md" --json-out "$OUT/result.json"
docker logs "$CN" > "$OUT/last_server.log" 2>&1 || true
stop_db
sudo rm -rf "$DATA" "$TMPD"
log "끝 — $OUT/result.md"
