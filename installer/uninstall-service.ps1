$ErrorActionPreference='Stop'
$service=Get-Service 'SendspinWindows' -ErrorAction SilentlyContinue
if($service){if($service.Status -ne 'Stopped'){Stop-Service $service.Name -Force;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))};& sc.exe delete 'SendspinWindows' | Out-Null;if($LASTEXITCODE -ne 0){throw 'Cannot remove Sendspin service.'}}
Get-Process 'Sendspin.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
Get-NetFirewallRule -Name 'SendspinWindows-mDNS' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
