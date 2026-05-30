$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$reportDir = Join-Path $root "build/reports/store-compliance-smoke"
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null

$unitLog = Join-Path $reportDir "unit.log"
$flutterLog = Join-Path $reportDir "flutter.log"
$summary = Join-Path $reportDir "summary.md"

$startedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$unitExit = 0
$flutterExit = 0

dotnet test tests/Karar.UnitTests/Karar.UnitTests.csproj `
  --filter "FullyQualifiedName~StoreComplianceSmokeTests" `
  --verbosity quiet `
  *> $unitLog
$unitExit = $LASTEXITCODE

flutter test --no-pub test/release_store_compliance_smoke_test.dart *> $flutterLog
$flutterExit = $LASTEXITCODE

$status = if ($unitExit -eq 0 -and $flutterExit -eq 0) { "PASS" } else { "FAIL" }

@"
# UGC Store Compliance Smoke

- Status: $status
- Started: $startedAt
- Finished: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss K")
- Backend unit smoke exit code: $unitExit
- Flutter smoke exit code: $flutterExit
- Backend log: $unitLog
- Flutter log: $flutterLog

Release gate rule: FAIL blocks App Store / Play Store submission until fixed.
"@ | Set-Content -Path $summary -Encoding UTF8

Get-Content $summary

if ($status -ne "PASS") {
  exit 1
}
