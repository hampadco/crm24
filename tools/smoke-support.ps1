$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5100'
$email = "sup$(Get-Random)@test.ir"
$portalEmail = "customer$(Get-Random)@test.ir"

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
Post-Form "$base/App/Account/Register" $s @{ CompanyName = 'پشتیبانی تست'; FullName = 'مدیر پشتیبانی'; Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg.Content) } | Out-Null
Write-Output "1. TENANT REGISTERED"

# 2) SLA page seeds default policies
$sla = Invoke-WebRequest -Uri "$base/App/tickets/sla" -WebSession $s -UseBasicParsing
$slaDecoded = Decode $sla
if ($slaDecoded -match 'بحرانی' -and $slaDecoded -match 'عادی') { Write-Output "2. SLA POLICIES SEEDED" } else { throw 'sla seed failed' }

# 3) Contact record for the end-customer
$cp = Invoke-WebRequest -Uri "$base/App/m/contacts/create" -WebSession $s -UseBasicParsing
Post-Form "$base/App/m/contacts/create" $s @{
    f_name = 'علی مشتری'; f_mobile = '09120001122';
    '__RequestVerificationToken' = (Get-Token $cp.Content)
} | Out-Null
$clist = Invoke-WebRequest -Uri "$base/App/m/contacts" -WebSession $s -UseBasicParsing
$contactId = ([regex]::Match($clist.Content, '/App/m/contacts/(\d+)/edit')).Groups[1].Value
if (-not $contactId) { throw 'contact id not found' }
Write-Output "3. CONTACT CREATED (id=$contactId)"

# 4) Invoice for that contact (portal will see it)
$ip = Invoke-WebRequest -Uri "$base/App/finance/invoices/create" -WebSession $s -UseBasicParsing
$inv = Post-Form "$base/App/finance/save" $s @{
    Id = 0; Kind = 'Invoice'; CustomerName = 'علی مشتری'; ContactRecordId = $contactId; DiscountPercent = 0;
    'Lines[0].Title' = 'قرارداد پشتیبانی سالانه'; 'Lines[0].Quantity' = 1; 'Lines[0].UnitPrice' = 900000;
    'Lines[0].DiscountPercent' = 0; 'Lines[0].TaxPercent' = 0;
    '__RequestVerificationToken' = (Get-Token $ip.Content)
}
if ((Decode $inv) -match 'فاکتور شماره') { Write-Output "4. INVOICE CREATED FOR CONTACT" } else { throw 'invoice create failed' }

# 5) Portal user linked to the contact
$pu = Invoke-WebRequest -Uri "$base/App/portal-users" -WebSession $s -UseBasicParsing
$puSaved = Post-Form "$base/App/portal-users/create" $s @{
    email = $portalEmail; fullName = 'علی مشتری'; password = 'Portal@123'; contactRecordId = $contactId;
    '__RequestVerificationToken' = (Get-Token $pu.Content)
}
if ((Decode $puSaved) -match [regex]::Escape($portalEmail)) { Write-Output "5. PORTAL USER CREATED" } else { throw 'portal user create failed' }

# 6) Service contract
$scp = Invoke-WebRequest -Uri "$base/App/contracts/create" -WebSession $s -UseBasicParsing
$sc = Post-Form "$base/App/contracts/save" $s @{
    id = 0; name = 'قرارداد طلایی'; customerName = 'علی مشتری';
    startUtc = '2026-01-01'; endUtc = '2027-01-01'; maxTickets = 10; isActive = 'true';
    '__RequestVerificationToken' = (Get-Token $scp.Content)
}
if ((Decode $sc) -match 'قرارداد طلایی') { Write-Output "6. SERVICE CONTRACT CREATED" } else { throw 'contract create failed' }

# 7) Warranty
$wp = Invoke-WebRequest -Uri "$base/App/warranties/create" -WebSession $s -UseBasicParsing
$w = Post-Form "$base/App/warranties/create" $s @{
    serialNumber = 'SN-1001'; customerName = 'علی مشتری';
    startUtc = '2026-01-01'; endUtc = '2028-01-01'; notes = 'گارانتی دو ساله';
    '__RequestVerificationToken' = (Get-Token $wp.Content)
}
if ((Decode $w) -match 'SN-1001') { Write-Output "7. WARRANTY CREATED" } else { throw 'warranty create failed' }

# 8) KB article published to portal
$kp = Invoke-WebRequest -Uri "$base/App/kb/create" -WebSession $s -UseBasicParsing
$kb = Post-Form "$base/App/kb/save" $s @{
    id = 0; title = 'راهنمای نصب نرم‌افزار'; body = 'مراحل نصب: ابتدا فایل را دانلود کنید سپس اجرا کنید.';
    category = 'آموزش'; isPublishedToPortal = 'true';
    '__RequestVerificationToken' = (Get-Token $kp.Content)
}
if ((Decode $kb) -match 'راهنمای نصب نرم‌افزار') { Write-Output "8. KB ARTICLE CREATED (published)" } else { throw 'kb create failed' }

# 9) Leave request + admin approve
$lv = Invoke-WebRequest -Uri "$base/App/leaves" -WebSession $s -UseBasicParsing
Post-Form "$base/App/leaves/create" $s @{
    type = 'Leave'; fromUtc = '2026-08-01'; toUtc = '2026-08-03'; reason = 'سفر خانوادگی';
    '__RequestVerificationToken' = (Get-Token $lv.Content)
} | Out-Null
$lv2 = Invoke-WebRequest -Uri "$base/App/leaves" -WebSession $s -UseBasicParsing
$leaveId = ([regex]::Match($lv2.Content, '/App/leaves/(\d+)/review')).Groups[1].Value
if (-not $leaveId) { throw 'leave id not found' }
$reviewed = Post-Form "$base/App/leaves/$leaveId/review" $s @{ approve = 'true'; note = 'موافقت شد'; '__RequestVerificationToken' = (Get-Token $lv2.Content) }
if ((Decode $reviewed) -match 'تأیید شده') { Write-Output "9. LEAVE REQUESTED + APPROVED" } else { throw 'leave review failed' }

# 10) Portal login (separate session)
$plogin = Invoke-WebRequest -Uri "$base/Portal/Account/Login" -SessionVariable p -UseBasicParsing
$pdash = Post-Form "$base/Portal/Account/Login" $p @{ email = $portalEmail; password = 'Portal@123'; '__RequestVerificationToken' = (Get-Token $plogin.Content) }
$pdDecoded = Decode $pdash
if ($pdDecoded -match 'علی مشتری' -and $pdDecoded -match 'پورتال مشتری') { Write-Output "10. PORTAL LOGIN OK" } else { throw 'portal login failed' }

# 11) Portal user creates a ticket
$ptc = Invoke-WebRequest -Uri "$base/Portal/tickets/create" -WebSession $p -UseBasicParsing
$pticket = Post-Form "$base/Portal/tickets/create" $p @{
    subject = 'مشکل در ورود به سامانه'; body = 'هنگام ورود خطای ۵۰۰ می‌گیرم.'; priority = 'High';
    '__RequestVerificationToken' = (Get-Token $ptc.Content)
}
$ptDecoded = Decode $pticket
if ($ptDecoded -match 'مشکل در ورود به سامانه' -and $ptDecoded -match 'خطای ۵۰۰') { Write-Output "11. PORTAL TICKET CREATED" } else { throw 'portal ticket failed' }
$ticketId = ([regex]::Match($pticket.BaseResponse.ResponseUri.AbsolutePath, '/tickets/(\d+)')).Groups[1].Value

# 12) Staff sees the ticket and replies
$appTickets = Invoke-WebRequest -Uri "$base/App/tickets" -WebSession $s -UseBasicParsing
if ((Decode $appTickets) -notmatch 'مشکل در ورود به سامانه') { throw 'ticket not visible to staff' }
$appDetail = Invoke-WebRequest -Uri "$base/App/tickets/$ticketId" -WebSession $s -UseBasicParsing
$replied = Post-Form "$base/App/tickets/$ticketId/reply" $s @{ body = 'لطفاً کش مرورگر را پاک کنید و دوباره تلاش کنید.'; '__RequestVerificationToken' = (Get-Token $appDetail.Content) }
if ((Decode $replied) -match 'کش مرورگر') { Write-Output "12. STAFF REPLIED" } else { throw 'staff reply failed' }

# 13) Portal sees the staff reply
$pdetail = Invoke-WebRequest -Uri "$base/Portal/tickets/$ticketId" -WebSession $p -UseBasicParsing
if ((Decode $pdetail) -match 'کش مرورگر') { Write-Output "13. PORTAL SEES STAFF REPLY" } else { throw 'portal reply visibility failed' }

# 14) Portal replies back
$preplied = Post-Form "$base/Portal/tickets/$ticketId/reply" $p @{ body = 'انجام دادم، حل شد. ممنون.'; '__RequestVerificationToken' = (Get-Token $pdetail.Content) }
if ((Decode $preplied) -match 'حل شد. ممنون') { Write-Output "14. PORTAL REPLIED BACK" } else { throw 'portal reply failed' }

# 15) Staff closes the ticket -> portal sees closed state
$appDetail2 = Invoke-WebRequest -Uri "$base/App/tickets/$ticketId" -WebSession $s -UseBasicParsing
Post-Form "$base/App/tickets/$ticketId/status" $s @{ status = 'Closed'; '__RequestVerificationToken' = (Get-Token $appDetail2.Content) } | Out-Null
$pclosed = Invoke-WebRequest -Uri "$base/Portal/tickets/$ticketId" -WebSession $p -UseBasicParsing
if ((Decode $pclosed) -match 'این تیکت بسته شده است') { Write-Output "15. TICKET CLOSED (portal read-only)" } else { throw 'close failed' }

# 16) Portal invoices list shows the invoice
$pinv = Invoke-WebRequest -Uri "$base/Portal/invoices" -WebSession $p -UseBasicParsing
$piDecoded = Decode $pinv
if ($piDecoded -match '900,000') { Write-Output "16. PORTAL INVOICES OK (900,000)" } else { throw 'portal invoices failed' }

# 17) Portal KB shows the published article
$pkb = Invoke-WebRequest -Uri "$base/Portal/kb" -WebSession $p -UseBasicParsing
$pkbDecoded = Decode $pkb
if ($pkbDecoded -match 'راهنمای نصب نرم‌افزار') { Write-Output "17. PORTAL KB OK" } else { throw 'portal kb failed' }
$kbId = ([regex]::Match($pkb.Content, '/Portal/kb/(\d+)')).Groups[1].Value
$pkbd = Invoke-WebRequest -Uri "$base/Portal/kb/$kbId" -WebSession $p -UseBasicParsing
if ((Decode $pkbd) -match 'مراحل نصب') { Write-Output "18. PORTAL KB DETAILS OK" } else { throw 'portal kb details failed' }

# 19) Tenant isolation: portal session cannot access App area
$guard = Invoke-WebRequest -Uri "$base/App/tickets" -WebSession $p -UseBasicParsing -MaximumRedirection 5
if ($guard.BaseResponse.ResponseUri.AbsolutePath -match 'Login') { Write-Output "19. PORTAL SESSION BLOCKED FROM APP AREA" } else { throw 'auth isolation failed' }

Write-Output "ALL SUPPORT/PORTAL CHECKS PASSED"
