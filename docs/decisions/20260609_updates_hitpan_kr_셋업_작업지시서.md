# `updates.hitpan.kr` 셋업 작업지시서

**작성**: 2026-06-09 PM
**근거**: 사장님 결재 3 (자동 업데이트 서버 = NCP `updates.hitpan.kr`)
**대상**: 사장님 직접 박는 영역 (NCP 인프라 + Cloudflare DNS)

---

## 셋업 절차 (사장님 직접 박는 영역)

### 1. NCP 서버 영역 박기
- 기존 NCP 백오피스 서버에 nginx 가상호스트 추가 (별도 서버 불필요)
- 또는 NCP Object Storage 박은 후 Cloudflare R2 연동 (트래픽 종량제)

**PM 추천**: 기존 NCP 백오피스 서버에 nginx 가상호스트 (운영 부담 0)

### 2. nginx 설정 박기

`/etc/nginx/sites-available/updates.hitpan.kr`:
```nginx
server {
    listen 80;
    listen [::]:80;
    server_name updates.hitpan.kr;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name updates.hitpan.kr;

    ssl_certificate     /etc/letsencrypt/live/hitpan.kr/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/hitpan.kr/privkey.pem;

    root /var/www/updates;
    index manifest.json;

    # manifest.json: 캐시 박지 않음 (즉시 반영)
    location = /manifest.json {
        add_header Cache-Control "no-store, no-cache, must-revalidate";
        add_header Access-Control-Allow-Origin "*";
        try_files $uri =404;
    }

    # packages/*.zip: 1년 캐시 (불변 산출물)
    location /packages/ {
        add_header Cache-Control "public, max-age=31536000, immutable";
        try_files $uri =404;
    }

    # 그 외 차단
    location / {
        return 403;
    }
}
```

### 3. Cloudflare DNS 박기
- `updates.hitpan.kr` → A 레코드 → NCP 서버 IP
- Proxied: ON (Cloudflare CDN 박힘)

### 4. 디렉토리 박기
```bash
sudo mkdir -p /var/www/updates/packages
sudo chown -R www-data:www-data /var/www/updates
sudo chmod -R 755 /var/www/updates
```

### 5. 첫 manifest 박기
```bash
sudo cp installer/updates/manifest-sample.json /var/www/updates/manifest.json
sudo nano /var/www/updates/manifest.json   # 실값 박기 (버전, sha256, sizeBytes)
```

### 6. SSL 인증서 발급
```bash
sudo certbot --nginx -d updates.hitpan.kr
```

### 7. nginx 재로드
```bash
sudo ln -s /etc/nginx/sites-available/updates.hitpan.kr /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

### 8. 시험
```bash
curl -I https://updates.hitpan.kr/manifest.json
# 응답: 200 OK + Cache-Control: no-store
```

---

## CI/CD 자동 배포 박힘 (GitHub Actions)

`.github/workflows/release-update.yml` 추가 박을 영역:

```yaml
name: Release Update Package

on:
  release:
    types: [published]

jobs:
  publish-update:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Build update package
        run: |
          zip -r hitpan-${{ github.event.release.tag_name }}.zip src/

      - name: Compute sha256
        run: |
          sha256sum hitpan-*.zip > SHA256SUMS
          echo "SHA256=$(cut -d' ' -f1 SHA256SUMS)" >> $GITHUB_ENV
          echo "SIZE=$(stat -c%s hitpan-*.zip)" >> $GITHUB_ENV

      - name: Generate manifest.json
        run: |
          cat > manifest.json <<EOF
          {
            "version": "${{ github.event.release.tag_name }}",
            "channel": "Normal",
            "downloadUrl": "https://updates.hitpan.kr/packages/hitpan-${{ github.event.release.tag_name }}.zip",
            "sha256": "${{ env.SHA256 }}",
            "sizeBytes": ${{ env.SIZE }},
            "releasedAt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
            "releaseNotes": "${{ github.event.release.body }}",
            "requiresMigration": false,
            "consentMessage": null
          }
          EOF

      - name: Upload to NCP
        env:
          NCP_HOST: ${{ secrets.NCP_HOST }}
          NCP_USER: ${{ secrets.NCP_USER }}
        run: |
          echo "${{ secrets.NCP_SSH_PRIVATE_KEY }}" > key
          chmod 600 key
          scp -i key hitpan-*.zip $NCP_USER@$NCP_HOST:/var/www/updates/packages/
          scp -i key manifest.json $NCP_USER@$NCP_HOST:/var/www/updates/manifest.json
```

(이 워크플로우는 PM이 박은 영역 — 사장님 GitHub Secrets 등록 후 자동 가동)

---

## 채널 분기 박는 영역 (수동)

- **Normal** 박기: 위 자동 배포 그대로
- **Emergency** 박기: 수동으로 manifest.json `channel: "Emergency"` 박은 후 NCP 업로드
- **Major** 박기: 수동으로 `channel: "Major"`, `requiresMigration: true`, `consentMessage: "..."` 박음

---

## 사장님이 박을 영역 (이 가이드 끝나면)

1. NCP nginx에 가상호스트 박기
2. Cloudflare DNS 박기
3. SSL 인증서 박기
4. 첫 manifest.json 박기
5. GitHub Secrets 박기 (NCP_HOST, NCP_USER, NCP_SSH_PRIVATE_KEY)
