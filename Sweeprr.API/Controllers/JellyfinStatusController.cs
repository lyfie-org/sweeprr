using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sweeprr.API.Integrations.Jellyfin.WebSocket;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/jellyfin")]
[Authorize]
public sealed class JellyfinStatusController : ControllerBase
{
    private readonly IJellyfinWebSocketStatus _wsStatus;

    public JellyfinStatusController(IJellyfinWebSocketStatus wsStatus)
    {
        _wsStatus = wsStatus;
    }

    /// <summary>
    /// Returns the current WebSocket connection state and last-connected timestamp.
    /// Used by the frontend realtime status pill ("Realtime: connected" indicator).
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetStatus() => Ok(new
    {
        state           = _wsStatus.State.ToString(),
        lastConnectedAt = _wsStatus.LastConnectedAt
    });
}
