using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Web.Services;

namespace Crm.Web.Areas.App.Controllers;

public class NexSlice
{
    public string Label { get; set; } = "";
    public int Value { get; set; }
    public int Percent { get; set; }
}

public class NexMeeting
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "";
    public string When { get; set; } = "";
    public string Place { get; set; } = "";
}

public class NexCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string When { get; set; } = "";
}

public class NexTaskRow
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string When { get; set; } = "";
    public bool Done { get; set; }
}

public class NexSeries
{
    public string[] Labels { get; set; } = [];
    public int[] Data { get; set; } = [];
}

public class NexCardVm
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public NexDashVm Nex { get; set; } = new();
}

public class NexDashVm
{
    public int Contacts { get; set; }
    public string ContactsDelta { get; set; } = "۰٪";
    public bool ContactsUp { get; set; } = true;
    public string ContactsPrev { get; set; } = "۰";
    public int Leads { get; set; }
    public string LeadsDelta { get; set; } = "۰٪";
    public bool LeadsUp { get; set; } = true;
    public int TasksDone { get; set; }
    public int TasksTotal { get; set; }
    public int TasksPercent { get; set; }
    public List<NexSlice> TaskSlices { get; set; } = [];
    public int ActiveDeals { get; set; }
    public string DealsDelta { get; set; } = "۰٪";
    public bool DealsUp { get; set; } = true;
    public string DealsPrev { get; set; } = "۰";
    public string RevenueHint { get; set; } = "";
    public List<NexSlice> Sources { get; set; } = [];
    public int Retention { get; set; }
    public string RetentionDelta { get; set; } = "۰٪";
    public bool RetentionUp { get; set; } = true;
    public string EarningYear { get; set; } = "۰";
    public string EarningMonth { get; set; } = "۰";
    public string EarningOrders { get; set; } = "۰";
    public int EarningGauge { get; set; }
    public int OrdersPaid { get; set; }
    public int OrdersCanceled { get; set; }
    public int OrdersRefunded { get; set; }
    public int OrdersPaidPct { get; set; }
    public int OrdersCanceledPct { get; set; }
    public int OrdersRefundedPct { get; set; }
    public List<NexMeeting> Meetings { get; set; } = [];
    public int ClosedDeals { get; set; }
    public string PipelineValue { get; set; } = "۰";
    public string DealsTrend { get; set; } = "۰٪";
    public bool DealsTrendUp { get; set; } = true;
    public List<NexSlice> Funnel { get; set; } = [];
    public string FunnelTotal { get; set; } = "۰";
    public string FunnelDelta { get; set; } = "۰٪";
    public bool FunnelUp { get; set; } = true;
    public List<NexCustomer> Customers { get; set; } = [];
    public List<NexTaskRow> Tasks { get; set; } = [];
    public string ChartJson { get; set; } = "{}";
}

public partial class DashboardController
{
    private async Task<NexDashVm> BuildNexAsync(
        IReadOnlyList<ModuleDef> modules,
        TimeZoneInfo zone,
        PersianCalendar pc,
        int year,
        int month)
    {
        var vm = new NexDashVm();
        var byName = modules.ToDictionary(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase);
        int? Id(string name) => byName.TryGetValue(name, out var id) ? id : null;

        var wanted = new[] { "contacts", "leads", "tasks", "opportunities", "events", "invoices", "payments", "sales_orders" }
            .Select(Id)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        var rows = wanted.Count == 0
            ? []
            : await _db.Records.AsNoTracking()
                .Where(r => wanted.Contains(r.ModuleId))
                .Select(r => new NexRow(r.Id, r.ModuleId, r.Title, r.CreatedAtUtc, r.CustomData))
                .ToListAsync();

        var now = DateTime.UtcNow;
        var monthStart = MonthStartUtc(pc, zone, year, month);
        var prev = month == 1 ? (year - 1, 12) : (year, month - 1);
        var prevStart = MonthStartUtc(pc, zone, prev.Item1, prev.Item2);
        var todayStart = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date, DateTimeKind.Unspecified), zone);

        List<NexRow> Of(string name) =>
            Id(name) is int id ? rows.Where(r => r.ModuleId == id).ToList() : [];

        var contacts = Of("contacts");
        var leads = Of("leads");
        var tasks = Of("tasks");
        var opps = Of("opportunities");
        var events = Of("events");
        var invoices = Of("invoices");
        var payments = Of("payments");
        var orders = Of("sales_orders");

        var contactMonths = MonthCounts(contacts, pc, zone, year, month, 6);
        var leadMonths = MonthCounts(leads, pc, zone, year, month, 6);
        vm.Contacts = contacts.Count;
        vm.ContactsPrev = contacts.Count(r => r.CreatedAtUtc >= prevStart && r.CreatedAtUtc < monthStart).ToFa();
        (vm.ContactsDelta, vm.ContactsUp) = Delta(
            contacts.Count(r => r.CreatedAtUtc >= monthStart),
            contacts.Count(r => r.CreatedAtUtc >= prevStart && r.CreatedAtUtc < monthStart));
        vm.Leads = leads.Count;
        (vm.LeadsDelta, vm.LeadsUp) = Delta(
            leads.Count(r => r.CreatedAtUtc >= monthStart),
            leads.Count(r => r.CreatedAtUtc >= prevStart && r.CreatedAtUtc < monthStart));

        var taskStatus = tasks.Select(r => Field(r.Json, "status") ?? "todo").ToList();
        vm.TasksTotal = tasks.Count;
        vm.TasksDone = taskStatus.Count(s => s.Equals("done", StringComparison.OrdinalIgnoreCase));
        vm.TasksPercent = vm.TasksTotal == 0 ? 0 : (int)Math.Round(vm.TasksDone * 100.0 / vm.TasksTotal);
        vm.TaskSlices =
        [
            Slice("در انتظار", taskStatus.Count(s => s.Equals("todo", StringComparison.OrdinalIgnoreCase)), vm.TasksTotal),
            Slice("در حال انجام", taskStatus.Count(s => s.Equals("inprogress", StringComparison.OrdinalIgnoreCase)), vm.TasksTotal),
            Slice("لغو شده", taskStatus.Count(s => s.Equals("canceled", StringComparison.OrdinalIgnoreCase)), vm.TasksTotal)
        ];

        bool OpenDeal(NexRow row)
        {
            var stage = Field(row.Json, "stage") ?? "";
            return !stage.Equals("won", StringComparison.OrdinalIgnoreCase)
                && !stage.Equals("lost", StringComparison.OrdinalIgnoreCase);
        }

        var active = opps.Where(OpenDeal).ToList();
        vm.ActiveDeals = active.Count;
        vm.DealsPrev = active.Count(r => r.CreatedAtUtc >= prevStart && r.CreatedAtUtc < monthStart).ToFa();
        (vm.DealsDelta, vm.DealsUp) = Delta(
            active.Count(r => r.CreatedAtUtc >= monthStart),
            active.Count(r => r.CreatedAtUtc >= prevStart && r.CreatedAtUtc < monthStart));

        var moneyRows = payments.Count > 0 ? payments : invoices.Count > 0 ? invoices : orders;
        var money = moneyRows.Select(r =>
        {
            var amount = Money(Field(r.Json, "grandTotal"));
            if (amount == 0) amount = Money(Field(r.Json, "amount"));
            var when = Stamp(First(Field(r.Json, "paidAt"), Field(r.Json, "issueDate"), Field(r.Json, "orderDate")), r.CreatedAtUtc);
            return (amount, when);
        }).ToList();

        var yearStart = MonthStartUtc(pc, zone, year, 1);
        decimal SumSince(DateTime start) => money.Where(m => m.when >= start).Sum(m => m.amount);
        var yearTotal = SumSince(yearStart);
        var monthTotal = SumSince(monthStart);
        vm.EarningYear = yearTotal.ToFa();
        vm.EarningMonth = monthTotal.ToFa();
        vm.EarningOrders = money.Count(m => m.when >= yearStart).ToFa();
        vm.EarningGauge = yearTotal <= 0 ? 0 : (int)Math.Min(100, Math.Round(monthTotal * 100m / yearTotal));

        var monthSeries = MoneyByMonth(money, pc, zone, year, month, 12);
        var weekSeries = MoneyByDay(money, zone, 7);
        var todaySeries = MoneyByHour(money, zone, todayStart);
        vm.RevenueHint = monthTotal.ToFa() + " تومان این ماه";

        var sourceCounts = leads
            .GroupBy(r => Field(r.Json, "source") ?? "")
            .Select(g => (Key: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .Take(5)
            .ToList();
        if (sourceCounts.Count == 0)
            sourceCounts.Add(("", 0));
        var sourceTotal = Math.Max(1, sourceCounts.Sum(s => s.Count));
        vm.Sources = sourceCounts.Select(s => Slice(SourceLabel(s.Key), s.Count, sourceTotal)).ToList();

        int WonIn(DateTime start, DateTime end) => opps.Count(r =>
            (Field(r.Json, "stage") ?? "").Equals("won", StringComparison.OrdinalIgnoreCase)
            && r.CreatedAtUtc >= start && r.CreatedAtUtc < end);
        int OppIn(DateTime start, DateTime end) => opps.Count(r => r.CreatedAtUtc >= start && r.CreatedAtUtc < end);
        int Rate(DateTime start, DateTime end)
        {
            var all = OppIn(start, end);
            return all == 0 ? 0 : (int)Math.Round(WonIn(start, end) * 100.0 / all);
        }
        var wonAll = opps.Count(r => (Field(r.Json, "stage") ?? "").Equals("won", StringComparison.OrdinalIgnoreCase));
        vm.Retention = opps.Count == 0 ? 0 : (int)Math.Round(wonAll * 100.0 / opps.Count);
        var thisRate = Rate(monthStart, now.AddDays(1));
        var prevRate = Rate(prevStart, monthStart);
        (vm.RetentionDelta, vm.RetentionUp) = Delta(thisRate, prevRate);
        var retention = RetentionMonths(opps, pc, zone, year, month, 6);

        var statusRows = invoices.Count > 0 ? invoices : orders;
        int StatusCount(params string[] keys) => statusRows.Count(r =>
            keys.Contains(Field(r.Json, "status") ?? "", StringComparer.OrdinalIgnoreCase));
        vm.OrdersPaid = invoices.Count > 0
            ? StatusCount("Paid", "PartiallyPaid")
            : StatusCount("Confirmed", "Converted");
        vm.OrdersCanceled = StatusCount("Canceled");
        vm.OrdersRefunded = StatusCount("Refunded");
        var orderTotal = Math.Max(1, vm.OrdersPaid + vm.OrdersCanceled + vm.OrdersRefunded);
        vm.OrdersPaidPct = vm.OrdersPaid * 100 / orderTotal;
        vm.OrdersCanceledPct = vm.OrdersCanceled * 100 / orderTotal;
        vm.OrdersRefundedPct = vm.OrdersRefunded * 100 / orderTotal;
        if (vm.OrdersPaid + vm.OrdersCanceled + vm.OrdersRefunded == 0)
        {
            vm.OrdersPaidPct = 0;
            vm.OrdersCanceledPct = 0;
            vm.OrdersRefundedPct = 0;
        }

        var heat = Heatmap(statusRows, zone);

        vm.Meetings = events
            .Select(r =>
            {
                var start = Stamp(Field(r.Json, "startAt"), r.CreatedAtUtc);
                var end = Stamp(Field(r.Json, "endAt"), start);
                return (r, start, end);
            })
            .Where(x => x.start >= now.AddHours(-1))
            .OrderBy(x => x.start)
            .Take(6)
            .Select(x => new NexMeeting
            {
                Id = x.r.Id,
                Title = string.IsNullOrWhiteSpace(x.r.Title) ? "رویداد" : x.r.Title,
                Kind = EventKind(Field(x.r.Json, "type")),
                Place = string.IsNullOrWhiteSpace(Field(x.r.Json, "location")) ? "بدون مکان" : Field(x.r.Json, "location")!,
                When = PersianDateHelper.ToJalaliDateTime(x.start).Split(' ').Last()
                    + (x.end > x.start ? " – " + PersianDateHelper.ToJalaliDateTime(x.end).Split(' ').Last() : "")
            })
            .ToList();

        vm.ClosedDeals = wonAll;
        var pipelineMoney = opps.Where(OpenDeal).Sum(r =>
        {
            var amount = Money(Field(r.Json, "amount"));
            return amount;
        });
        vm.PipelineValue = pipelineMoney.ToFa();
        var dealMonths = MonthCounts(opps, pc, zone, year, month, 6);
        (vm.DealsTrend, vm.DealsTrendUp) = Delta(
            opps.Count(r => r.CreatedAtUtc >= monthStart),
            opps.Count(r => r.CreatedAtUtc >= prevStart && r.CreatedAtUtc < monthStart));

        var qualified = leads.Count(r => (Field(r.Json, "status") ?? "").Equals("qualified", StringComparison.OrdinalIgnoreCase));
        vm.Funnel =
        [
            Slice("سرنخ‌ها", leads.Count, 1),
            Slice("واجد شرایط", qualified, 1),
            Slice("فرصت‌ها", active.Count, 1),
            Slice("بسته‌شده", wonAll, 1)
        ];
        var funnelMax = Math.Max(1, vm.Funnel.Max(f => f.Value));
        foreach (var step in vm.Funnel)
            step.Percent = (int)Math.Round(step.Value * 100.0 / funnelMax);
        vm.FunnelTotal = pipelineMoney.ToFa();
        vm.FunnelDelta = vm.DealsTrend;
        vm.FunnelUp = vm.DealsTrendUp;

        vm.Customers = contacts
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(8)
            .Select(r => new NexCustomer
            {
                Id = r.Id,
                Name = string.IsNullOrWhiteSpace(r.Title) ? Field(r.Json, "name") ?? "مخاطب" : r.Title,
                Phone = Field(r.Json, "mobile") ?? Field(r.Json, "phone") ?? "—",
                Email = Field(r.Json, "email") ?? "—",
                When = PersianDateHelper.ToJalaliDate(r.CreatedAtUtc)
            })
            .ToList();

        vm.Tasks = tasks
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(8)
            .Select(r =>
            {
                var due = Stamp(Field(r.Json, "dueDate"), r.CreatedAtUtc);
                var status = Field(r.Json, "status") ?? "";
                return new NexTaskRow
                {
                    Id = r.Id,
                    Title = string.IsNullOrWhiteSpace(r.Title) ? Field(r.Json, "name") ?? "وظیفه" : r.Title,
                    When = PersianDateHelper.ToJalaliDateTime(due).Split(' ').Last(),
                    Done = status.Equals("done", StringComparison.OrdinalIgnoreCase)
                };
            })
            .ToList();

        vm.ChartJson = JsonSerializer.Serialize(new
        {
            contacts = new { labels = contactMonths.Labels, data = contactMonths.Data },
            leads = new { labels = leadMonths.Labels, data = leadMonths.Data },
            tasks = vm.TaskSlices.Select(s => new { s.Label, s.Value }),
            revenue = new { month = monthSeries, week = weekSeries, today = todaySeries },
            sources = vm.Sources.Select(s => new { s.Label, s.Percent }),
            retention,
            gauge = vm.EarningGauge,
            gaugeLabel = vm.EarningYear,
            heat,
            deals = new { labels = dealMonths.Labels, data = dealMonths.Data }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return vm;
    }

    private sealed record NexRow(int Id, int ModuleId, string Title, DateTime CreatedAtUtc, string Json);

    private static NexSlice Slice(string label, int value, int total) => new()
    {
        Label = label,
        Value = value,
        Percent = total <= 0 ? 0 : (int)Math.Round(value * 100.0 / total)
    };

    private static (string Text, bool Up) Delta(int current, int previous)
    {
        if (previous <= 0)
            return current <= 0 ? ("۰٪", true) : ("+۱۰۰٪", true);
        var pct = (int)Math.Round((current - previous) * 100.0 / previous);
        var up = pct >= 0;
        var body = Math.Abs(pct).ToFa();
        return ((up ? "+" : "−") + body + "٪", up);
    }

    private static string SourceLabel(string key) => key.ToLowerInvariant() switch
    {
        "website" => "وب‌سایت",
        "referral" => "معرفی",
        "ads" => "تبلیغات",
        "social" => "شبکه اجتماعی",
        "exhibition" => "نمایشگاه",
        "call" => "تماس ورودی",
        "" => "نامشخص",
        _ => key
    };

    private static string EventKind(string? type) => (type ?? "").ToLowerInvariant() switch
    {
        "visit" => "بازدید",
        "demo" => "دمو",
        "other" => "سایر",
        _ => "جلسه"
    };

    private static string? Field(string json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(name, out var el))
                return null;
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static decimal Money(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;
        var text = PersianFormattingHelper.FromPersianDigits(raw)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("٬", "", StringComparison.Ordinal)
            .Trim();
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static DateTime Stamp(string? raw, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        var text = PersianFormattingHelper.FromPersianDigits(raw).Trim();
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.Kind switch
            {
                DateTimeKind.Utc => parsed,
                DateTimeKind.Local => parsed.ToUniversalTime(),
                _ => DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            };
        }

        if (text.Length >= 8 && text[0] == '1')
        {
            try
            {
                return PersianDateHelper.FromJalaliDateTime(text);
            }
            catch (ArgumentOutOfRangeException)
            {
                return fallback;
            }
        }

        return fallback;
    }

    private static DateTime MonthStartUtc(PersianCalendar pc, TimeZoneInfo zone, int year, int month)
    {
        var local = pc.ToDateTime(year, month, 1, 0, 0, 0, 0);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    }

    private static NexSeries MonthCounts(List<NexRow> rows, PersianCalendar pc, TimeZoneInfo zone, int year, int month, int take)
    {
        var labels = new List<string>();
        var data = new List<int>();
        for (var i = take - 1; i >= 0; i--)
        {
            var cursor = month - i;
            var cursorYear = year;
            while (cursor <= 0)
            {
                cursor += 12;
                cursorYear--;
            }

            var start = MonthStartUtc(pc, zone, cursorYear, cursor);
            var nextMonth = cursor == 12 ? 1 : cursor + 1;
            var nextYear = cursor == 12 ? cursorYear + 1 : cursorYear;
            var end = MonthStartUtc(pc, zone, nextYear, nextMonth);
            labels.Add(PersianMonths[cursor]);
            data.Add(rows.Count(r => r.CreatedAtUtc >= start && r.CreatedAtUtc < end));
        }

        return new NexSeries { Labels = labels.ToArray(), Data = data.ToArray() };
    }

    private static NexSeries MoneyByMonth(List<(decimal amount, DateTime when)> rows, PersianCalendar pc, TimeZoneInfo zone, int year, int month, int take)
    {
        var labels = new List<string>();
        var data = new List<int>();
        for (var i = take - 1; i >= 0; i--)
        {
            var cursor = month - i;
            var cursorYear = year;
            while (cursor <= 0)
            {
                cursor += 12;
                cursorYear--;
            }

            var start = MonthStartUtc(pc, zone, cursorYear, cursor);
            var nextMonth = cursor == 12 ? 1 : cursor + 1;
            var nextYear = cursor == 12 ? cursorYear + 1 : cursorYear;
            var end = MonthStartUtc(pc, zone, nextYear, nextMonth);
            labels.Add(PersianMonths[cursor]);
            data.Add((int)Math.Round(rows.Where(r => r.when >= start && r.when < end).Sum(r => r.amount)));
        }

        return new NexSeries { Labels = labels.ToArray(), Data = data.ToArray() };
    }

    private static NexSeries MoneyByDay(List<(decimal amount, DateTime when)> rows, TimeZoneInfo zone, int days)
    {
        var labels = new List<string>();
        var data = new List<int>();
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        for (var i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), zone);
            var end = start.AddDays(1);
            labels.Add(PersianFormattingHelper.ToPersianDigits(day.ToString("MM/dd", CultureInfo.InvariantCulture)));
            data.Add((int)Math.Round(rows.Where(r => r.when >= start && r.when < end).Sum(r => r.amount)));
        }

        return new NexSeries { Labels = labels.ToArray(), Data = data.ToArray() };
    }

    private static NexSeries MoneyByHour(List<(decimal amount, DateTime when)> rows, TimeZoneInfo zone, DateTime todayStartUtc)
    {
        var labels = new[] { "۰–۴", "۴–۸", "۸–۱۲", "۱۲–۱۶", "۱۶–۲۰", "۲۰–۲۴" };
        var data = new int[6];
        foreach (var row in rows.Where(r => r.when >= todayStartUtc && r.when < todayStartUtc.AddDays(1)))
        {
            var hour = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(row.when), zone).Hour;
            data[Math.Min(5, hour / 4)] += (int)Math.Round(row.amount);
        }

        return new NexSeries { Labels = labels, Data = data };
    }

    private static object RetentionMonths(List<NexRow> opps, PersianCalendar pc, TimeZoneInfo zone, int year, int month, int take)
    {
        var labels = new List<string>();
        var won = new List<int>();
        var open = new List<int>();
        var lost = new List<int>();
        for (var i = take - 1; i >= 0; i--)
        {
            var cursor = month - i;
            var cursorYear = year;
            while (cursor <= 0)
            {
                cursor += 12;
                cursorYear--;
            }

            var start = MonthStartUtc(pc, zone, cursorYear, cursor);
            var nextMonth = cursor == 12 ? 1 : cursor + 1;
            var nextYear = cursor == 12 ? cursorYear + 1 : cursorYear;
            var end = MonthStartUtc(pc, zone, nextYear, nextMonth);
            var slice = opps.Where(r => r.CreatedAtUtc >= start && r.CreatedAtUtc < end).ToList();
            labels.Add(PersianMonths[cursor]);
            won.Add(slice.Count(r => (Field(r.Json, "stage") ?? "").Equals("won", StringComparison.OrdinalIgnoreCase)));
            lost.Add(slice.Count(r => (Field(r.Json, "stage") ?? "").Equals("lost", StringComparison.OrdinalIgnoreCase)));
            open.Add(slice.Count - won[^1] - lost[^1]);
        }

        return new { labels, won, open, lost };
    }

    private static object Heatmap(List<NexRow> rows, TimeZoneInfo zone)
    {
        var bands = new[] { 8, 10, 12, 14, 16 };
        var grid = bands.Select(_ => new int[7]).ToArray();
        foreach (var row in rows)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(row.CreatedAtUtc), zone);
            var day = ((int)local.DayOfWeek + 1) % 7;
            var band = local.Hour < 9 ? 0 : local.Hour < 11 ? 1 : local.Hour < 13 ? 2 : local.Hour < 15 ? 3 : 4;
            grid[band][day]++;
        }

        return new
        {
            days = new[] { "شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه" },
            series = bands.Select((hour, i) => new { name = hour.ToFa(), data = grid[i] }).ToArray()
        };
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    internal static readonly Dictionary<string, string> NexTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nx-contacts"] = "مخاطبان کل",
        ["nx-leads"] = "تحلیل سرنخ",
        ["nx-tasks"] = "نمای وظایف",
        ["nx-deals"] = "معاملات فعال",
        ["nx-revenue"] = "درآمد",
        ["nx-sources"] = "منابع ورود",
        ["nx-retention"] = "نرخ برد معاملات",
        ["nx-earning"] = "درآمد کل",
        ["nx-orders"] = "وضعیت سفارش",
        ["nx-heatmap"] = "سفارش بر اساس زمان",
        ["nx-meetings"] = "جلسه‌های پیش‌رو",
        ["nx-dealview"] = "نمای معاملات",
        ["nx-funnel"] = "قیف فروش",
        ["nx-customers"] = "مشتریان جدید",
        ["nx-tasklist"] = "به‌روزرسانی وظایف"
    };

    internal static bool IsNexType(string? type) =>
        type is not null && NexTitles.ContainsKey(type);

    private static DashCellVm StudioCell(WidgetView widget)
    {
        var skin = WidgetSkins.Find(widget.StudioSkin);
        return new DashCellVm
        {
            Key = "w:" + widget.Widget.Id,
            Kind = "studio",
            Widget = widget,
            W = skin?.W ?? 4,
            H = skin?.H ?? 3,
            MinW = skin?.MinW ?? 3,
            MaxW = skin?.MaxW ?? 12,
            MinH = skin?.MinH ?? 3,
            MaxH = skin?.MaxH ?? 8
        };
    }

    private static DashCellVm NexCell(WidgetView widget)
    {
        var type = widget.Widget.Type;
        var wide = type is "nx-revenue" or "nx-customers";
        var tall = type is "nx-meetings" or "nx-tasklist" or "nx-earning" or "nx-heatmap";
        return new DashCellVm
        {
            Key = "w:" + widget.Widget.Id,
            Kind = "nex",
            Widget = widget,
            W = wide ? 8 : 4,
            H = tall ? 5 : wide ? 4 : 3,
            MinW = 3,
            MaxW = 12,
            MinH = 3,
            MaxH = 8
        };
    }
}
