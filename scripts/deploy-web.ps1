#!/usr/bin/env pwsh
# Flutter web'i production modunda build edip Firebase'e deploy eder.
# Kullanim: .\scripts\deploy-web.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "Building Flutter web (production)..." -ForegroundColor Cyan
flutter build web `
  --release `
  --no-wasm-dry-run `
  --dart-define=USE_REMOTE_API=true `
  --dart-define=API_BASE_URL=https://karar-oq5t.onrender.com

Write-Host "Deploying to Firebase Hosting..." -ForegroundColor Cyan
firebase deploy --only hosting:app

Write-Host "Deploy tamamlandi." -ForegroundColor Green
