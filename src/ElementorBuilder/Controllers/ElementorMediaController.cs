using ElementorBuilder.Options;
using ElementorBuilder.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ElementorBuilder.Controllers;

[Authorize]
[Route("Elementor/Media")]
public class ElementorMediaController : Controller
{
    private readonly ElementorMediaFileService _media;

    public ElementorMediaController(ElementorMediaFileService media)
    {
        _media = media;
    }

    [HttpPost("Upload")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        try
        {
            var (success, url, error) = await _media.UploadAsync(file);
            if (!success)
                return Json(new { success = false, message = error });

            return Json(new
            {
                success = true,
                url,
                fileName = file.FileName,
                size = file.Length
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"خطا در آپلود: {ex.Message}" });
        }
    }

    [HttpPost("Delete")]
    public async Task<IActionResult> Delete([FromForm] string url, CancellationToken cancellationToken)
    {
        var (success, message) = await _media.TryDeleteIfUnreferencedAsync(url, cancellationToken);
        return Json(new { success, message });
    }
}
