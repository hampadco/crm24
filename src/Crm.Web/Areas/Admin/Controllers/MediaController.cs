using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Crm.Web.Services;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
[Route("Admin/Media")]
public class MediaController : Controller
{
    private readonly MediaUploadService _uploads;
    private readonly ContentMediaService _contentMedia;

    public MediaController(MediaUploadService uploads, ContentMediaService contentMedia)
    {
        _uploads = uploads;
        _contentMedia = contentMedia;
    }

    [HttpPost("Upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        var (success, url, error) = await _uploads.UploadImageAsync(file);
        if (!success)
            return Json(new { success = false, message = error });

        return Json(new { success = true, url });
    }

    [HttpPost("UploadAudio")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAudio(IFormFile file)
    {
        var (success, url, error) = await _uploads.UploadAudioAsync(file);
        if (!success)
            return Json(new { success = false, message = error });

        return Json(new { success = true, url });
    }

    [HttpPost("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromForm] string url)
    {
        var (success, message) = await _contentMedia.TryDeleteIfUnreferencedAsync(url);
        return Json(new { success, message });
    }
}
