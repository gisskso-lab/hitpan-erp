#!/usr/bin/env bash
# 히트판 ERP — NCP 서버 환경변수 일괄 등록 스크립트
#
# 사용:
#   1) secrets/.generated-secrets-20260611.txt 를 NCP 서버 /tmp 로 SCP 업로드
#   2) NCP 서버 SSH 접속 후:
#      sudo bash deploy-ncp-secrets.sh /tmp/.generated-secrets-20260611.txt
#   3) 등록 후 secrets 파일 즉시 파기 (shred)
#
# 헌법 #29: PM은 NCP SSH 직접 실행 불가. 사장님 영역.
#
# 대상:
#   - systemd service: hitpan-backoffice-api.service
#   - Environment= 항목 5개 박제
#   - 박은 후 systemctl daemon-reload + restart

set -euo pipefail

SECRET_FILE="${1:-/tmp/.generated-secrets-20260611.txt}"
SERVICE_NAME="hitpan-backoffice-api.service"
SYSTEMD_OVERRIDE_DIR="/etc/systemd/system/${SERVICE_NAME}.d"
OVERRIDE_FILE="${SYSTEMD_OVERRIDE_DIR}/secrets.conf"

if [[ ! -f "$SECRET_FILE" ]]; then
    echo "❌ 시크릿 파일이 없습니다: $SECRET_FILE"
    exit 1
fi

if [[ $EUID -ne 0 ]]; then
    echo "❌ root 권한 필요. sudo 로 실행하세요."
    exit 1
fi

echo "============================================"
echo "  NCP 환경변수 박제 — $SERVICE_NAME"
echo "============================================"

mkdir -p "$SYSTEMD_OVERRIDE_DIR"
chmod 700 "$SYSTEMD_OVERRIDE_DIR"

echo "[Service]" > "$OVERRIDE_FILE"
while IFS= read -r line; do
    if [[ "$line" =~ ^([A-Z_][A-Z0-9_]*)=(.+)$ ]]; then
        key="${BASH_REMATCH[1]}"
        val="${BASH_REMATCH[2]}"
        echo "Environment=\"${key}=${val}\"" >> "$OVERRIDE_FILE"
        echo "  > $key 박제 OK"
    fi
done < "$SECRET_FILE"

chmod 600 "$OVERRIDE_FILE"
chown root:root "$OVERRIDE_FILE"

echo ""
echo "systemd 재로드 + 서비스 재시작..."
systemctl daemon-reload
systemctl restart "$SERVICE_NAME"

sleep 3
if systemctl is-active --quiet "$SERVICE_NAME"; then
    echo "✅ 서비스 정상 가동"
else
    echo "❌ 서비스 가동 실패. journalctl -u $SERVICE_NAME -n 50 확인"
    exit 1
fi

echo ""
echo "============================================"
echo "  완료"
echo "============================================"
echo ""
echo "⚠️  $SECRET_FILE 즉시 파기 권장:"
echo "    shred -u $SECRET_FILE"
