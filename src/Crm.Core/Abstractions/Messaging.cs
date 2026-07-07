namespace Crm.Core.Abstractions;

/// <summary>ارسال ایمیل — آداپتور SMTP واقعی در پلن ۹ وصل می‌شود.</summary>
public interface IEmailSender
{
    Task<bool> SendAsync(string to, string subject, string body);
}

/// <summary>ارسال پیامک/پیام‌رسان — آداپتور پنل ایرانی در پلن ۹ وصل می‌شود.</summary>
public interface ISmsSender
{
    Task<bool> SendAsync(string to, string text);
}

/// <summary>ارسال سند به نرم‌افزار حسابداری — آداپتور واقعی در پلن ۹.</summary>
/// <summary>درگاه پرداخت — آدرس صفحه پرداخت را برمی‌گرداند.</summary>
public interface IPaymentGateway
{
    Task<string> BeginAsync(Crm.Core.Entities.PaymentTransaction transaction);
}

/// <summary>ارسال پیام از طریق پیام‌رسان (بله/تلگرام/واتساپ).</summary>
public interface IMessengerSender
{
    Task<bool> SendAsync(string chatId, string text);
}

public interface IAccountingGateway
{
    Task<bool> PushAsync(string documentType, int documentId, string payloadJson);
}
