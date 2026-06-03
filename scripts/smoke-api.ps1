<#
.SYNOPSIS
    Karar API smoke test -- staging and production readiness validation.
.PARAMETER BaseUrl
    API base URL.  Defaults to $env:SMOKE_BASE_URL, then http://localhost:5088.
.PARAMETER SkipWriteChecks
    Lightweight mode: only health + public read-only endpoints are verified.
    Use this for post-deploy production checks where write operations on real
    data are undesirable.
.NOTES
    Requires PowerShell 7+ (pwsh).  CI runs on ubuntu-latest with pwsh pre-installed.
    Local: install PowerShell 7 from https://github.com/PowerShell/PowerShell/releases
.EXAMPLE
    # Full staging smoke (includes write operations)
    pwsh -File smoke-api.ps1 -BaseUrl https://karar-staging.onrender.com

    # Lightweight production check (health + public endpoints only)
    pwsh -File smoke-api.ps1 -BaseUrl https://karar-oq5t.onrender.com -SkipWriteChecks

    # Against localhost dev server (reads SMOKE_BASE_URL or falls back to :5088)
    pwsh -File smoke-api.ps1
#>
param(
    [string]$BaseUrl,
    [switch]$SkipWriteChecks
)

$ErrorActionPreference = "Stop"

# Resolve BaseUrl: param > env > localhost default
if (-not $BaseUrl) {
    $BaseUrl = if ($env:SMOKE_BASE_URL) { $env:SMOKE_BASE_URL } else { "http://localhost:5088" }
}

# ── Helper ────────────────────────────────────────────────────────────────────

function Invoke-Json {
    param(
        [string]    $Method,
        [string]    $Path,
        [object]    $Body    = $null,
        [hashtable] $Headers = @{}
    )
    $params = @{ Method = $Method; Uri = "$BaseUrl$Path"; Headers = $Headers }
    if ($null -ne $Body) {
        $params.ContentType = "application/json"
        $params.Body        = ($Body | ConvertTo-Json -Depth 10)
    }
    Invoke-RestMethod @params
}

$mode = if ($SkipWriteChecks) { "lightweight" } else { "full" }
Write-Host "=== Smoke Test: $BaseUrl ($mode mode) ==="

# ── 1. Health checks ──────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[1/3] Health checks"

# /health/ready -- retry up to 3 times to handle cold-start on Render free tier
Write-Host "  Checking /health/ready..."
$ready = $false
for ($i = 1; $i -le 3; $i++) {
    try {
        $r = Invoke-WebRequest -Uri "$BaseUrl/health/ready" -Method GET -UseBasicParsing -TimeoutSec 20
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch {
        Write-Host "    Attempt $i/3 failed: $($_.Exception.Message)"
        if ($i -lt 3) { Start-Sleep -Seconds 10 }
    }
}
if (-not $ready) { throw "/health/ready did not return 200 after 3 attempts." }
Write-Host "    /health/ready OK"

# /health/version -- log deployed commit SHA (informational, non-fatal)
Write-Host "  Checking /health/version..."
try {
    $ver = Invoke-Json -Method GET -Path "/health/version"
    Write-Host "    commitSha  : $($ver.commitSha)"
    Write-Host "    environment: $($ver.environment)"
} catch {
    Write-Host "    WARN /health/version unavailable (non-fatal): $($_.Exception.Message)"
}

# /health/slo -- warn if burn-rate alerts are firing (non-fatal in smoke context)
Write-Host "  Checking /health/slo..."
try {
    $slo    = Invoke-Json -Method GET -Path "/health/slo"
    $firing = @($slo.burnRatePolicies | Where-Object { $_.severity -eq "page" -and $_.status -eq "alert" })
    if ($firing.Count -gt 0) {
        $names = ($firing | Select-Object -ExpandProperty name) -join ", "
        Write-Host "    WARN: SLO burn-rate alert(s) firing -- $names (non-fatal in smoke)"
    } else {
        Write-Host "    /health/slo OK (status=$($slo.status))"
    }
} catch {
    Write-Host "    WARN /health/slo unavailable (non-fatal): $($_.Exception.Message)"
}

# ── 2. Public API checks ──────────────────────────────────────────────────────
# These checks require no authentication and validate DB + cache connectivity.

Write-Host ""
Write-Host "[2/3] Public API checks"

# /api/v1/categories -- always seeded by migrations; fatal if empty
Write-Host "  Checking /api/v1/categories..."
$cats = Invoke-Json -Method GET -Path "/api/v1/categories"
if (-not $cats.categories -or $cats.categories.Count -lt 1) {
    throw "/api/v1/categories returned empty -- DB may not be seeded or is unreachable."
}
Write-Host "    /api/v1/categories OK ($($cats.categories.Count) categories)"

# /api/v1/posts -- public feed; may be empty on a fresh staging environment (non-fatal)
Write-Host "  Checking /api/v1/posts (public feed)..."
try {
    $publicFeed = Invoke-Json -Method GET -Path "/api/v1/posts"
    if ($publicFeed.posts.Count -lt 1) {
        Write-Host "    WARN: feed is empty (staging may be fresh -- non-fatal)"
    } else {
        Write-Host "    /api/v1/posts OK ($($publicFeed.posts.Count) posts)"
    }
} catch {
    Write-Host "    WARN /api/v1/posts: $($_.Exception.Message) (non-fatal)"
}

if ($SkipWriteChecks) {
    Write-Host ""
    Write-Host "=== Smoke test passed (lightweight mode) ==="
    exit 0
}

# ── 3. Write operations ───────────────────────────────────────────────────────
# All data is prefixed with [SMOKE] for easy identification and cleanup.

Write-Host ""
Write-Host "[3/3] Write operations"

$firstCategoryId = ($cats.categories | Select-Object -First 1).id
$ts = Get-Date -Format 'yyyyMMddHHmmss'

Write-Host "  Registering device..."
$session = Invoke-Json -Method POST -Path "/api/v1/devices/register" -Body @{
    fingerprint = "smoke-$ts"
    platform    = "web"
    appVersion  = "1.0.0"
}
$headers = @{ "X-Device-Token" = $session.deviceToken }
Write-Host "    device registered"

Write-Host "  Creating post [SMOKE]..."
$post = Invoke-Json -Method POST -Path "/api/v1/posts" -Headers $headers -Body @{
    title      = "[SMOKE] Test icin yeterince uzun baslik metni"
    content    = "[SMOKE] Bu smoke test icerigi en az elli karakter olacak sekilde yazilmistir."
    categoryId = $firstCategoryId
}
Write-Host "    post created: $($post.id)"

Write-Host "  Voting..."
Invoke-Json -Method POST -Path "/api/v1/posts/$($post.id)/vote" -Headers $headers -Body @{
    voteType = "hakli"
} | Out-Null
Write-Host "    vote OK"

Write-Host "  Commenting..."
Invoke-Json -Method POST -Path "/api/v1/posts/$($post.id)/comments" -Headers $headers -Body @{
    content = "[SMOKE] Smoke test yorumu basariyla gonderildi."
} | Out-Null
Write-Host "    comment OK"

Write-Host "  Reporting..."
Invoke-Json -Method POST -Path "/api/v1/reports" -Headers $headers -Body @{
    targetType  = "post"
    targetId    = $post.id
    reason      = "other"
    description = "[SMOKE] Smoke test raporu"
} | Out-Null
Write-Host "    report OK"

Write-Host ""
Write-Host "=== Smoke test passed ==="
