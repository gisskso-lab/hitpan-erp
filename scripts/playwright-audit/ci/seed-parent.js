// G1-3 갈래 H — S5 시드 ① 부모계정 (1안 · 실물 길 `HitPan.API seed-parent`) — 작업지시서 §3-3 · §8 ①
//
// 하는 일
//   1) 런 안에서 시험용 P-256 키쌍을 만든다(운영 키 0 · Secrets 0). 개인키는 이 프로세스 메모리에만 있다.
//   2) 백오피스와 같은 꼴의 3분절 증표 `<payloadB64>.<sigB64>.<kid>` 를 그 개인키로 서명한다
//      (SerialProofVerifier.cs:111~116 — payloadB64 문자열 바이트 · SHA-256 · IEEE-P1363).
//   3) 공개키만 `HITPAN_SERIAL_PUBLIC_KEY__<kid>` 로 seed-parent 자식 프로세스에 준다(SerialProofVerifier.cs:56~64).
//      kid 는 내장 키(v1)와 다른 `ciui` — 내장 공개키를 덮지 않는다.
//   4) 개인키(두 꼴)·서명·증표는 만든 즉시 가리고 S10 검색 대상으로 넘긴다(§3-2 S0 ⓐ · §0-9 ④-2).
//
// 종료 코드: 0 = 성공 · 그 밖 = [장치] 실패(결과표에 DEVICE 로 찍는다).
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');
const L = require('./lib');

const KID = 'ciui';

function b64url(buf) {
  return Buffer.from(buf).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function fail(r, what, got) {
  L.rec(r, { id: 'H-S5a', what, got, pass: false, kind: 'DEVICE' });
  L.saveResult(r);
  process.exit(1);
}

function main() {
  const r = L.loadResult();
  const apiExe = process.env.UI_API_EXE;
  const pwParent = process.env.UI_PW_PARENT;
  const loginParent = process.env.UI_LOGIN_PARENT || 'ci-parent';
  if (!apiExe || !fs.existsSync(apiExe)) return fail(r, '시드① 준비 — API 실행파일', 'UI_API_EXE 없음');
  if (!pwParent) return fail(r, '시드① 준비 — 부모 비번(S0)', 'UI_PW_PARENT 없음');

  // 1) 키쌍 — 개인키 두 꼴(JWK d · PKCS8 DER base64)을 만든 즉시 가린다.
  const { privateKey, publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
  const jwk = privateKey.export({ format: 'jwk' });
  L.maskAndRegister('SEED_PRIV_D', jwk.d);
  L.maskAndRegister('SEED_PRIV_DER', privateKey.export({ format: 'der', type: 'pkcs8' }).toString('base64'));
  const publicPem = publicKey.export({ format: 'pem', type: 'spki' }).toString();

  // 2) 증표 — 꼴은 ProofPayload(SerialProofVerifier.cs:212~244)와 1:1.
  const now = Math.floor(Date.now() / 1000);
  const tenantId = crypto.randomUUID();
  const payload = {
    jti: crypto.randomUUID(),
    iss: 'ci-ui-seed',
    aud: 'erp-bootstrap',
    sub: tenantId,
    tenant_code: 'CIUI',
    company_name: '시험 회사',
    iat: now,
    exp: now + 3600,
    subscription: {
      SubscriptionTier: 'basic',
      Status: 'active',
      AiMode: 'hitpan_pool',
      AiTokenMonthlyLimit: 0,
      AiTokenExtra: 0,
      AnthropicKeyStatus: 'none',
      MaxUsers: 5, // 대표 포함 5 — 자식 2 를 막지 않는다(§8 ④ · 본사가 보내는 값 자리 그대로)
      ExtraDeviceSlots: 0,
      ResellerTier: 0,
    },
  };
  const payloadB64 = b64url(Buffer.from(JSON.stringify(payload), 'utf8'));
  const sig = crypto.sign('sha256', Buffer.from(payloadB64, 'utf8'), { key: privateKey, dsaEncoding: 'ieee-p1363' });
  const sigB64 = b64url(sig);
  L.maskAndRegister('SEED_SIG', sigB64);
  const proof = `${payloadB64}.${sigB64}.${KID}`;
  L.maskAndRegister('SEED_PROOF', proof);

  // 3) 입력 파일 — 업로드 폴더 밖(RUNNER_TEMP). seed-parent 가 읽는 즉시 지운다(SeedParentCommand.cs:103~107).
  const workDir = path.join(process.env.RUNNER_TEMP || os.tmpdir(), 'ui-seed');
  fs.mkdirSync(workDir, { recursive: true, mode: 0o700 });
  const inputPath = path.join(workDir, 'seed-parent-input.json');
  fs.writeFileSync(inputPath, JSON.stringify({
    signedProof: proof,
    loginId: loginParent,
    password: pwParent,
    name: '시험 대표',
    bizNo: '0000000001',
    ceoName: '시험 대표',
  }), { mode: 0o600 });

  const env = { ...process.env, [`HITPAN_SERIAL_PUBLIC_KEY__${KID}`]: publicPem };
  const run = spawnSync(apiExe, ['seed-parent', inputPath], { env, encoding: 'utf8', timeout: 120000 });

  // 입력 파일이 남았으면 지운다(seed-parent 가 못 지운 경우).
  try {
    if (fs.existsSync(inputPath)) fs.unlinkSync(inputPath);
  } catch (e) {
    console.log(`::warning::시드① 입력 파일 지우기 실패 (${e.code || '원문 생략'})`);
  }

  console.log(`seed-parent 표준출력: ${L.sanitize(run.stdout)}`);
  console.log(`seed-parent 표준오류: ${L.sanitize(run.stderr)}`);
  if (run.error) return fail(r, '시드① seed-parent 실행', `spawn 오류 ${run.error.code || ''}`);
  if (run.status !== 0) return fail(r, '시드① seed-parent 종료 코드 0', `exit=${run.status}`);

  r.seed = { mode: '1안', path: 'HitPan.API seed-parent (실물 길)', kid: KID, tenant_code: payload.tenant_code };
  L.rec(r, { id: 'H-S5a', what: '시드① 부모계정 seed-parent (1안 · Production)', got: 'exit=0', pass: true, kind: 'DEVICE' });
  L.saveResult(r);
  L.exportPlain('UI_TENANT_ID', tenantId);
}

main();
