using Microsoft.AspNetCore.Mvc;

namespace OpenIRP.Api.Controllers
{
    [ApiController]
    [Route("api/health")]
    public sealed class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                status = "Healthy",
                service = "OpenIRP.Api"
            });
        }
    }
}