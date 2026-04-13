namespace HitPan.Application.Interfaces;

public interface IEventPublisher
{
    Task PublishAsync<T>(
        string eventType,
        T payload,
        CancellationToken ct = default)
        where T : class;
}
