# push-admin.ps1
# admin-panel/ içindeki Next.js kaynak kodunu karar_admin GitHub repo'suna push'lar.
#
# Kullanım:
#   .\scripts\push-admin.ps1                  # main branch'e push
#   .\scripts\push-admin.ps1 -Branch feat/xyz  # başka branch
#   .\scripts\push-admin.ps1 -DryRun           # sadece ne yapacağını göster
#
# Ön koşullar:
#   - git yüklü ve PATH'te
#   - GitHub SSH erişimi: ssh -T git@github.com

param(
    [string]$Branch = 'main',
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

# ── Sabitler ────────────────────────────────────────────────────────────────
$RepoRoot    = Split-Path $PSScriptRoot -Parent
$AdminSrc    = Join-Path $RepoRoot 'admin-panel'
$RemoteUrl   = 'https://github.com/kocennes/karar_admin.git'
$TempDir     = Join-Path $env:TEMP "karar_admin_push_$(Get-Random)"

# ── Kontroller ──────────────────────────────────────────────────────────────
if (-not (Test-Path $AdminSrc)) {
    Write-Error "admin-panel/ klasörü bulunamadı: $AdminSrc"
    exit 1
}

$requiredFiles = @('package.json', 'src\app\layout.tsx')
foreach ($f in $requiredFiles) {
    if (-not (Test-Path (Join-Path $AdminSrc $f))) {
        Write-Error "Beklenen dosya eksik: admin-panel\$f — kaynak kod var mı?"
        exit 1
    }
}

# ── DryRun modu ─────────────────────────────────────────────────────────────
if ($DryRun) {
    Write-Host "[DRY RUN] Şu dosyalar push'lanacak:" -ForegroundColor Yellow
    Get-ChildItem $AdminSrc -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\node_modules\\' -and $_.FullName -notmatch '\\.next\\' } |
        ForEach-Object { Write-Host "  $($_.FullName.Replace($AdminSrc, '').TrimStart('\'))" }
    exit 0
}

# ── Geçici worktree kur ─────────────────────────────────────────────────────
Write-Host ">> Geçici dizin oluşturuluyor: $TempDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $TempDir | Out-Null

try {
    Set-Location $TempDir

    Write-Host ">> karar_admin klonlanıyor..." -ForegroundColor Cyan
    $isEmptyRepo = $false
    $branchExists = git ls-remote --heads $RemoteUrl $Branch 2>&1
    if (-not $branchExists) {
        # Boş repo veya branch yok — init ile başlat
        Write-Host ">> Repo boş veya branch yok, init yapılıyor..." -ForegroundColor Yellow
        git init | Out-Null
        git remote add origin $RemoteUrl
        git checkout -b $Branch
        $isEmptyRepo = $true
    } else {
        git clone --depth 1 --branch $Branch $RemoteUrl . 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git clone başarısız oldu." }
    }

    # Eski kaynak dosyaları sil (node_modules ve .next hariç)
    Write-Host ">> Mevcut dosyalar temizleniyor..." -ForegroundColor Cyan
    Get-ChildItem -Force |
        Where-Object { $_.Name -ne '.git' } |
        Remove-Item -Recurse -Force

    # admin-panel/ içeriğini kopyala (node_modules ve .next hariç)
    Write-Host ">> Kaynak dosyalar kopyalanıyor..." -ForegroundColor Cyan
    Get-ChildItem $AdminSrc -Force |
        Where-Object { $_.Name -notin @('node_modules', '.next') } |
        Copy-Item -Destination $TempDir -Recurse -Force

    # Commit mesajı
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm'
    $commitMsg = "chore: sync from untitled repo ($timestamp)"

    git config user.email "kocennes@users.noreply.github.com"
    git config user.name "kocennes"

    git add -A
    $status = git status --porcelain
    if (-not $status) {
        Write-Host "OK Değişiklik yok — push atlandı." -ForegroundColor Green
        exit 0
    }

    Write-Host ">> Commit oluşturuluyor..." -ForegroundColor Cyan
    git commit -m $commitMsg

    Write-Host ">> Push'lanıyor >> $RemoteUrl ($Branch)..." -ForegroundColor Cyan
    git push origin $Branch

    Write-Host "OK Push tamamlandı: https://github.com/kocennes/karar_admin/tree/$($Branch)" -ForegroundColor Green
}
finally {
    Set-Location $RepoRoot
    if (Test-Path $TempDir) {
        Remove-Item $TempDir -Recurse -Force
    }
}
