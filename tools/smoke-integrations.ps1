$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5100'
$email = "int$(Get-Random)@test.ir"
$portalEmail = "pay$(Get-Random)@test.ir"

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
function Api($method, $path, $key, $body) {
    $headers = @{ 'X-Api-Key' = $key }
    if ($null -ne $body) {
        Invoke-RestMethod -Uri "$base$path" -Method $method -Headers $headers `
            -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 5)))
    } else {
        Invoke-RestMethod -Uri "$base$path" -Method $method -Headers $headers
    }
}

# 1) Register tenant
$reg = Invoke-WebRequest -Uri "$base/App/Account/Register" -SessionVariable s -UseBasicParsing
Post-Form "$base/App/Account/Register" $s @{ CompanyName = 'یکپارچه تست'; FullName = 'مدیر یکپارچگی'; Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg.Content) } | Out-Null
Write-Output "1. TENANT REGISTERED"

# 2) Save integration settings and verify round-trip
$ig = Invoke-WebRequest -Uri "$base/App/integrations" -WebSession $s -UseBasicParsing
Post-Form "$base/App/integrations/save" $s @{
    SmtpHost = 'mail.example.ir'; SmtpPort = 587; SmtpUser = 'crm'; SmtpPassword = 'secret'; SmtpFrom = 'crm@example.ir';
    SmsApiUrl = 'https://sms.example.ir/send'; SmsApiKey = 'sms-key'; SmsFrom = '3000';
    BaleBotToken = 'bale-token'; AccountingWebhookUrl = 'https://acc.example.ir/hook'; VoipEnabled = 'true';
    '__RequestVerificationToken' = (Get-Token $ig.Content)
} | Out-Null
$ig2 = Invoke-WebRequest -Uri "$base/App/integrations" -WebSession $s -UseBasicParsing
if ((Decode $ig2) -match 'mail\.example\.ir' -and (Decode $ig2) -match 'bale-token') { Write-Output "2. INTEGRATION SETTINGS SAVED" } else { throw 'integration settings failed' }

# 3) Create API key (read/write) and grab the plaintext key
$created = Post-Form "$base/App/integrations/api-keys/create" $s @{ name = 'کلید تست'; canWrite = 'true'; '__RequestVerificationToken' = (Get-Token $ig2.Content) }
$apiKey = ([regex]::Match($created.Content, 'crm_[a-f0-9]{48}')).Value
if ($apiKey) { Write-Output "3. API KEY CREATED" } else { throw 'api key create failed' }

# 4) API: modules list
$mods = Api GET '/api/v1/modules' $apiKey $null
if (($mods | Where-Object { $_.name -eq 'leads' })) { Write-Output "4. API MODULES OK" } else { throw 'api modules failed' }

# 5) API: create + list + update lead
$lead = Api POST '/api/v1/leads' $apiKey @{ name = 'سرنخ از API'; phone = '02100001111' }
if ($lead.id -gt 0) { Write-Output "5. API LEAD CREATED (id=$($lead.id))" } else { throw 'api create failed' }
$list = Api GET '/api/v1/leads?search=API' $apiKey $null
if ($list.total -ge 1) { Write-Output "6. API LIST/SEARCH OK" } else { throw 'api list failed' }
$updated = Api PUT "/api/v1/leads/$($lead.id)" $apiKey @{ name = 'سرنخ از API'; phone = '02100002222' }
if ($updated.data.phone -eq '02100002222') { Write-Output "7. API UPDATE OK" } else { throw 'api update failed' }

# 8) API auth: no key -> 401, read-only key -> 403 on write
try { Invoke-RestMethod -Uri "$base/api/v1/leads" -Method GET; throw 'unauthenticated request allowed' }
catch { if ($_.Exception.Response.StatusCode.value__ -eq 401) { Write-Output "8. NO KEY -> 401" } else { throw } }
$ig3 = Invoke-WebRequest -Uri "$base/App/integrations" -WebSession $s -UseBasicParsing
$created2 = Post-Form "$base/App/integrations/api-keys/create" $s @{ name = 'فقط خواندن'; '__RequestVerificationToken' = (Get-Token $ig3.Content) }
$roKey = ([regex]::Match($created2.Content, 'crm_[a-f0-9]{48}')).Value
try { Api POST '/api/v1/leads' $roKey @{ name = 'نباید ساخته شود' }; throw 'read-only key wrote' }
catch { if ($_.Exception.Response.StatusCode.value__ -eq 403) { Write-Output "9. READ-ONLY KEY -> 403 ON WRITE" } else { throw } }

# 10) VoIP webhook: contact with mobile, incoming call matches it
$contact = Api POST '/api/v1/contacts' $apiKey @{ name = 'مشتری تلفنی'; mobile = '09120009988' }
$call = Api POST '/api/v1/voip/incoming' $apiKey @{ caller = '09120009988' }
if ($call.matchedContactId -eq $contact.id -and $call.callRecordId -gt 0) { Write-Output "10. VOIP CALL LOGGED + CONTACT MATCHED" } else { throw 'voip failed' }

# 11) Subscription renewal via payment gateway
$sub = Invoke-WebRequest -Uri "$base/App/subscription" -WebSession $s -UseBasicParsing
$planId = ([regex]::Match($sub.Content, 'name="planId" value="(\d+)"')).Groups[1].Value
if (-not $planId) { throw 'no plan on subscription page' }
$payPage = Post-Form "$base/App/subscription/renew" $s @{ planId = $planId; '__RequestVerificationToken' = (Get-Token $sub.Content) }
$payToken = ([regex]::Match($payPage.BaseResponse.ResponseUri.AbsolutePath, '/pay/([a-f0-9]{32})')).Groups[1].Value
if (-not $payToken) { throw 'gateway redirect failed' }
Write-Output "11. RENEWAL TRANSACTION -> GATEWAY PAGE"
$done = Post-Form "$base/pay/$payToken/confirm" $s @{ action = 'pay'; '__RequestVerificationToken' = (Get-Token $payPage.Content) }
$doneDecoded = Decode $done
if ($doneDecoded -match 'پرداخت با موفقیت انجام و اشتراک تمدید شد' -and $doneDecoded -match 'فعال') { Write-Output "12. SUBSCRIPTION RENEWED (tenant Active)" } else { throw 'renewal apply failed' }

# 13) Invoice + portal user for online invoice payment
$cl = Invoke-WebRequest -Uri "$base/App/m/contacts" -WebSession $s -UseBasicParsing
$contactRecordId = $contact.id
$ip = Invoke-WebRequest -Uri "$base/App/finance/invoices/create" -WebSession $s -UseBasicParsing
Post-Form "$base/App/finance/save" $s @{
    Id = 0; Kind = 'Invoice'; CustomerName = 'مشتری تلفنی'; ContactRecordId = $contactRecordId; DiscountPercent = 0;
    'Lines[0].Title' = 'سرویس سالانه'; 'Lines[0].Quantity' = 1; 'Lines[0].UnitPrice' = 1200000;
    'Lines[0].DiscountPercent' = 0; 'Lines[0].TaxPercent' = 0;
    '__RequestVerificationToken' = (Get-Token $ip.Content)
} | Out-Null
$pu = Invoke-WebRequest -Uri "$base/App/portal-users" -WebSession $s -UseBasicParsing
Post-Form "$base/App/portal-users/create" $s @{
    email = $portalEmail; fullName = 'مشتری تلفنی'; password = 'Portal@123'; contactRecordId = $contactRecordId;
    '__RequestVerificationToken' = (Get-Token $pu.Content)
} | Out-Null
Write-Output "13. INVOICE + PORTAL USER READY"

# 14) Portal login and pay invoice online
$plogin = Invoke-WebRequest -Uri "$base/Portal/Account/Login" -SessionVariable p -UseBasicParsing
Post-Form "$base/Portal/Account/Login" $p @{ email = $portalEmail; password = 'Portal@123'; '__RequestVerificationToken' = (Get-Token $plogin.Content) } | Out-Null
$pinv = Invoke-WebRequest -Uri "$base/Portal/invoices" -WebSession $p -UseBasicParsing
$invId = ([regex]::Match($pinv.Content, '/Portal/invoices/(\d+)')).Groups[1].Value
if (-not $invId) { throw 'portal invoice not found' }
$pdetail = Invoke-WebRequest -Uri "$base/Portal/invoices/$invId" -WebSession $p -UseBasicParsing
$gw = Post-Form "$base/Portal/invoices/$invId/pay-online" $p @{ '__RequestVerificationToken' = (Get-Token $pdetail.Content) }
$invToken = ([regex]::Match($gw.BaseResponse.ResponseUri.AbsolutePath, '/pay/([a-f0-9]{32})')).Groups[1].Value
if (-not $invToken) { throw 'invoice gateway redirect failed' }
$paid = Post-Form "$base/pay/$invToken/confirm" $p @{ action = 'pay'; '__RequestVerificationToken' = (Get-Token $gw.Content) }
$paidDecoded = Decode $paid
if ($paidDecoded -match 'پرداخت شما با موفقیت ثبت شد') { Write-Output "14. INVOICE PAID ONLINE VIA PORTAL" } else { throw 'invoice online payment failed' }

# 15) Staff side sees the invoice as paid
$appInv = Invoke-WebRequest -Uri "$base/App/finance/invoices" -WebSession $s -UseBasicParsing
if ((Decode $appInv) -match 'تسویه‌شده') { Write-Output "15. INVOICE STATUS = PAID IN APP" } else { throw 'invoice status not paid' }

# 16) OpenAPI document is exposed
$openapi = Invoke-RestMethod -Uri "$base/openapi/v1.json"
if ($openapi.openapi) { Write-Output "16. OPENAPI DOCUMENT OK" } else { throw 'openapi failed' }

# 17) CRITICAL: tenant isolation via API — second tenant's key must not see tenant 1 data
$email2 = "int2$(Get-Random)@test.ir"
$reg2 = Invoke-WebRequest -Uri "$base/App/Account/Register" -SessionVariable s2 -UseBasicParsing
Post-Form "$base/App/Account/Register" $s2 @{ CompanyName = 'شرکت دوم'; FullName = 'مدیر دوم'; Email = $email2; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg2.Content) } | Out-Null
$ig4 = Invoke-WebRequest -Uri "$base/App/integrations" -WebSession $s2 -UseBasicParsing
$created3 = Post-Form "$base/App/integrations/api-keys/create" $s2 @{ name = 'کلید دوم'; canWrite = 'true'; '__RequestVerificationToken' = (Get-Token $ig4.Content) }
$otherKey = ([regex]::Match($created3.Content, 'crm_[a-f0-9]{48}')).Value
$otherList = Api GET '/api/v1/leads' $otherKey $null
if ($otherList.total -eq 0) { Write-Output "17. TENANT ISOLATION OK (0 leads visible)" } else { throw "TENANT DATA LEAK: second tenant sees $($otherList.total) leads" }
try { Api GET "/api/v1/leads/$($lead.id)" $otherKey $null; throw 'TENANT DATA LEAK: cross-tenant record readable' }
catch { if ($_.Exception.Response.StatusCode.value__ -eq 404) { Write-Output "18. CROSS-TENANT RECORD -> 404" } else { throw } }

Write-Output "ALL INTEGRATION CHECKS PASSED"
