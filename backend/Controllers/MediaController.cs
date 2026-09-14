using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Controllers;

[ApiController]
[Route("api/media")]
[Authorize]
public class MediaController : ControllerBase
{
    private readonly IMediaStorageService _media;

    public MediaController(IMediaStorageService media) => _media = media;

    [HttpGet("{fileName}")]
    public async Task<IActionResult> Get(string fileName)
    {
        var opened = await _media.OpenAsync(fileName);
        if (opened is null) return NotFound();
        var (stream, contentType) = opened.Value;
        Response.Headers.CacheControl = "private, max-age=300";
        return File(stream, contentType);
    }
}
