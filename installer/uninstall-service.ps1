$ErrorActionPreference='Stop'
$service=Get-Service 'SnapcastWindows' -ErrorAction SilentlyContinue
if($service){if($service.Status -ne 'Stopped'){Stop-Service $service.Name -Force;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20))};& sc.exe delete $service.Name | Out-Null;if($LASTEXITCODE -ne 0){throw 'Service removal failed.'}}
Get-Process 'Snapcast.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
# Preserve configuration for future reinstall.

