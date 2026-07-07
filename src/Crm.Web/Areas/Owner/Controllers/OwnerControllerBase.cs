using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Areas.Owner.Controllers;

/// <summary>پایه کنترلرهای پنل مالک — با کوکی ادمین سایت و نقش Admin.</summary>
[Area("Owner")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = "Admin")]
public abstract class OwnerControllerBase : Controller
{
}
