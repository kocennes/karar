param(
    [ValidateSet("android", "ios")]
    [string]$Target = "android",

    [string]$ApiBaseUrl = "https://api.karar.app",
    [string]$Environment = "production",
    [string]$DebugInfoDir = "build/debug-info"
)

$ErrorActionPreference = "Stop"

$commonArgs = @(
    "--release",
    "--obfuscate",
    "--split-debug-info=$DebugInfoDir",
    "--dart-define=USE_REMOTE_API=true",
    "--dart-define=API_BASE_URL=$ApiBaseUrl",
    "--dart-define=REQUIRE_SECURE_API_TRANSPORT=true",
    "--dart-define=ENVIRONMENT=$Environment"
)

if ($Target -eq "android") {
    flutter build appbundle @commonArgs
    exit $LASTEXITCODE
}

flutter build ipa @commonArgs --export-options-plist=ExportOptions.plist
exit $LASTEXITCODE
