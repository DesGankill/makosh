$ErrorActionPreference = "Stop"
$port = 9222
$profile = Join-Path $env:LOCALAPPDATA "Makosh\edge-deepseek"
New-Item -ItemType Directory -Force -Path $profile | Out-Null

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "$env:LocalAppData\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $edge) {
    Write-Host "Microsoft Edge not found."
    exit 1
}

Write-Host "Opening DeepSeek in a separate Edge profile for Makosh."
Write-Host "Log in in this window and keep it open."
Write-Host "Your normal Edge can stay open."
Start-Process -FilePath $edge -ArgumentList @(
    "--remote-debugging-port=$port",
    "--user-data-dir=$profile",
    "https://chat.deepseek.com"
)
