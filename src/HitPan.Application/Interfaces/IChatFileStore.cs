namespace HitPan.Application.Interfaces;

/// <summary>
/// 메신저 파일 저장소. 작(2026-08-13) 그룹웨어 단계9.
/// </summary>
/// <remarks>
/// 🔴 <b>파일은 디스크에, DB 에는 경로만.</b> DB 안에 통째로 넣으면
/// DB 가 수십 GB 로 불어 <b>백업·복구·업데이트가 모두 느려진다.</b>
/// 사장님(2026-08-13): <i>"히트판 ERP 데이터양이 많으면 과부화가 올 수 있어. 파일전송은 최소한으로"</i>
/// </remarks>
public interface IChatFileStore
{
    /// <summary>
    /// 파일을 저장한다. 🔴 확장자·시그니처를 검사해 <b>실행파일이면 막는다.</b>
    /// </summary>
    /// <exception cref="InvalidOperationException">보낼 수 없는 형식일 때.</exception>
    Task<StoredChatFile> SaveAsync(string tenantId, string originalName, Stream content,
        CancellationToken ct = default);

    /// <summary>읽기용으로 연다. 없으면 null.</summary>
    Stream? OpenRead(string relativePath);

    /// <summary>지운다(DB 저장이 실패했을 때 뒷정리). 실패해도 던지지 않는다.</summary>
    void TryDelete(string relativePath);
}

/// <summary>저장된 파일의 자리.</summary>
public sealed class StoredChatFile
{
    /// <summary>🔴 저장 파일명이기도 하다 — 원래 이름을 쓰면 경로 조작이 들어온다.</summary>
    public string FileId { get; init; } = string.Empty;

    /// <summary><c>chat-files/{tenant_id}/{yyyyMM}/{file_id}.{ext}</c></summary>
    public string RelativePath { get; init; } = string.Empty;
}
