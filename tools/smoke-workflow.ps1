$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5100'
$email = "wf$(Get-Random)@test.ir"

function Get-Token($content) {
    ([regex]::Match($content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
}
function Post-Form($url, $session, [hashtable]$fields) {
    $pairs = foreach ($k in $fields.Keys) {
        "$([System.Uri]::EscapeDataString($k))=$([System.Uri]::EscapeDataString([string]$fields[$k]))"
    }
    Invoke-WebRequest -Uri $url -Method Post -WebSession $session -UseBasicParsing `
        -ContentType 'application/x-www-form-urlencoded' -Body ($pairs -join '&')
}
function Decode($response) { [System.Net.WebUtility]::HtmlDecode($response.Content) }

# 1) Register tenant
$reg = Invoke-WebRequest -Uri "$base/App/Account/Register" -SessionVariable s -UseBasicParsing
Post-Form "$base/App/Account/Register" $s @{ CompanyName = 'اتوماسیون تست'; FullName = 'مدیر اتوماسیون'; Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg.Content) } | Out-Null
Write-Output "1. TENANT REGISTERED"

# 2) Build workflow rule: new lead from website + city Tehran -> sms + task + notify + tag
$wfPage = Invoke-WebRequest -Uri "$base/App/workflows/create" -WebSession $s -UseBasicParsing
$wfDecoded = Decode $wfPage
$leadsModuleId = ([regex]::Match($wfDecoded, '<option value="(\d+)">سرنخ‌ها</option>')).Groups[1].Value
if (-not $leadsModuleId) { throw 'leads module id not found' }

$conditions = '{"logic":"and","items":[{"field":"city","op":"equals","value":"تهران"},{"field":"source","op":"equals","value":"website"}]}'
$saved = Post-Form "$base/App/workflows/save" $s @{
    Id = 0; Name = 'خوش‌آمد سرنخ تهران'; ModuleId = $leadsModuleId; Trigger = 'RecordCreated';
    ConditionsJson = $conditions;
    'Actions[0].Type' = 'SendSms';    'Actions[0].ConfigJson' = '{"to":"{phone}","text":"سلام {name} خوش آمدید"}';
    'Actions[1].Type' = 'CreateTask'; 'Actions[1].ConfigJson' = '{"name":"تماس با {name}","dueInDays":"1","priority":"high"}';
    'Actions[2].Type' = 'Notify';     'Actions[2].ConfigJson' = '{"title":"سرنخ جدید","body":"{name} از {city}"}';
    'Actions[3].Type' = 'ToggleTag';  'Actions[3].ConfigJson' = '{"tag":"vip","mode":"add"}';
    '__RequestVerificationToken' = (Get-Token $wfPage.Content)
}
$sDecoded = Decode $saved
if ($sDecoded -match 'خوش‌آمد سرنخ تهران' -and $sDecoded -match 'ارسال پیامک' -and $sDecoded -match 'ایجاد وظیفه') {
    Write-Output "2. WORKFLOW RULE CREATED (4 actions)"
} else { throw 'workflow create failed' }
$ruleId = ([regex]::Match($saved.Content, '/App/workflows/(\d+)/logs')).Groups[1].Value

# 3) Create matching lead (Tehran + website) -> triggers async workflow
$lp = Invoke-WebRequest -Uri "$base/App/m/leads/create" -WebSession $s -UseBasicParsing
Post-Form "$base/App/m/leads/create" $s @{
    f_name = 'رضا کریمی'; f_phone = '09121112222'; f_city = 'تهران'; f_status = 'hot'; f_source = 'website';
    '__RequestVerificationToken' = (Get-Token $lp.Content)
} | Out-Null
Write-Output "3. MATCHING LEAD CREATED (Tehran/website)"

# 4) Wait for Hangfire to run the job, then check logs
$logsOk = $false
for ($i = 0; $i -lt 12; $i++) {
    Start-Sleep -Seconds 5
    $logs = Invoke-WebRequest -Uri "$base/App/workflows/$ruleId/logs" -WebSession $s -UseBasicParsing
    $lDecoded = Decode $logs
    $successCount = ([regex]::Matches($lDecoded, 'موفق')).Count
    if ($successCount -ge 4) { $logsOk = $true; break }
}
if ($logsOk) { Write-Output "4. WORKFLOW EXECUTED (4 successful actions logged)" } else { throw "workflow logs incomplete: $successCount" }

# 5) Auto-created task exists
$tasks = Invoke-WebRequest -Uri "$base/App/m/tasks" -WebSession $s -UseBasicParsing
if ((Decode $tasks) -match 'تماس با رضا کریمی') { Write-Output "5. AUTO TASK CREATED" } else { throw 'auto task missing' }

# 6) Non-matching lead (Shiraz) -> no new executions
$lp2 = Invoke-WebRequest -Uri "$base/App/m/leads/create" -WebSession $s -UseBasicParsing
Post-Form "$base/App/m/leads/create" $s @{
    f_name = 'سارا احمدی'; f_phone = '09123334444'; f_city = 'شیراز'; f_status = 'warm'; f_source = 'referral';
    '__RequestVerificationToken' = (Get-Token $lp2.Content)
} | Out-Null
Start-Sleep -Seconds 20
$logs2 = Invoke-WebRequest -Uri "$base/App/workflows/$ruleId/logs" -WebSession $s -UseBasicParsing
$count2 = ([regex]::Matches((Decode $logs2), 'موفق')).Count
if ($count2 -eq 4) { Write-Output "6. NON-MATCHING LEAD SKIPPED (still 4 logs)" } else { throw "conditions leaked: $count2 logs" }

# 7) Dashboard widgets: counter + pie
$dash = Invoke-WebRequest -Uri "$base/App/Dashboard" -WebSession $s -UseBasicParsing
$dashToken = Get-Token $dash.Content
Post-Form "$base/App/dashboard/widgets/add" $s @{ type = 'counter'; moduleId = $leadsModuleId; title = 'شمار سرنخ‌ها'; '__RequestVerificationToken' = $dashToken } | Out-Null
$dash2 = Post-Form "$base/App/dashboard/widgets/add" $s @{ type = 'pie'; moduleId = $leadsModuleId; fieldName = 'status'; title = 'سرنخ بر اساس وضعیت'; '__RequestVerificationToken' = $dashToken }
$d2 = Decode $dash2
if ($d2 -match 'شمار سرنخ‌ها' -and $d2 -match 'سرنخ بر اساس وضعیت' -and $d2 -match 'widget-chart') {
    Write-Output "7. DASHBOARD WIDGETS ADDED (counter + pie)"
} else { throw 'widgets failed' }

# 8) Report: leads filtered to Tehran, grouped by status
$rp = Invoke-WebRequest -Uri "$base/App/reports/create" -WebSession $s -UseBasicParsing
$report = Post-Form "$base/App/reports/save" $s @{
    Id = 0; Name = 'گزارش سرنخ تهران'; ModuleId = $leadsModuleId;
    'Columns[0]' = 'name'; 'Columns[1]' = 'city';
    FiltersJson = '{"logic":"and","items":[{"field":"city","op":"equals","value":"تهران"}]}';
    GroupByField = 'status'; SumField = '';
    '__RequestVerificationToken' = (Get-Token $rp.Content)
}
$rDecoded = Decode $report
if ($rDecoded -match 'گزارش سرنخ تهران' -and $rDecoded -match 'رضا کریمی' -and $rDecoded -notmatch 'سارا احمدی' -and $rDecoded -match 'خلاصه گروه‌بندی') {
    Write-Output "8. REPORT FILTER + GROUPING OK"
} else { throw 'report failed' }
$reportId = ([regex]::Match($report.BaseResponse.ResponseUri.AbsolutePath, '/reports/(\d+)')).Groups[1].Value

# 9) Excel export
$xlsx = Invoke-WebRequest -Uri "$base/App/reports/$reportId/excel" -WebSession $s -UseBasicParsing
if ($xlsx.Headers['Content-Type'] -match 'spreadsheetml' -and $xlsx.RawContentLength -gt 1000) {
    Write-Output "9. EXCEL EXPORT OK ($($xlsx.RawContentLength) bytes)"
} else { throw 'excel export failed' }

Write-Output "ALL WORKFLOW/REPORT CHECKS PASSED"
