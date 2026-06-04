using Sweeprr.API.Dtos.Connections;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

public interface IConnectionTestService
{
    /// <summary>Tests a saved connection by its ID.</summary>
    Task<ConnectionTestResult> TestSavedAsync(int connectionId);

    /// <summary>Tests a connection using inline credentials (before saving).</summary>
    Task<ConnectionTestResult> TestUnsavedAsync(ConnectionType type, string baseUrl, string apiKey, bool allowInsecure);
}
