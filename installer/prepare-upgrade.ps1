$ErrorActionPreference='Stop'
$service=Get-Service 'SnapcastWindows' -ErrorAction SilentlyContinue
if($service -and $service.Status -ne 'Stopped'){Stop-Service $service.Name -Force;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20))}
Get-Process 'Snapcast.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force

