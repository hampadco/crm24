using Crm.Core.Entities;

namespace Crm.Web.Areas.App.Models;

public class SmsIndexModel
{
    public bool Ready { get; set; }
    public bool KavenegarSelected { get; set; }
    public string? CreditText { get; set; }
    public string? FromDisplay { get; set; }
    public List<SmsModuleOption> Modules { get; set; } = [];
    public List<MessageTemplate> Templates { get; set; } = [];
    public List<SmsDispatch> Recent { get; set; } = [];
}

public class SmsModuleOption
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
}

public static class SmsLabels
{
    public static string Status(string? code) => code switch
    {
        "queued" => "در صف",
        "sending" => "در حال ارسال",
        "sent" => "ارسال شد",
        "delivered" => "رسیده",
        "partial" => "ناقص",
        "failed" => "ناموفق",
        "canceled" => "لغو شد",
        _ => "نامشخص"
    };

    public static string Badge(string? code) => code switch
    {
        "delivered" => "bg-label-success",
        "sent" or "sending" => "bg-label-info",
        "partial" => "bg-label-warning",
        "failed" => "bg-label-danger",
        _ => "bg-label-secondary"
    };

    public static string Delivery(string? code) => code switch
    {
        "operator" => "نزد اپراتور",
        "received" => "دریافت اپراتور",
        "delivered" => "رسیده",
        "failed" => "نرسیده",
        "blacklist" => "بلک‌لیست",
        _ => "در انتظار"
    };
}
