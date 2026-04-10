namespace HitPan.Web.Services;

public interface IAuthTokenRefresher
{
    Task<bool> TryRefreshAsync(CancellationToken ct = default);
}
