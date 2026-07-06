using Microsoft.AspNetCore.Mvc;

namespace StoreSystem.Api.Controllers;

/// <summary>
/// Yengil "tirik" tekshiruvi. Desktop ilova topilgan yoki yozilgan manzilning
/// haqiqatan JESKO server ekanini shu endpoint orqali tekshiradi (bazaga tegmaydi).
/// GET /api/ping
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        ok = true,
        app = "JESKO",
        name = Environment.MachineName,
        version = "1.0.0",
        timeUtc = DateTime.UtcNow
    });
}
