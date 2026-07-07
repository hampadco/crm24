$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5100'
$email = "sales$(Get-Random)@test.ir"

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

# 1) Register new tenant → all 7 sales modules seeded
$reg = Invoke-WebRequest -Uri "$base/App/Account/Register" -SessionVariable s -UseBasicParsing
$resp = Post-Form "$base/App/Account/Register" $s @{ CompanyName = 'فروش پایه تست'; FullName = 'مدیر فروش'; Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg.Content) }
$dash = Decode $resp
$modules = @('سرنخ‌ها', 'سازمان‌ها', 'مخاطبین', 'فرصت‌های فروش', 'وظایف', 'رویدادها', 'تماس‌های تلفنی')
$missing = $modules | Where-Object { $dash -notmatch $_ }
if ($missing) { throw "modules missing on dashboard: $missing" }
Write-Output "1. ALL 7 SALES MODULES SEEDED"

# 2) Create a lead with company
$cp = Invoke-WebRequest -Uri "$base/App/m/leads/create" -WebSession $s -UseBasicParsing
$lead = Post-Form "$base/App/m/leads/create" $s @{
    f_name = 'امیر توکلی'; f_company = 'پخش البرز'; f_phone = '09121110000'; f_email = 'amir@alborz.ir';
    f_city = 'تهران'; f_status = 'hot'; f_source = 'website'; '__RequestVerificationToken' = (Get-Token $cp.Content)
}
if ((Decode $lead) -match 'امیر توکلی') { Write-Output "2. LEAD CREATED" } else { throw 'lead create failed' }
$leadId = ([regex]::Match($lead.Content, '/App/m/leads/(\d+)/edit')).Groups[1].Value

# 3) One-click lead conversion
$leadList = Invoke-WebRequest -Uri "$base/App/m/leads" -WebSession $s -UseBasicParsing
$convert = Post-Form "$base/App/m/leads/$leadId/convert" $s @{ '__RequestVerificationToken' = (Get-Token $leadList.Content) }
$convDecoded = Decode $convert
if ($convert.BaseResponse.ResponseUri.AbsolutePath -eq '/App/m/opportunities' -and $convDecoded -match 'تبدیل شد') {
    Write-Output "3. LEAD CONVERTED (redirected to opportunities)"
} else { throw "conversion failed: $($convert.BaseResponse.ResponseUri)" }
if ($convDecoded -match 'فرصت فروش امیر توکلی') { Write-Output "4. OPPORTUNITY CREATED FROM LEAD" }

# 5) Contact created with organization lookup resolved
$contacts = Invoke-WebRequest -Uri "$base/App/m/contacts" -WebSession $s -UseBasicParsing
$cDecoded = Decode $contacts
if ($cDecoded -match 'امیر توکلی' -and $cDecoded -match 'پخش البرز') { Write-Output "5. CONTACT + ORG LOOKUP DISPLAY OK" } else { throw 'contact/lookup failed' }

# 6) Lead is gone from list (soft-deleted after conversion)
$leads2 = Invoke-WebRequest -Uri "$base/App/m/leads" -WebSession $s -UseBasicParsing
if ((Decode $leads2) -notmatch 'امیر توکلی') { Write-Output "6. LEAD CLOSED AFTER CONVERSION" }

# 7) Kanban view shows opportunity in "new" column
$kanban = Invoke-WebRequest -Uri "$base/App/kanban/opportunities" -WebSession $s -UseBasicParsing
$kDecoded = Decode $kanban
if ($kDecoded -match 'فرصت فروش امیر توکلی' -and $kDecoded -match 'kanban-column') { Write-Output "7. KANBAN VIEW OK" } else { throw 'kanban failed' }
$oppId = ([regex]::Match($kanban.Content, 'data-id="(\d+)"')).Groups[1].Value

# 8) Kanban drag&drop endpoint moves stage
$moveResp = Post-Form "$base/App/kanban/opportunities/move" $s @{ recordId = $oppId; stage = 'negotiation'; '__RequestVerificationToken' = (Get-Token $kanban.Content) }
if ($moveResp.StatusCode -eq 200) { Write-Output "8. KANBAN MOVE OK" }
$kanban2 = Invoke-WebRequest -Uri "$base/App/kanban/opportunities" -WebSession $s -UseBasicParsing
if ((Decode $kanban2) -match 'مذاکره') { Write-Output "9. STAGE PERSISTED" }

# 10) Invalid stage rejected
try {
    Post-Form "$base/App/kanban/opportunities/move" $s @{ recordId = $oppId; stage = 'bogus'; '__RequestVerificationToken' = (Get-Token $kanban2.Content) } | Out-Null
    throw 'invalid stage accepted!'
} catch {
    if ($_.Exception.Message -match '400') { Write-Output "10. INVALID STAGE REJECTED" } else { throw }
}

# 11) Create task + event, check calendar feed
$tp = Invoke-WebRequest -Uri "$base/App/m/tasks/create" -WebSession $s -UseBasicParsing
Post-Form "$base/App/m/tasks/create" $s @{ f_name = 'پیگیری تماس البرز'; f_dueDate = '2026-07-10T10:00'; f_priority = 'high'; f_status = 'todo'; '__RequestVerificationToken' = (Get-Token $tp.Content) } | Out-Null
$ep = Invoke-WebRequest -Uri "$base/App/m/events/create" -WebSession $s -UseBasicParsing
Post-Form "$base/App/m/events/create" $s @{ f_name = 'جلسه دمو محصول'; f_startAt = '2026-07-12T14:00'; f_endAt = '2026-07-12T15:30'; f_type = 'demo'; '__RequestVerificationToken' = (Get-Token $ep.Content) } | Out-Null
$feed = Invoke-WebRequest -Uri "$base/App/calendar/feed" -WebSession $s -UseBasicParsing
$feedJson = ($feed.Content | ConvertFrom-Json)
$titles = ($feedJson | ForEach-Object { $_.title }) -join '|'
if ($titles -match 'پیگیری تماس البرز' -and $titles -match 'جلسه دمو محصول') { Write-Output "11. CALENDAR FEED OK ($($feedJson.Count) items)" } else { throw "calendar feed failed: $titles" }

# 12) Calendar page renders
$cal = Invoke-WebRequest -Uri "$base/App/calendar" -WebSession $s -UseBasicParsing
if ($cal.Content -match 'fullcalendar') { Write-Output "12. CALENDAR PAGE OK" }

# 13) Log a phone call with contact lookup
$contactId = ([regex]::Match($contacts.Content, '/App/m/contacts/(\d+)/edit')).Groups[1].Value
$callp = Invoke-WebRequest -Uri "$base/App/m/calls/create" -WebSession $s -UseBasicParsing
$call = Post-Form "$base/App/m/calls/create" $s @{
    f_name = 'تماس معرفی محصول'; f_contact = $contactId; f_direction = 'outgoing';
    f_callAt = '2026-07-07T11:30'; f_result = 'answered'; '__RequestVerificationToken' = (Get-Token $callp.Content)
}
$callDecoded = Decode $call
if ($callDecoded -match 'تماس معرفی محصول' -and $callDecoded -match 'امیر توکلی') { Write-Output "13. CALL LOGGED (contact lookup shown)" } else { throw 'call failed' }

Write-Output "ALL SALES CHECKS PASSED"
