$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5100'
$email = "p8$(Get-Random)@test.ir"

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
Post-Form "$base/App/Account/Register" $s @{ CompanyName = 'پلن هشت'; FullName = 'مدیر پلن هشت'; Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg.Content) } | Out-Null
Write-Output "1. TENANT REGISTERED"

# 2) Won opportunity -> convert to project
$op = Invoke-WebRequest -Uri "$base/App/m/opportunities/create" -WebSession $s -UseBasicParsing
Post-Form "$base/App/m/opportunities/create" $s @{
    f_name = 'پروژه پرتال سازمانی'; f_amount = 50000000; f_stage = 'won';
    '__RequestVerificationToken' = (Get-Token $op.Content)
} | Out-Null
$pl = Invoke-WebRequest -Uri "$base/App/projects" -WebSession $s -UseBasicParsing
$plDecoded = Decode $pl
if ($plDecoded -notmatch 'پروژه پرتال سازمانی') { throw 'won opportunity not offered for conversion' }
$oppId = ([regex]::Match($pl.Content, '/App/projects/from-opportunity/(\d+)')).Groups[1].Value
$proj = Post-Form "$base/App/projects/from-opportunity/$oppId" $s @{ '__RequestVerificationToken' = (Get-Token $pl.Content) }
$projDecoded = Decode $proj
if ($projDecoded -match 'از فرصت برنده ساخته شد' -and $projDecoded -match '50,000,000') { Write-Output "2. OPPORTUNITY -> PROJECT CONVERTED" } else { throw 'project conversion failed' }
$projectId = ([regex]::Match($proj.BaseResponse.ResponseUri.AbsolutePath, '/projects/(\d+)')).Groups[1].Value

# 3) Phase + task + gantt + progress
$phased = Post-Form "$base/App/projects/$projectId/phases" $s @{ name = 'فاز طراحی'; '__RequestVerificationToken' = (Get-Token $proj.Content) }
$tasked = Post-Form "$base/App/projects/$projectId/tasks" $s @{
    name = 'طراحی رابط کاربری'; startUtc = '2026-07-10'; endUtc = '2026-07-20';
    '__RequestVerificationToken' = (Get-Token $phased.Content)
}
$tDecoded = Decode $tasked
if ($tDecoded -match 'فاز طراحی' -and $tDecoded -match 'طراحی رابط کاربری' -and $tDecoded -match 'نمای گانت') { Write-Output "3. PHASE + TASK + GANTT OK" } else { throw 'phase/task failed' }
$taskId = ([regex]::Match($tasked.Content, '/App/projects/tasks/(\d+)/progress')).Groups[1].Value
$progressed = Post-Form "$base/App/projects/tasks/$taskId/progress" $s @{ percent = 60; '__RequestVerificationToken' = (Get-Token $tasked.Content) }
if ((Decode $progressed) -match '60%') { Write-Output "4. TASK PROGRESS 60%" } else { throw 'progress failed' }

# 5) Product with inventory (stock 5) for purchase test
$pp = Invoke-WebRequest -Uri "$base/App/products/create" -WebSession $s -UseBasicParsing
$prod = Post-Form "$base/App/products/save" $s @{
    Id = 0; Name = 'ماژول سخت‌افزاری'; Sku = 'HW-01'; Unit = 'عدد'; SalePrice = 2000000; TaxPercent = 0;
    IsService = 'false'; TrackInventory = 'true'; StockQty = 5; ReorderPoint = 2; IsActive = 'true';
    '__RequestVerificationToken' = (Get-Token $pp.Content)
}
$prodId = ([regex]::Match($prod.Content, '/App/products/(\d+)/edit')).Groups[1].Value
Write-Output "5. PRODUCT CREATED (stock 5)"

# 6) Vendor
$vp = Invoke-WebRequest -Uri "$base/App/vendors/create" -WebSession $s -UseBasicParsing
$vendor = Post-Form "$base/App/vendors/save" $s @{
    id = 0; name = 'بازرگانی پارس'; phone = '02188887777'; isActive = 'true';
    '__RequestVerificationToken' = (Get-Token $vp.Content)
}
if ((Decode $vendor) -match 'بازرگانی پارس') { Write-Output "6. VENDOR CREATED" } else { throw 'vendor failed' }
$vlist = Invoke-WebRequest -Uri "$base/App/vendors" -WebSession $s -UseBasicParsing
$vendorId = ([regex]::Match($vlist.Content, '/App/vendors/(\d+)/edit')).Groups[1].Value

# 7) Purchase order: 10x product @ 1,500,000 = 15,000,000
$pop = Invoke-WebRequest -Uri "$base/App/purchase-orders/create" -WebSession $s -UseBasicParsing
$po = Post-Form "$base/App/purchase-orders/save" $s @{
    Id = 0; VendorId = $vendorId; Note = 'خرید فوری';
    'Lines[0].ProductId' = $prodId; 'Lines[0].Title' = 'ماژول سخت‌افزاری';
    'Lines[0].Quantity' = 10; 'Lines[0].UnitCost' = 1500000;
    '__RequestVerificationToken' = (Get-Token $pop.Content)
}
$poDecoded = Decode $po
if ($poDecoded -match 'سفارش خرید شماره' -and $poDecoded -match '15,000,000') { Write-Output "7. PURCHASE ORDER CREATED (15,000,000)" } else { throw 'po create failed' }
$poId = ([regex]::Match($po.BaseResponse.ResponseUri.AbsolutePath, '/purchase-orders/(\d+)')).Groups[1].Value

# 8) Mark ordered -> receive -> stock 5 + 10 = 15
$ordered = Post-Form "$base/App/purchase-orders/$poId/order" $s @{ '__RequestVerificationToken' = (Get-Token $po.Content) }
$received = Post-Form "$base/App/purchase-orders/$poId/receive" $s @{ '__RequestVerificationToken' = (Get-Token $ordered.Content) }
if ((Decode $received) -match 'انبار شارژ شد') { Write-Output "8. PO RECEIVED" } else { throw 'po receive failed' }
$plist = Invoke-WebRequest -Uri "$base/App/products" -WebSession $s -UseBasicParsing
if ((Decode $plist) -match '15') { Write-Output "9. STOCK CHARGED (5 -> 15)" } else { throw 'stock charge failed' }

# 10) Vendor payment
$paid = Post-Form "$base/App/purchase-orders/$poId/pay" $s @{ amount = 15000000; method = 'حواله'; '__RequestVerificationToken' = (Get-Token $received.Content) }
if ((Decode $paid) -match 'پرداخت ثبت شد') { Write-Output "10. VENDOR PAYMENT OK" } else { throw 'vendor payment failed' }

# 11) Campaign + attach won opportunity -> ROI
$cp = Invoke-WebRequest -Uri "$base/App/campaigns/create" -WebSession $s -UseBasicParsing
$camp = Post-Form "$base/App/campaigns/save" $s @{
    id = 0; name = 'کمپین تابستانه'; channel = 'اینستاگرام'; status = 'Active';
    startUtc = '2026-07-01'; endUtc = '2026-08-01'; budget = 10000000; actualCost = 10000000;
    '__RequestVerificationToken' = (Get-Token $cp.Content)
}
$campId = ([regex]::Match($camp.BaseResponse.ResponseUri.AbsolutePath, '/campaigns/(\d+)')).Groups[1].Value
$member = Post-Form "$base/App/campaigns/$campId/members" $s @{ moduleName = 'opportunities'; recordId = $oppId; '__RequestVerificationToken' = (Get-Token $camp.Content) }
$mDecoded = Decode $member
# ROI = (50M - 10M) / 10M = 400%
if ($mDecoded -match '400') { Write-Output "11. CAMPAIGN ROI OK (400%)" } else { throw 'campaign roi failed' }

# 12) Web form for leads (name + phone + hidden source=website) with captcha off for test simplicity? keep captcha on and solve it
$wfp = Invoke-WebRequest -Uri "$base/App/webforms/create" -WebSession $s -UseBasicParsing
$fieldsJson = '[{"Name":"name","Hidden":false,"DefaultValue":null},{"Name":"phone","Hidden":false,"DefaultValue":null},{"Name":"source","Hidden":true,"DefaultValue":"website"}]'
Post-Form "$base/App/webforms/save" $s @{
    id = 0; name = 'فرم تماس سایت'; moduleId = ([regex]::Match((Decode $wfp), '<option value="(\d+)"[^>]*>سرنخ‌ها</option>')).Groups[1].Value;
    fieldsJson = $fieldsJson; successMessage = 'با تشکر! ثبت شد.'; useCaptcha = 'true'; isActive = 'true';
    '__RequestVerificationToken' = (Get-Token $wfp.Content)
} | Out-Null
$wflist = Invoke-WebRequest -Uri "$base/App/webforms" -WebSession $s -UseBasicParsing
$wfDecoded = Decode $wflist
if ($wfDecoded -match 'فرم تماس سایت') { Write-Output "12. WEB FORM CREATED" } else { throw 'webform create failed' }
$formKey = ([regex]::Match($wfDecoded, '/f/([a-f0-9]{12})')).Groups[1].Value

# 13) Anonymous submit (new session, solve math captcha)
$anon = $null
$formPage = Invoke-WebRequest -Uri "$base/f/$formKey" -SessionVariable anon -UseBasicParsing
$fpDecoded = Decode $formPage
if ($fpDecoded -notmatch 'فرم تماس سایت') { throw 'public form not rendered' }
$mathMatch = [regex]::Match($fpDecoded, '(\d+)\s*\+\s*(\d+)\s*=')
$answer = [int]$mathMatch.Groups[1].Value + [int]$mathMatch.Groups[2].Value
$captchaToken = ([regex]::Match($formPage.Content, 'name="captchaToken" value="([^"]+)"')).Groups[1].Value
$submitted = Post-Form "$base/f/$formKey" $anon @{
    fld_name = 'مشتری وب‌فرمی'; fld_phone = '09351112233';
    captchaToken = $captchaToken; captchaAnswer = $answer
}
if ((Decode $submitted) -match 'با تشکر! ثبت شد') { Write-Output "13. PUBLIC FORM SUBMITTED (captcha solved)" } else { throw 'public submit failed' }

# 14) Lead created in CRM with hidden source
$leads = Invoke-WebRequest -Uri "$base/App/m/leads" -WebSession $s -UseBasicParsing
if ((Decode $leads) -match 'مشتری وب‌فرمی') { Write-Output "14. LEAD CREATED FROM WEB FORM" } else { throw 'webform lead missing' }

# 15) Survey with convert-to-lead + ticket survey flag
$svp = Invoke-WebRequest -Uri "$base/App/surveys/create" -WebSession $s -UseBasicParsing
Post-Form "$base/App/surveys/save" $s @{
    id = 0; title = 'رضایت از پشتیبانی'; isActive = 'true'; convertToLead = 'true'; isTicketSurvey = 'true';
    'questions[0].Text' = 'کیفیت پاسخگویی را چگونه ارزیابی می‌کنید؟'; 'questions[0].Type' = 'Scale';
    'questions[1].Text' = 'کدام کانال را ترجیح می‌دهید؟'; 'questions[1].Type' = 'SingleChoice'; 'questions[1].Options' = "تلفن`nایمیل`nچت";
    '__RequestVerificationToken' = (Get-Token $svp.Content)
} | Out-Null
$svlist = Invoke-WebRequest -Uri "$base/App/surveys" -WebSession $s -UseBasicParsing
$svDecoded = Decode $svlist
if ($svDecoded -match 'رضایت از پشتیبانی') { Write-Output "15. SURVEY CREATED" } else { throw 'survey create failed' }
$surveyKey = ([regex]::Match($svDecoded, '/s/([a-f0-9]{12})')).Groups[1].Value

# 16) Anonymous survey response -> converts respondent to lead
$anon2 = $null
$surveyPage = Invoke-WebRequest -Uri "$base/s/$surveyKey" -SessionVariable anon2 -UseBasicParsing
$spDecoded = Decode $surveyPage
if ($spDecoded -notmatch 'کیفیت پاسخگویی') { throw 'public survey not rendered' }
$qIds = [regex]::Matches($surveyPage.Content, 'name="q_(\d+)"') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
$respFields = @{ respondentName = 'پاسخ‌دهنده نظرسنجی'; respondentPhone = '09361119999' }
$respFields["q_$($qIds[0])"] = '5'
$respFields["q_$($qIds[1])"] = 'تلفن'
$responded = Post-Form "$base/s/$surveyKey" $anon2 $respFields
if ((Decode $responded) -match 'سپاسگزاریم') { Write-Output "16. SURVEY RESPONSE SUBMITTED" } else { throw 'survey submit failed' }

# 17) Response visible + respondent became a lead
$resp = Invoke-WebRequest -Uri "$base/App/surveys" -WebSession $s -UseBasicParsing
$respLink = ([regex]::Match($resp.Content, '/App/surveys/(\d+)/responses')).Groups[1].Value
$responses = Invoke-WebRequest -Uri "$base/App/surveys/$respLink/responses" -WebSession $s -UseBasicParsing
$rDecoded = Decode $responses
if ($rDecoded -match 'پاسخ‌دهنده نظرسنجی' -and $rDecoded -match 'تلفن') { Write-Output "17. SURVEY RESPONSES VISIBLE" } else { throw 'responses failed' }
$leads2 = Invoke-WebRequest -Uri "$base/App/m/leads" -WebSession $s -UseBasicParsing
if ((Decode $leads2) -match 'پاسخ‌دهنده نظرسنجی') { Write-Output "18. SURVEY RESPONDENT -> LEAD" } else { throw 'survey lead missing' }

# 19) Message template
$tp = Invoke-WebRequest -Uri "$base/App/templates" -WebSession $s -UseBasicParsing
$tpl = Post-Form "$base/App/templates/save" $s @{
    id = 0; title = 'خوش‌آمدگویی'; body = 'سلام! از تماس شما سپاسگزاریم.'; isPublic = 'true';
    '__RequestVerificationToken' = (Get-Token $tp.Content)
}
if ((Decode $tpl) -match 'خوش‌آمدگویی') { Write-Output "19. MESSAGE TEMPLATE CREATED" } else { throw 'template failed' }

Write-Output "ALL PLAN8 CHECKS PASSED"
