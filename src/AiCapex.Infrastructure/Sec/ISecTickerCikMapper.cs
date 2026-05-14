namespace AiCapex.Infrastructure.Sec;

public interface ISecTickerCikMapper
{
    Task<string?> GetCikAsync(string ticker, CancellationToken cancellationToken = default);
}
