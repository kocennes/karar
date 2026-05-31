# B6-2: main branch protection kurulumu
# Çalıştırmadan önce: gh auth login
# Kullanım: .\scripts\setup-branch-protection.ps1

param(
    [string]$Repo = "kocennes/karar",
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"

Write-Host "Branch protection ayarlanıyor: $Repo / $Branch" -ForegroundColor Cyan

# Gerekli status check'ler (CI job name'leri ile birebir eşleşmeli)
$checks = @(
    "Forbidden File Guard",
    "Flutter Tests",
    ".NET Unit Tests",
    "Migration Safety Check",
    "Container Build & Security Scan",
    "CodeQL Analysis (C#)"
)

$body = @{
    required_status_checks = @{
        strict   = $true
        contexts = $checks
    }
    enforce_admins                  = $false
    required_pull_request_reviews   = @{
        required_approving_review_count = 1
        dismiss_stale_reviews           = $true
    }
    restrictions    = $null
    allow_force_pushes = $false
    allow_deletions    = $false
} | ConvertTo-Json -Depth 5

gh api "repos/$Repo/branches/$Branch/protection" `
    --method PUT `
    --header "Accept: application/vnd.github+json" `
    --input - <<< $body

Write-Host ""
Write-Host "Branch protection aktif:" -ForegroundColor Green
Write-Host "  - Zorunlu status check'ler:" -ForegroundColor Green
foreach ($c in $checks) { Write-Host "      • $c" -ForegroundColor Green }
Write-Host "  - PR review zorunlu (1 onay, stale dismiss aktif)" -ForegroundColor Green
Write-Host "  - Force push devre disi" -ForegroundColor Green
Write-Host "  - Branch silme devre disi" -ForegroundColor Green
