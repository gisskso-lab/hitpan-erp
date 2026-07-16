using System.Security.Cryptography;
using System.Text;
using HitPan.Watchdog.AutoUpdate;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 작1 게이트 G-S — manifest 서명 규격·검증 고정 (2026-07-16, 사장님 결재).
///
/// 왜 이 테스트가 중요한가:
///   서명은 "환경변수 하나로 남의 코드가 SYSTEM 권한으로 설치되는" 사고를 막는 유일한 방어다
///   (feed 주소는 HITPAN_UPDATE_FEED 로 바뀌고, sha256 은 manifest 안에 있어 자기참조다).
///   동시에 서명 규격이 발급(NCP)과 검증(EXE) 사이에서 한 글자라도 어긋나면 정상 manifest 도
///   전부 거부돼 **모든 고객의 업데이트가 전면 중단**된다. 양쪽 위험이 다 크므로 규격을 못박는다.
/// </summary>
public class UpdateSignatureTests
{
    private static UpdateManifest Sample(
        string version = "1.2.35",
        UpdateChannel channel = UpdateChannel.Normal,
        string url = "https://updates.hitpan.kr/packages/hitpan-1.2.35.zip",
        string sha = "abc123",
        long size = 71_000_000,
        bool migration = false,
        string? signature = null,
        string? kid = null)
        => new(version, channel, url, sha, size,
               new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc),
               "릴리스 노트", migration, "안내 문구", signature, kid);

    // ─── 서명 대상 문자열 규격 (발급기와 맞춰야 하는 계약) ──────────────────────────

    [Fact]
    public void 서명_대상_문자열이_규격대로다()
    {
        var payload = UpdateManifestSigning.BuildSigningPayload(Sample());

        // 이 문자열이 NCP 발급 스크립트와 정확히 같아야 한다. 바꾸면 기존 서명이 전부 무효가 된다.
        Assert.Equal(
            "hitpan-update-v1\n" +
            "version=1.2.35\n" +
            "channel=normal\n" +
            "url=https://updates.hitpan.kr/packages/hitpan-1.2.35.zip\n" +
            "sha256=abc123\n" +
            "size=71000000\n" +
            "migration=false",
            payload);
    }

    [Fact]
    public void 규격_표식이_맨_앞에_있다()
    {
        // 다른 용도로 만든 서명이 업데이트 manifest 에 재사용되는 것을 막는 표식.
        Assert.StartsWith("hitpan-update-v1\n", UpdateManifestSigning.BuildSigningPayload(Sample()));
    }

    /// <summary>
    /// sha256 이 서명 대상에 들어가는 게 핵심이다 — 이래야 "manifest 를 바꾸는 자가 sha256 도 바꾼다"는
    /// 자기참조 고리가 끊긴다(sha256 을 바꾸려면 NCP 개인키로 서명을 다시 만들어야 한다).
    /// </summary>
    [Fact]
    public void sha256이_바뀌면_서명_대상도_바뀐다_자기참조_차단()
    {
        var a = UpdateManifestSigning.BuildSigningPayload(Sample(sha: "aaa"));
        var b = UpdateManifestSigning.BuildSigningPayload(Sample(sha: "bbb"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void 공격자가_바꿔_이득을_볼_값은_전부_서명_대상이다()
    {
        var baseline = UpdateManifestSigning.BuildSigningPayload(Sample());

        // 어느 하나만 바꿔도 서명 대상이 달라져야 한다 = 서명이 깨진다.
        Assert.NotEqual(baseline, UpdateManifestSigning.BuildSigningPayload(Sample(version: "9.9.9")));
        Assert.NotEqual(baseline, UpdateManifestSigning.BuildSigningPayload(Sample(url: "https://evil.example/x.zip")));
        Assert.NotEqual(baseline, UpdateManifestSigning.BuildSigningPayload(Sample(sha: "deadbeef")));
        Assert.NotEqual(baseline, UpdateManifestSigning.BuildSigningPayload(Sample(size: 1)));
        Assert.NotEqual(baseline, UpdateManifestSigning.BuildSigningPayload(Sample(migration: true)));
        Assert.NotEqual(baseline, UpdateManifestSigning.BuildSigningPayload(Sample(channel: UpdateChannel.Emergency)));
    }

    [Fact]
    public void 표시용_문구는_서명_대상이_아니다()
    {
        // ReleaseNotes·ConsentMessage·ReleasedAt 이 달라도 "무엇이 설치되는가"는 안 변한다.
        // 이런 값까지 서명에 넣으면 표기 흔들림(개행·타임존)으로 정상 릴리스가 거부될 위험만 커진다.
        var a = Sample() with { ReleaseNotes = "A", ConsentMessage = "가", ReleasedAt = DateTime.UtcNow };
        var b = Sample() with { ReleaseNotes = "B", ConsentMessage = "나", ReleasedAt = DateTime.UtcNow.AddDays(-3) };
        Assert.Equal(UpdateManifestSigning.BuildSigningPayload(a), UpdateManifestSigning.BuildSigningPayload(b));
    }

    // ─── 검증기 동작 (fail-closed) ──────────────────────────────────────────────

    private static UpdateSignatureVerifier NewVerifier()
        => new(NullLogger<UpdateSignatureVerifier>.Instance);

    [Fact]
    public void 서명_없는_manifest는_거부한다()
    {
        Assert.False(NewVerifier().Verify(Sample(signature: null, kid: null)));
    }

    [Theory]
    [InlineData(null, "upd-v1")]   // 서명만 없음
    [InlineData("sig", null)]      // kid 만 없음
    [InlineData("", "upd-v1")]
    [InlineData("sig", "")]
    public void 서명이나_kid가_비면_거부한다(string? sig, string? kid)
    {
        Assert.False(NewVerifier().Verify(Sample(signature: sig, kid: kid)));
    }

    [Fact]
    public void 모르는_kid는_거부한다_폐기키_또는_미배포()
    {
        // 아직 공개키가 배포되지 않았거나(초기 설정 미완), 폐기된 키로 서명된 manifest.
        // 확인할 수 없으면 설치하지 않는다.
        Assert.False(NewVerifier().Verify(Sample(signature: "AAAA", kid: "누가봐도-모르는-키")));
    }

    [Fact]
    public void 서명_형식이_깨지면_거부한다()
    {
        Assert.False(NewVerifier().Verify(Sample(signature: "!!!not-base64!!!", kid: UpdateManifestSigning.DefaultKid)));
    }

    /// <summary>
    /// 실제 ECDSA 키쌍으로 왕복 검증 — 규격(BuildSigningPayload)과 검증기가 서로 맞는지 실증한다.
    /// db.conf override 경로를 쓰지 않고 검증기 내부 로직만 직접 확인한다
    /// (override 는 파일 의존이라 단위 테스트에서 건드리지 않는다).
    /// </summary>
    [Fact]
    public void 정상_서명은_규격대로_만들면_검증된다_왕복()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Sample();
        var payload = UpdateManifestSigning.BuildSigningPayload(manifest);

        var sig = ecdsa.SignData(
            Encoding.UTF8.GetBytes(payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        // 검증기가 쓰는 것과 동일한 알고리즘·형식으로 확인 (SerialProofVerifier 와 같은 규격)
        var ok = ecdsa.VerifyData(
            Encoding.UTF8.GetBytes(payload),
            sig,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.True(ok);
    }

    /// <summary>
    /// 서명 후 manifest 를 조작하면 검증이 깨진다 — 위·변조 차단의 실증.
    /// </summary>
    [Fact]
    public void 서명_후_manifest를_바꾸면_검증이_깨진다()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var original = Sample();
        var sig = ecdsa.SignData(
            Encoding.UTF8.GetBytes(UpdateManifestSigning.BuildSigningPayload(original)),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        // 공격자가 다운로드 주소를 자기 서버로 바꿨다.
        var tampered = original with { DownloadUrl = "https://evil.example/payload.zip" };

        var ok = ecdsa.VerifyData(
            Encoding.UTF8.GetBytes(UpdateManifestSigning.BuildSigningPayload(tampered)),
            sig,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.False(ok);
    }

    [Fact]
    public void 워치독_서명키는_시리얼키와_분리돼_있다()
    {
        // 시리얼 개인키 유출 = 가짜 라이선스. 업데이트 개인키 유출 = 전 고객 PC 임의 코드 실행.
        // 위력이 다르므로 kid 를 분리한다 — 시리얼 증표의 기본 kid("v1")를 재사용하면 안 된다.
        Assert.Equal("upd-v1", UpdateManifestSigning.DefaultKid);
        Assert.NotEqual("v1", UpdateManifestSigning.DefaultKid);
    }

    // ─── 실제 검증기(VerifyWithPem) 왕복 — F-3 봉합 (2026-07-16, 검증팀 SoD 반증) ────────────
    //
    //   ⚠️ 검증팀 적발: 위 '왕복' 테스트들은 bare ECDsa 로 자기 서명·자기 검증만 해서 정작
    //      UpdateSignatureVerifier 를 한 번도 호출하지 않았다(검증기를 지워도 초록이었다).
    //      게다가 발급(NCP openssl)과 검증(.NET) 규격이 실측으로 어긋나(DER≠P1363, 표준Base64≠Base64Url)
    //      사장님이 절차서대로 서명하면 전 고객 업데이트가 전면 중단되는 P0 가 잠복해 있었다.
    //      아래 테스트는 검증기 코어를 '직접' 태워, openssl 형식(DER + 표준 Base64)과
    //      .NET 형식(P1363 + Base64Url) 두 가지가 다 통과함을 실증한다.

    private static string NewPublicKeyPem(out ECDsa key)
    {
        key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportSubjectPublicKeyInfoPem();
    }

    private static string ToStandardBase64(byte[] b) => Convert.ToBase64String(b);              // openssl base64 형식
    private static string ToBase64Url(byte[] b) =>                                               // 백오피스(.NET) 형식
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void openssl형식_DER_표준Base64_서명이_검증기를_통과한다()
    {
        // openssl `dgst -sha256 -sign` 이 내는 것과 동일: DER(Rfc3279DerSequence) + 표준 Base64.
        var pem = NewPublicKeyPem(out var key);
        using (key)
        {
            var manifest = Sample();
            var payload = Encoding.UTF8.GetBytes(UpdateManifestSigning.BuildSigningPayload(manifest));
            var derSig = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            var signed = manifest with { Signature = ToStandardBase64(derSig), Kid = UpdateManifestSigning.DefaultKid };

            Assert.Equal(UpdateSignatureVerifier.VerifyResult.Ok,
                UpdateSignatureVerifier.VerifyWithPem(signed, pem));
        }
    }

    [Fact]
    public void dotnet형식_P1363_Base64Url_서명이_검증기를_통과한다()
    {
        // 백오피스 SerialSignatureService 와 동일: IEEE-P1363 + Base64Url. 미래 자동화 경로.
        var pem = NewPublicKeyPem(out var key);
        using (key)
        {
            var manifest = Sample();
            var payload = Encoding.UTF8.GetBytes(UpdateManifestSigning.BuildSigningPayload(manifest));
            var p1363 = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var signed = manifest with { Signature = ToBase64Url(p1363), Kid = UpdateManifestSigning.DefaultKid };

            Assert.Equal(UpdateSignatureVerifier.VerifyResult.Ok,
                UpdateSignatureVerifier.VerifyWithPem(signed, pem));
        }
    }

    [Fact]
    public void 검증기가_서명후_변조를_잡는다_DownloadUrl()
    {
        // 정상 서명 후 다운로드 주소만 바꿔치기 — 검증기가 SignatureMismatch 로 거부해야 한다.
        var pem = NewPublicKeyPem(out var key);
        using (key)
        {
            var original = Sample();
            var derSig = key.SignData(
                Encoding.UTF8.GetBytes(UpdateManifestSigning.BuildSigningPayload(original)),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            var tampered = original with
            {
                DownloadUrl = "https://evil.example/payload.zip",
                Signature = ToStandardBase64(derSig),
                Kid = UpdateManifestSigning.DefaultKid
            };

            Assert.Equal(UpdateSignatureVerifier.VerifyResult.SignatureMismatch,
                UpdateSignatureVerifier.VerifyWithPem(tampered, pem));
        }
    }

    [Fact]
    public void 검증기가_다른_키로_만든_서명을_거부한다()
    {
        // 공격자의 개인키로 서명 → 우리 공개키로 검증 = 불일치. "환경변수 하나로 남의 코드 실행" 차단의 핵심.
        var ourPem = NewPublicKeyPem(out var ourKey);
        using (ourKey)
        using (var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var manifest = Sample();
            var attackerSig = attackerKey.SignData(
                Encoding.UTF8.GetBytes(UpdateManifestSigning.BuildSigningPayload(manifest)),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            var signed = manifest with { Signature = ToStandardBase64(attackerSig), Kid = UpdateManifestSigning.DefaultKid };

            Assert.Equal(UpdateSignatureVerifier.VerifyResult.SignatureMismatch,
                UpdateSignatureVerifier.VerifyWithPem(signed, ourPem));
        }
    }

    [Fact]
    public void 검증기가_망가진_공개키_PEM에_예외없이_거부한다()
    {
        // 공개키 PEM 이 손상돼도 크래시하지 않고 Error 로 안전 거부(fail-closed, 헌법 #15).
        var manifest = Sample() with { Signature = "AAAA", Kid = UpdateManifestSigning.DefaultKid };
        Assert.Equal(UpdateSignatureVerifier.VerifyResult.Error,
            UpdateSignatureVerifier.VerifyWithPem(manifest, "-----BEGIN PUBLIC KEY-----\nnot-a-key\n-----END PUBLIC KEY-----"));
    }

    [Fact]
    public void 검증기가_규격_한글자_어긋남을_잡는다()
    {
        // 발급자가 payload 규격을 어기면(예: channel 대문자) 정상 키로 서명해도 거부돼야 한다.
        // = "발급·검증 규격이 어긋나면 조용히 통과하지 않는다"의 실증(전 고객 중단은 오히려 이걸로 조기 발각).
        var pem = NewPublicKeyPem(out var key);
        using (key)
        {
            var manifest = Sample();
            // 규격을 벗어난 payload("channel=Normal" 대문자)에 서명한다.
            var wrongPayload = UpdateManifestSigning.BuildSigningPayload(manifest)
                .Replace("channel=normal", "channel=Normal", StringComparison.Ordinal);
            var sig = key.SignData(Encoding.UTF8.GetBytes(wrongPayload),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            var signed = manifest with { Signature = ToStandardBase64(sig), Kid = UpdateManifestSigning.DefaultKid };

            Assert.Equal(UpdateSignatureVerifier.VerifyResult.SignatureMismatch,
                UpdateSignatureVerifier.VerifyWithPem(signed, pem));
        }
    }
}
