using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Areas.Portal.Controllers;

/// <summary>پایه کنترلرهای پورتال مشتریان نهایی — کوکی جداگانه Portal.</summary>
[Area("Portal")]
[Authorize(AuthenticationSchemes = "Portal")]
public abstract class PortalControllerBase : Controller
{
    protected int PortalUserId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    protected int? PortalContactRecordId =>
        int.TryParse(User.FindFirst("portal:contact")?.Value, out var id) ? id : null;
}
