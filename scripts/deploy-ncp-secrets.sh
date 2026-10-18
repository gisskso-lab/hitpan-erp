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
#   - Environment= 항목 5개 등록
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
echo "  NCP 환경변수 등록 — $SERVICE_NAME"
echo "============================================"

mkdir -p "$SYSTEMD_OVERRIDE_DIR"
chmod 700 "$SYSTEMD_OVERRIDE_DIR"

# >>> P0GATE-BEGIN  ← 🔴 게이트(P0DeliveryPathGateTests)가 이 구간만 떼어 실제로 돌린다.
#   왜 구간을 표시하나: 이 위에는 root 검사·systemctl 이 있어 시험 기계에서 못 돈다.
#   🔴 표시만 추가했고 로직은 한 줄도 안 바꿨다. 이 두 줄을 지우면 게이트가 FAIL 한다.
# 🔴 20261019작P0 §1 — 적재 개수와 건너뛴 줄을 세기 위한 그릇(종전엔 둘 다 없었다).
applied=0
skipped=()

echo "[Service]" > "$OVERRIDE_FILE"
while IFS= read -r line; do
    # 🔴 20261019작P0 §1 (사장님 결재 2026-10-19) — 종전 줄은 지우지 않는다(#1):
    #   if [[ "$line" =~ ^([A-Z_][A-Z0-9_]*)=(.+)$ ]]; then
    # 왜 바꾸나: 대문자만 받아서 코드가 읽는 정본 이름(License__Pepper ·
    #   Backoffice__BizNoPepper · Smtp__Host 처럼 섞인 대소문자)을 아무 말 없이 건너뛰었다.
    #   ⇒ 고친 이름을 이 스크립트로는 영원히 넣을 수 없었다. 실측 2026-10-18.
    if [[ "$line" =~ ^([A-Za-z_][A-Za-z0-9_]*)=(.+)$ ]]; then
        key="${BASH_REMATCH[1]}"
        val="${BASH_REMATCH[2]}"
        echo "Environment=\"${key}=${val}\"" >> "$OVERRIDE_FILE"
        # 🔴 20261019작P0 §1-3 — 값은 한 글자도 찍지 않는다. 키 이름만.
        #   (종전 낱말이 사장님 금지어라 「등록」으로 바꿨다)
        echo "  > $key 등록 OK"
        applied=$((applied + 1))
    else
        # 🔴 20261019작P0 §1-2 — 종전엔 안 맞는 줄이 if 를 그냥 빠져나갔다.
        #   에러도 경고도 없어서 「0개 넣고도 성공」이 가능했다(#15 침묵 금지).
        #   빈 줄과 # 주석은 정상 통과시키고, 그 밖에 안 맞는 줄만 모은다.
        if [[ -n "$line" && ! "$line" =~ ^[[:space:]]*# ]]; then
            skipped+=("$line")
        fi
    fi
done < "$SECRET_FILE"

# ── 🔴 20261019작P0 §1-2·1-3 — 조용히 넘기지 않는다 ─────────────────────
echo ""
echo "등록한 키 개수: $applied"
if [[ $applied -eq 0 ]]; then
    echo "❌ 한 개도 등록하지 못했다. 비밀 파일 모양을 확인하라(KEY=VALUE 한 줄씩)."
    exit 1
fi
if [[ ${#skipped[@]} -gt 0 ]]; then
    echo "❌ 건너뛴 줄 ${#skipped[@]} 개 — 이 줄들은 서버에 들어가지 않았다:"
    for s in "${skipped[@]}"; do
        # 🔴 값은 찍지 않는다. = 앞의 키 자리만 보여준다.
        echo "    - ${s%%=*}"
    done
    echo "   KEY=VALUE 모양이 아닌 줄이다. 고치고 다시 실행하라."
    exit 1
fi

# >>> P0GATE-END

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
