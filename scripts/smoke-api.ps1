param(
  [string]$BaseUrl = "http://localhost:5088"
)

$ErrorActionPreference = "Stop"

function Invoke-Json {
  param(
    [string]$Method,
    [string]$Path,
    [object]$Body = $null,
    [hashtable]$Headers = @{}
  )

  $params = @{
    Method = $Method
    Uri = "$BaseUrl$Path"
    Headers = $Headers
  }

  if ($null -ne $Body) {
    $params.ContentType = "application/json"
    $params.Body = ($Body | ConvertTo-Json -Depth 10)
  }

  Invoke-RestMethod @params
}

Write-Host "Checking health..."
Invoke-Json -Method GET -Path "/health" | Out-Null

Write-Host "Registering device..."
$session = Invoke-Json -Method POST -Path "/api/v1/devices/register" -Body @{
  fingerprint = "smoke-test-device"
  platform = "web"
  appVersion = "1.0.0"
}

$headers = @{ "X-Device-Token" = $session.deviceToken }

Write-Host "Creating post..."
$post = Invoke-Json -Method POST -Path "/api/v1/posts" -Headers $headers -Body @{
  title = "Smoke test için yeterince uzun başlık"
  content = "Bu smoke test içeriği en az elli karakter olacak şekilde yazılmıştır."
  categoryId = 1
}

Write-Host "Fetching feed..."
$feed = Invoke-Json -Method GET -Path "/api/v1/posts" -Headers $headers
if ($feed.posts.Count -lt 1) {
  throw "Feed returned no posts."
}

Write-Host "Voting..."
Invoke-Json -Method POST -Path "/api/v1/posts/$($post.id)/vote" -Headers $headers -Body @{
  voteType = "hakli"
} | Out-Null

Write-Host "Commenting..."
Invoke-Json -Method POST -Path "/api/v1/posts/$($post.id)/comments" -Headers $headers -Body @{
  content = "Smoke test yorumu başarıyla gönderildi."
} | Out-Null

Write-Host "Reporting..."
Invoke-Json -Method POST -Path "/api/v1/reports" -Headers $headers -Body @{
  targetType = "post"
  targetId = $post.id
  reason = "other"
  description = "Smoke test raporu"
} | Out-Null

Write-Host "Smoke API checks passed."
