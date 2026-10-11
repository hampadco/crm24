using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public sealed class SmsTarget
{
    public int? RecordId { get; init; }
    public string? ModuleName { get; init; }
    public string Title { get; init; } = "";
    public string Name { get; init; } = "";
    public string PhoneE164 { get; init; } = "";
}

public sealed class SmsSendRequest
{
    public string? ModuleName { get; init; }
    public IReadOnlyList<int> RecordIds { get; init; } = [];
    public bool AllInModule { get; init; }
    public string? Query { get; init; }
    public string? ManualPhones { get; init; }
    public string Message { get; init; } = "";
    public DateTime? ScheduledUtc { get; init; }
}

public sealed class SmsSendOutcome
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public int? DispatchId { get; init; }
}

/// <summary>
/// ارسال پیامک از رکوردهای CRM. آی‌پی‌پنل واقعی است؛ کاوه‌نگار فعلاً بسته است.
/// </summary>
public class SmsService : ISmsSender
{
    public const int MaxRecipients = 1000;
    private const int ScanCap = 3000;
    private const int WebserviceChunk = 100;
    private const int PeerChunk = 40;

    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;
    private readonly TenantIntegrationService _integrations;
    private readonly IpPanelSmsClient _ip;
    private readonly ILogger<SmsService> _logger;

    public SmsService(
        CrmDbContext db,
        MetadataService metadata,
        TenantIntegrationService integrations,
        IpPanelSmsClient ip,
        ILogger<SmsService> logger)
    {
        _db = db;
        _metadata = metadata;
        _integrations = integrations;
        _ip = ip;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string to, string text)
    {
        var config = await _integrations.GetAsync();
        if (!config.SmsEnabled)
        {
            _logger.LogInformation("SMS skipped (disabled) to={To}", to);
            return true;
        }

        if (config.IsKavenegar)
        {
            _logger.LogInformation("SMS skipped (Kavenegar not implemented) to={To}", to);
            return false;
        }

        var outcome = await SendAsync(new SmsSendRequest
        {
            ManualPhones = to,
            Message = text
        });
        return outcome.Ok;
    }

    public async Task<(bool Ok, decimal? Credit, string? Error)> GetCreditAsync(CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync();
        if (config.IsKavenegar)
            return (false, null, "کاوه‌نگار هنوز وصل نشده است.");
        if (!config.HasIpPanel)
            return (false, null, "آی‌پی‌پنل فعال نیست. کلید و شماره خط را ذخیره کنید.");
        return await _ip.GetCreditAsync(config, ct);
    }

    public async Task<IReadOnlyList<SmsTarget>> SearchAsync(string moduleName, string? query, CancellationToken ct = default)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return [];
        var fields = await _metadata.GetFieldsAsync(module.Id);
        var rows = await LoadRowsAsync(module.Id, query, recordIds: null, take: 80, ct);
        var targets = await MaterializeAsync(module, fields, rows, ct);
        return targets.Take(40).ToList();
    }

    public async Task<SmsSendOutcome> SendAsync(SmsSendRequest request, CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync();
        if (config.IsKavenegar)
            return Fail("کاوه‌نگار هنوز آماده نیست. فعلاً فقط آی‌پی‌پنل ارسال می‌کند.");
        if (!config.HasIpPanel)
            return Fail("پنل پیامک فعال نیست. از یکپارچگی‌ها آی‌پی‌پنل را روشن کنید.");

        var message = (request.Message ?? "").Trim();
        if (message.Length == 0)
            return Fail("متن پیام خالی است.");
        if (message.Length > 900)
            return Fail("متن پیام طولانی است.");

        var targets = await CollectTargetsAsync(request, ct);
        if (targets.Count == 0)
            return Fail("گیرنده‌ای با شماره معتبر پیدا نشد.");
        if (targets.Count > MaxRecipients)
            return Fail($"در هر ارسال حداکثر {MaxRecipients} شماره مجاز است.");

        var personalized = targets.Select(t => (t, Text: Personalize(message, t))).ToList();
        var sameText = personalized.All(p => p.Text == message);
        var from = IpPanelSmsClient.NormalizeSender(config.SmsFrom);
        var sendUtc = request.ScheduledUtc is DateTime when && when > DateTime.UtcNow.AddMinutes(1) ? when : (DateTime?)null;

        IpPanelSendResult sent;
        if (sameText)
        {
            sent = await SendChunksAsync(config, message, personalized.Select(p => p.t.PhoneE164).ToList(), sendUtc, ct);
        }
        else
        {
            sent = await SendPeerChunksAsync(
                config,
                personalized.Select(p => (p.t.PhoneE164, p.Text)).ToList(),
                sendUtc,
                ct);
        }

        var dispatch = new SmsDispatch
        {
            Provider = "ippanel",
            Kind = personalized.Count == 1 ? "single" : sameText ? "bulk" : "personal",
            Message = message,
            FromNumber = from,
            ScheduledUtc = sendUtc,
            OutboxIdsJson = JsonSerializer.Serialize(sent.OutboxIds),
            RecipientCount = personalized.Count,
            Status = sent.Ok ? (sendUtc is null ? "sent" : "queued") : sent.Partial ? "partial" : "failed",
            Error = sent.Ok ? null : sent.Error,
            ModuleName = request.ModuleName
        };
        foreach (var item in personalized)
        {
            dispatch.Recipients.Add(new SmsRecipient
            {
                Phone = item.t.PhoneE164,
                DisplayName = string.IsNullOrWhiteSpace(item.t.Name) ? item.t.Title : item.t.Name,
                ModuleName = item.t.ModuleName,
                RecordId = item.t.RecordId,
                Message = item.Text == message ? null : item.Text,
                DeliveryStatus = sent.Ok || sent.Partial ? "pending" : "failed"
            });
        }

        _db.SmsDispatches.Add(dispatch);
        await _db.SaveChangesAsync(ct);

        if (!sent.Ok && !sent.Partial)
            return new SmsSendOutcome { Ok = false, Message = sent.Error ?? "ارسال ناموفق بود.", DispatchId = dispatch.Id };

        var note = sendUtc is null
            ? $"برای {personalized.Count} شماره ارسال شد."
            : $"ارسال {personalized.Count} شماره زمان‌بندی شد.";
        if (!string.IsNullOrWhiteSpace(sent.Error))
            note += " " + sent.Error;
        return new SmsSendOutcome { Ok = true, Message = note, DispatchId = dispatch.Id };
    }

    public async Task RefreshAsync(int dispatchId, CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync();
        if (!config.HasIpPanel)
            return;

        var dispatch = await _db.SmsDispatches.Include(d => d.Recipients)
            .FirstOrDefaultAsync(d => d.Id == dispatchId, ct);
        if (dispatch is null || dispatch.Provider != "ippanel")
            return;

        var ids = ParseOutboxIds(dispatch.OutboxIdsJson);
        if (ids.Count == 0)
            return;

        decimal cost = 0;
        var sawCost = false;
        int? worst = null;
        foreach (var id in ids)
        {
            var bulk = await _ip.GetBulkAsync(config, id, ct);
            if (!bulk.Ok)
                continue;
            if (bulk.Cost is decimal c)
            {
                cost += c;
                sawCost = true;
            }
            if (bulk.StateId is int state)
                worst = worst is null ? state : Math.Max(worst.Value, state);

            for (var page = 1; page <= 20; page++)
            {
                var recipients = await _ip.GetRecipientsAsync(config, id, page, 100, ct);
                if (!recipients.Ok)
                    break;
                foreach (var row in recipients.Rows)
                {
                    var canon = PhoneIdentity.Canonical(row.Phone);
                    var match = dispatch.Recipients.FirstOrDefault(r => PhoneIdentity.Canonical(r.Phone) == canon);
                    if (match is null)
                        continue;
                    match.DeliveryStatus = MapDelivery(row.MessageStatus);
                }
                if (page >= recipients.LastPage)
                    break;
            }
        }

        if (sawCost)
            dispatch.Cost = cost;
        dispatch.Status = Rollup(worst, dispatch.Recipients.Select(r => r.DeliveryStatus).ToList(), dispatch.ScheduledUtc);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SmsSendOutcome> CancelAsync(int dispatchId, CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync();
        if (!config.HasIpPanel)
            return Fail("آی‌پی‌پنل فعال نیست.");

        var dispatch = await _db.SmsDispatches.FirstOrDefaultAsync(d => d.Id == dispatchId, ct);
        if (dispatch is null)
            return Fail("ارسال پیدا نشد.");
        if (dispatch.ScheduledUtc is null || dispatch.ScheduledUtc <= DateTime.UtcNow.AddMinutes(5))
            return Fail("فقط ارسال زمان‌بندی‌شده، حداقل پنج دقیقه قبل از موعد، قابل لغو است.");

        foreach (var id in ParseOutboxIds(dispatch.OutboxIdsJson))
        {
            var result = await _ip.CancelAsync(config, id, ct);
            if (!result.Ok)
                return Fail(result.Error ?? "لغو در آی‌پی‌پنل پذیرفته نشد.");
        }

        dispatch.Status = "canceled";
        await _db.SaveChangesAsync(ct);
        return new SmsSendOutcome { Ok = true, Message = "ارسال لغو شد.", DispatchId = dispatch.Id };
    }

    private async Task<List<SmsTarget>> CollectTargetsAsync(SmsSendRequest request, CancellationToken ct)
    {
        var targets = new List<SmsTarget>();
        if (!string.IsNullOrWhiteSpace(request.ModuleName) && (request.AllInModule || request.RecordIds.Count > 0))
        {
            var module = await _metadata.GetModuleByNameAsync(request.ModuleName);
            if (module is not null)
            {
                var fields = await _metadata.GetFieldsAsync(module.Id);
                var rows = request.AllInModule
                    ? await LoadRowsAsync(module.Id, request.Query, null, ScanCap, ct)
                    : await LoadRowsAsync(module.Id, null, request.RecordIds, request.RecordIds.Count, ct);
                targets.AddRange(await MaterializeAsync(module, fields, rows, ct));
            }
        }

        foreach (var raw in SplitPhones(request.ManualPhones))
        {
            var e164 = IpPanelSmsClient.ToE164(raw);
            if (e164 is null)
                continue;
            targets.Add(new SmsTarget { Title = IpPanelSmsClient.Display(e164), Name = "", PhoneE164 = e164 });
        }

        return Dedupe(targets).Take(MaxRecipients + 1).ToList();
    }

    private async Task<List<DynamicRecord>> LoadRowsAsync(
        int moduleId, string? query, IReadOnlyList<int>? recordIds, int take, CancellationToken ct)
    {
        var q = _db.Records.AsNoTracking().Where(r => r.ModuleId == moduleId);
        if (recordIds is { Count: > 0 })
        {
            var ids = recordIds.Distinct().Take(MaxRecipients).ToList();
            q = q.Where(r => ids.Contains(r.Id));
        }
        else if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(r => r.Title.Contains(term));
        }

        return await q.OrderByDescending(r => r.Id).Take(take).ToListAsync(ct);
    }

    private async Task<List<SmsTarget>> MaterializeAsync(
        ModuleDef module, IReadOnlyList<FieldDef> fields, IReadOnlyList<DynamicRecord> rows, CancellationToken ct)
    {
        var phoneNames = PhoneFieldNames(fields);
        var partyLookups = fields
            .Where(f => f.Type == FieldType.Lookup && PartyLinkService.PartyModules.Contains(f.LookupModule ?? ""))
            .OrderBy(f => f.LookupModule == "contacts" ? 0 : f.LookupModule == "leads" ? 1 : 2)
            .ToList();

        var targets = new List<SmsTarget>();
        var pending = new List<(DynamicRecord Row, string LookupModule, int PartyId)>();

        foreach (var row in rows)
        {
            var data = DynamicRecordService.ParseData(row);
            if (IsOptOut(data))
                continue;
            var phone = FirstPhone(data, phoneNames);
            if (phone is not null)
            {
                targets.Add(ToTarget(module.Name, row, data, phone));
                continue;
            }

            foreach (var lookup in partyLookups)
            {
                if (int.TryParse(data.GetValueOrDefault(lookup.Name), out var partyId)
                    && partyId > 0
                    && lookup.LookupModule is not null)
                {
                    pending.Add((row, lookup.LookupModule, partyId));
                    break;
                }
            }
        }

        foreach (var group in pending.GroupBy(p => p.LookupModule, StringComparer.OrdinalIgnoreCase))
        {
            var partyModule = await _metadata.GetModuleByNameAsync(group.Key);
            if (partyModule is null)
                continue;
            var partyFields = await _metadata.GetFieldsAsync(partyModule.Id);
            var names = PhoneFieldNames(partyFields);
            var ids = group.Select(g => g.PartyId).Distinct().ToList();
            var parties = await _db.Records.AsNoTracking()
                .Where(r => r.ModuleId == partyModule.Id && ids.Contains(r.Id))
                .ToListAsync(ct);
            var map = parties.ToDictionary(p => p.Id);
            foreach (var item in group)
            {
                if (!map.TryGetValue(item.PartyId, out var party))
                    continue;
                var data = DynamicRecordService.ParseData(party);
                if (IsOptOut(data))
                    continue;
                var phone = FirstPhone(data, names);
                if (phone is null)
                    continue;
                var sourceData = DynamicRecordService.ParseData(item.Row);
                targets.Add(ToTarget(module.Name, item.Row, sourceData, phone));
            }
        }

        return Dedupe(targets);
    }

    private async Task<IpPanelSendResult> SendChunksAsync(
        IntegrationConfig config, string message, List<string> phones, DateTime? sendUtc, CancellationToken ct)
    {
        var ids = new List<long>();
        string? error = null;
        foreach (var chunk in phones.Chunk(WebserviceChunk))
        {
            var result = await _ip.SendSameTextAsync(config, message, chunk, sendUtc, ct);
            ids.AddRange(result.OutboxIds);
            if (!result.Ok)
            {
                error = result.Error;
                return new IpPanelSendResult { Ok = false, Partial = ids.Count > 0, Error = error, OutboxIds = ids };
            }
        }
        return new IpPanelSendResult { Ok = true, OutboxIds = ids };
    }

    private async Task<IpPanelSendResult> SendPeerChunksAsync(
        IntegrationConfig config, List<(string Phone, string Message)> items, DateTime? sendUtc, CancellationToken ct)
    {
        var ids = new List<long>();
        foreach (var chunk in items.Chunk(PeerChunk))
        {
            var result = await _ip.SendPeerAsync(config, chunk, sendUtc, ct);
            ids.AddRange(result.OutboxIds);
            if (!result.Ok)
                return new IpPanelSendResult { Ok = false, Partial = ids.Count > 0, Error = result.Error, OutboxIds = ids };
        }
        return new IpPanelSendResult { Ok = true, OutboxIds = ids };
    }

    private static List<string> PhoneFieldNames(IReadOnlyList<FieldDef> fields)
    {
        var names = fields
            .Where(f => f.Type == FieldType.Phone || f.Name is "mobile" or "phone" or "phoneNumber")
            .Select(f => f.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var preferred in new[] { "mobile", "phone", "phoneNumber" })
        {
            var index = names.FindIndex(n => n.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (index > 0)
            {
                var name = names[index];
                names.RemoveAt(index);
                names.Insert(0, name);
            }
        }
        return names;
    }

    private static string? FirstPhone(IReadOnlyDictionary<string, string?> data, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var e164 = IpPanelSmsClient.ToE164(data.GetValueOrDefault(name));
            if (e164 is not null)
                return e164;
        }
        return null;
    }

    private static bool IsOptOut(IReadOnlyDictionary<string, string?> data)
    {
        var raw = data.GetValueOrDefault("doNotMessage");
        return raw is "true" or "1" or "on" or "yes";
    }

    private static SmsTarget ToTarget(string moduleName, DynamicRecord row, IReadOnlyDictionary<string, string?> data, string e164)
    {
        var name = data.GetValueOrDefault("name");
        if (string.IsNullOrWhiteSpace(name))
            name = row.Title;
        return new SmsTarget
        {
            RecordId = row.Id,
            ModuleName = moduleName,
            Title = row.Title,
            Name = name ?? "",
            PhoneE164 = e164
        };
    }

    private static string Personalize(string template, SmsTarget target)
    {
        return template
            .Replace("{name}", target.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", target.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{phone}", IpPanelSmsClient.Display(target.PhoneE164), StringComparison.OrdinalIgnoreCase);
    }

    private static List<SmsTarget> Dedupe(IEnumerable<SmsTarget> targets)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<SmsTarget>();
        foreach (var target in targets)
        {
            if (seen.Add(target.PhoneE164))
                list.Add(target);
        }
        return list;
    }

    private static IEnumerable<string> SplitPhones(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;
        foreach (var part in raw.Split([',', ';', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            yield return part.Trim();
    }

    private static List<long> ParseOutboxIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<long>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string MapDelivery(string? status) => status switch
    {
        "0" => "operator",
        "1" => "received",
        "2" => "delivered",
        "3" => "failed",
        "4" => "blacklist",
        _ => "pending"
    };

    private static string Rollup(int? stateId, IReadOnlyList<string> deliveries, DateTime? scheduledUtc)
    {
        if (stateId is 7)
            return "canceled";
        if (stateId is 3 or 4 or 8 or 9)
            return "failed";
        if (deliveries.Count > 0 && deliveries.All(s => s is "delivered" or "failed" or "blacklist"))
        {
            if (deliveries.All(s => s == "delivered"))
                return "delivered";
            if (deliveries.Any(s => s == "delivered"))
                return "partial";
            return "failed";
        }
        if (stateId is 0 or 1 or 5)
            return scheduledUtc is not null ? "queued" : "sending";
        if (stateId is 2)
            return "sending";
        return "sent";
    }

    private static SmsSendOutcome Fail(string message) => new() { Ok = false, Message = message };
}
