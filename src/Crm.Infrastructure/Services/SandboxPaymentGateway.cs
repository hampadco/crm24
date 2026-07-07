using Crm.Core.Abstractions;
using Crm.Core.Entities;

namespace Crm.Infrastructure.Services;

/// <summary>
/// درگاه پرداخت آزمایشی (Sandbox) — صفحه پرداخت داخلی /pay/{token}.
/// آداپتور درگاه واقعی (زرین‌پال/...) با همین Interface جایگزین می‌شود.
/// </summary>
public class SandboxPaymentGateway : IPaymentGateway
{
    public Task<string> BeginAsync(PaymentTransaction transaction) =>
        Task.FromResult($"/pay/{transaction.Token}");
}
