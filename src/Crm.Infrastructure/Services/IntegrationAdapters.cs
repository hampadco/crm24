using System.Net;
using System.Net.Mail;
using System.Text;
using Hangfire;
using Microsoft.Extensions.Logging;
using Crm.Core.Abstractions;

namespace Crm.Infrastructure.Services;

/// <summary>
/// آداپتورهای واقعی پلن ۹. همه در نبودِ پیکربندی Tenant به لاگ fallback می‌کنند
/// تا گردش‌کارها در محیط توسعه هم بدون خطا اجرا شوند.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly TenantIntegrationService _integrations;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(TenantIntegrationService integrations, ILogger<SmtpEmailSender> logger)
    {
        _integrations = integrations;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string to, string subject, string body)
    {
        var config = await _integrations.GetAsync();
        if (!config.HasSmtp)
        {
            _logger.LogInformation("EMAIL (no SMTP config) to={To} subject={Subject}: {Body}", to, subject, body);
            return true;
        }

        try
        {
            using var client = new SmtpClient(config.SmtpHost!, config.SmtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(config.SmtpUser, config.SmtpPassword)
            };
            using var message = new MailMessage(config.SmtpFrom ?? config.SmtpUser ?? "crm@localhost", to, subject, body)
            {
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP send failed to {To}", to);
            return false;
        }
    }
}

/// <summary>آداپتور پنل پیامک ایرانی — POST ساده به آدرس پیکربندی‌شده.</summary>
public class HttpSmsSender : ISmsSender
{
    private readonly TenantIntegrationService _integrations;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HttpSmsSender> _logger;

    public HttpSmsSender(TenantIntegrationService integrations, IHttpClientFactory httpFactory, ILogger<HttpSmsSender> logger)
    {
        _integrations = integrations;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string to, string text)
    {
        var config = await _integrations.GetAsync();
        if (!config.HasSms)
        {
            _logger.LogInformation("SMS (no config) to={To}: {Text}", to, text);
            return true;
        }

        try
        {
            var client = _httpFactory.CreateClient("sms");
            var payload = new Dictionary<string, string>
            {
                ["receptor"] = to,
                ["message"] = text,
                ["sender"] = config.SmsFrom ?? "",
                ["apikey"] = config.SmsApiKey ?? ""
            };
            var response = await client.PostAsync(config.SmsApiUrl, new FormUrlEncodedContent(payload));
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS send failed to {To}", to);
            return false;
        }
    }
}

/// <summary>آداپتور پیام‌رسان بله (Bot API سازگار با تلگرام).</summary>
public class BaleMessengerSender : IMessengerSender
{
    private readonly TenantIntegrationService _integrations;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<BaleMessengerSender> _logger;

    public BaleMessengerSender(TenantIntegrationService integrations, IHttpClientFactory httpFactory, ILogger<BaleMessengerSender> logger)
    {
        _integrations = integrations;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string chatId, string text)
    {
        var config = await _integrations.GetAsync();
        if (!config.HasBale)
        {
            _logger.LogInformation("BALE (no config) chat={Chat}: {Text}", chatId, text);
            return true;
        }

        try
        {
            var client = _httpFactory.CreateClient("bale");
            var url = $"https://tapi.bale.ai/bot{config.BaleBotToken}/sendMessage";
            var payload = new Dictionary<string, string> { ["chat_id"] = chatId, ["text"] = text };
            var response = await client.PostAsync(url, new FormUrlEncodedContent(payload));
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bale send failed to {Chat}", chatId);
            return false;
        }
    }
}

/// <summary>
/// ارسال به حسابداری از طریق صف Hangfire با retry —
/// سند تأییدشده حتی اگر وب‌سرویس مقصد موقتاً در دسترس نباشد گم نمی‌شود.
/// </summary>
public class QueuedAccountingGateway : IAccountingGateway
{
    private readonly IBackgroundJobClient _jobs;
    private readonly ITenantContext _tenant;

    public QueuedAccountingGateway(IBackgroundJobClient jobs, ITenantContext tenant)
    {
        _jobs = jobs;
        _tenant = tenant;
    }

    public Task<bool> PushAsync(string documentType, int documentId, string payloadJson)
    {
        var tenantId = _tenant.TenantId ?? 0;
        _jobs.Enqueue<AccountingPushJob>(job =>
            job.ExecuteAsync(tenantId, documentType, documentId, payloadJson));
        return Task.FromResult(true);
    }
}

public class AccountingPushJob
{
    private readonly TenantIntegrationService _integrations;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AccountingPushJob> _logger;

    public AccountingPushJob(TenantIntegrationService integrations, IHttpClientFactory httpFactory, ILogger<AccountingPushJob> logger)
    {
        _integrations = integrations;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 60, 300, 900, 3600, 10800 })]
    public async Task ExecuteAsync(int tenantId, string documentType, int documentId, string payloadJson)
    {
        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasAccounting)
        {
            _logger.LogInformation("ACCOUNTING (no config) {Type}#{Id}: {Payload}", documentType, documentId, payloadJson);
            return;
        }

        var client = _httpFactory.CreateClient("accounting");
        var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(config.AccountingWebhookUrl, content);
        response.EnsureSuccessStatusCode(); // شکست → retry خودکار Hangfire
    }
}
