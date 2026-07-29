using System.Net;
using System.Text;

namespace Crm.Web.Areas.App.Models;

/// <summary>کمک‌رسان ساخت سلول HTML امن برای CrmTable.</summary>
public static class CrmTableHtml
{
    public static string Enc(string? value) => WebUtility.HtmlEncode(value ?? "");

    public static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : Enc(value);

    public static string Strong(string? value, string? badgeHtml = null)
    {
        var sb = new StringBuilder();
        sb.Append("<strong>").Append(Enc(value)).Append("</strong>");
        if (!string.IsNullOrWhiteSpace(badgeHtml))
            sb.Append(' ').Append(badgeHtml);
        return sb.ToString();
    }

    public static string Badge(string text, string cssClass) =>
        $"<span class=\"badge {cssClass}\">{Enc(text)}</span>";

    public static string Link(string href, object? text, string cssClass = "crm-dt-title-link") =>
        $"<a class=\"{cssClass}\" href=\"{Enc(href)}\">{Enc(text?.ToString())}</a>";

    public static string SoftLink(string href, object? text) =>
        Link(href, text, "crm-dt-soft-link");

    public static string Money(decimal amount, string suffix = " تومان") =>
        Enc(amount.ToString("N0") + suffix);

    public static string Muted(string? value) =>
        $"<span class=\"text-muted\">{Enc(string.IsNullOrWhiteSpace(value) ? "—" : value)}</span>";
}
