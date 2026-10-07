$ErrorActionPreference='Stop'
foreach($name in @('SendspinWindows','SnapcastWindows')){
 $service=Get-Service $name -ErrorAction SilentlyContinue
 if($service -and $service.Status -ne 'Stopped'){Stop-Service $name -Force;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
}
foreach($name in @('Sendspin.Tray','Snapcast.Tray')){Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force}
$old=Get-Service 'SnapcastWindows' -ErrorAction SilentlyContinue
if($old){& sc.exe delete 'SnapcastWindows' | Out-Null;if($LASTEXITCODE -ne 0){throw 'Cannot remove the previous Snapcast service.'}}
Remove-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'SnapcastWindowsTray' -ErrorAction SilentlyContinue
