namespace HitPan.Application.Interfaces;

/// <summary>
/// 짧은 비밀(SMTP 패스워드, API 키 등) 암호화용 단순 인터페이스.
/// Infrastructure의 EncryptionService 가 구현하여 DI 주입한다.
/// </summary>
public interface IPasswordEncryptor
{
    byte[] Encrypt(string plainText);
    string Decrypt(byte[] cipherBytes);
}
