using Microsoft.AspNetCore.Mvc;

namespace TikTokShop.WebAPI.Controllers
{
    [ApiController]
    [Route("api/test")]
    [Tags("Tast")]
    public class TestController : ControllerBase
    {
        [HttpGet("test")]
        public IActionResult Test(){
            return Ok("eiei");
        }
    }
}
