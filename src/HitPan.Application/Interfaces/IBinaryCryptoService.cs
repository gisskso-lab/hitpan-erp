namespace HitPan.Application.Interfaces;

/// <summary>
/// VARBINARY 컬럼용 AES-256 암복호화 추상화.
///
/// W2 D3 (2026-05-12 사장님 결재 A-2): MdbMigrationService 등 Application 레이어에서
/// 형사영역 6개 컬럼(주민번호·급여 등) 처리 시 Infrastructure 의존성 역행 회피용.
///
/// 구현체는 Infrastructure 레이어의 IEncryptionService(EncryptBytes/DecryptBytes) 어댑터.
/// 마스터키는 ERP_ENCRYPTION_KEY 환경변수 (헌법 #18·#22 본사 송신 0).
/// </summary>
public interface IBinaryCryptoService
{
    /// <summary>UTF-8 평문 문자열 → AES-256 암호화 byte[].</summary>
    byte[]? EncryptToBytes(string? plaintext);

    /// <summary>AES-256 암호화 byte[] → UTF-8 평문 문자열.</summary>
    string? DecryptFromBytes(byte[]? ciphertext);
}
