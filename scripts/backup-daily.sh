#!/usr/bin/env bash
# 히트판 ERP — 일일 DB 백업 스크립트 (베타 운영용)
# cron 예: 0 3 * * *  /opt/hitpan/scripts/backup-daily.sh
set -euo pipefail

DB_NAME="${DB_NAME:-hitpan_erp}"
DB_USER="${DB_USER:-hitpan}"
DB_PASS="${DB_PASS:-Hitpan2025!}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/hitpan}"
RETAIN_DAYS="${RETAIN_DAYS:-14}"

STAMP="$(date +%Y%m%d_%H%M)"
OUT="${BACKUP_DIR}/hitpan_erp_${STAMP}.sql.gz"

mkdir -p "${BACKUP_DIR}"

echo "[backup] $(date -Iseconds) START ${DB_NAME} -> ${OUT}"
mysqldump \
  -u "${DB_USER}" -p"${DB_PASS}" \
  --single-transaction --quick --routines --triggers --events \
  --column-statistics=0 --set-gtid-purged=OFF \
  --default-character-set=utf8mb4 \
  "${DB_NAME}" | gzip -9 > "${OUT}"

SIZE="$(du -h "${OUT}" | cut -f1)"
echo "[backup] DONE size=${SIZE}"

echo "[backup] prune files older than ${RETAIN_DAYS}d"
find "${BACKUP_DIR}" -name "hitpan_erp_*.sql.gz" -mtime +${RETAIN_DAYS} -print -delete || true

echo "[backup] $(date -Iseconds) END"
