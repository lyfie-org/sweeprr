using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/system")]
[AllowAnonymous]
public class SystemController : ControllerBase
{
    /// <summary>
    /// Returns the current app version and release date.
    /// Accessible without authentication for display on login/setup pages.
    /// </summary>
    [HttpGet("info")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Info()
    {
        return Ok(new { version = "1.1.0", releaseDate = "2026-06-13" });
    }
}
