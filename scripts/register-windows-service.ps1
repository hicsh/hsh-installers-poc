# Register HSH Agent as a Windows Service
# Run this with administrator privileges after installing if the service wasn't auto-registered

$ServiceName = "HshAgent"
$DisplayName = "HSH Agent"
$ExePath = Join-Path $env:LOCALAPPDATA "HshAgent\current\HshAgent.exe"

if (-not (Test-Path $ExePath)) {
    Write-Error "HSH Agent not found at $ExePath"
    exit 1
}

Write-Host "Registering $DisplayName as a Windows Service..."

try {
    sc.exe create $ServiceName binPath= "`"$ExePath`"" start= auto DisplayName= "$DisplayName" | Out-Null
    sc.exe failure $ServiceName reset= 60 actions= restart/5000/restart/10000/restart/30000 | Out-Null
    sc.exe start $ServiceName | Out-Null

    Start-Sleep -Milliseconds 500
    $service = Get-Service $ServiceName -ErrorAction Stop

    Write-Host "✓ Service registered and started successfully"
    Write-Host "Service Status: $($service.Status)"
}
catch {
    Write-Error "Failed to register service: $_"
    exit 1
}
