param([Parameter(Mandatory=$true)][string]$InstallRoot)
$ErrorActionPreference='Stop'
$serviceName='SendspinWindows'
$exe=Join-Path $InstallRoot 'Service\Sendspin.Service.exe'
if(!(Test-Path $exe)){throw 'Sendspin service executable is missing.'}
$data=Join-Path $env:ProgramData 'SendspinWindows'
New-Item -ItemType Directory -Force -Path $data | Out-Null
& icacls.exe $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M' '*S-1-5-11:(OI)(CI)RX' | Out-Null
if($LASTEXITCODE -ne 0){throw 'Settings ACL configuration failed.'}
foreach($name in @('AudioEndpointBuilder','Audiosrv')){Set-Service $name -StartupType Automatic;Start-Service $name}
$existing=Get-Service $serviceName -ErrorAction SilentlyContinue
if($existing){
 if($existing.Status -ne 'Stopped'){Stop-Service $serviceName -Force;$existing.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
 & sc.exe config $serviceName binPath= ('"'+$exe+'"') start= auto obj= 'NT AUTHORITY\LocalService' depend= 'Audiosrv/AudioEndpointBuilder' | Out-Null
}else{
 & sc.exe create $serviceName binPath= ('"'+$exe+'"') start= auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'Sendspin Windows Speaker' depend= 'Audiosrv/AudioEndpointBuilder' | Out-Null
}
if($LASTEXITCODE -ne 0){throw 'Sendspin service registration failed.'}
& sc.exe description $serviceName 'Local Sendspin speaker with automatic discovery and Windows audio output.' | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= 'restart/5000/restart/10000/restart/30000' | Out-Null
# Scoped mDNS rule for this executable on trusted Windows network profiles.
$rule='SendspinWindows-mDNS'
Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -Name $rule -DisplayName 'Sendspin Windows discovery' -Direction Inbound -Program $exe -Protocol UDP -LocalPort 5353 -Profile Private,Domain -Action Allow | Out-Null
Start-Service $serviceName
