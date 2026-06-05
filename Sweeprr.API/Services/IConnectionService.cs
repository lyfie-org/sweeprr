using Sweeprr.API.Dtos.Connections;

namespace Sweeprr.API.Services;

public interface IConnectionService
{
    Task<IEnumerable<ConnectionResponse>> GetAllAsync();
    Task<ConnectionResponse?> GetByIdAsync(int id);
    Task<(ConnectionResponse connection, string? warning)> CreateAsync(ConnectionRequest request);
    Task<ConnectionResponse?> UpdateAsync(int id, ConnectionRequest request);
    Task<bool> DeleteAsync(int id);

    /// <summary>
    /// Decrypts and returns the raw API key for a connection.
    /// Only used internally (e.g. by the test service) — never sent to clients.
    /// Returns null when no key is stored or decryption fails.
    /// </summary>
    Task<string?> GetDecryptedKeyAsync(int id);

    /// <summary>Persists the last-tested status back to the DB.</summary>
    Task PersistTestResultAsync(int id, bool success);
}
