param([Parameter(Mandatory=$true)][string]$InstallRoot)
$ErrorActionPreference='Stop'
$serviceName='SnapcastWindows'
$exe=Join-Path $InstallRoot 'Service\Snapcast.Service.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Service executable missing.'}
$build=[int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
if($build -lt 22000){throw 'Windows 11 or Windows Server 2025 Desktop Experience is required.'}
$data=Join-Path $env:ProgramData 'SnapcastWindows'
New-Item -ItemType Directory -Force -Path $data | Out-Null
# Service can update config; interactive users read it but cannot replace privileged binaries.
& icacls.exe $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M' '*S-1-5-11:(OI)(CI)RX' | Out-Null
if($LASTEXITCODE -ne 0){throw 'Configuration ACL setup failed.'}
foreach($name in @('AudioEndpointBuilder','Audiosrv')) {Set-Service $name -StartupType Automatic;Start-Service $name}
$existing=Get-Service $serviceName -ErrorAction SilentlyContinue
if($existing){
 if($existing.Status -ne 'Stopped'){Stop-Service $serviceName -Force;$existing.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20))}
 & sc.exe config $serviceName binPath= ('"'+$exe+'"') start= auto obj= 'NT AUTHORITY\LocalService' depend= 'Audiosrv/AudioEndpointBuilder' | Out-Null
} else {
 & sc.exe create $serviceName binPath= ('"'+$exe+'"') start= auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'Snapcast Windows Audio' depend= 'Audiosrv/AudioEndpointBuilder' | Out-Null
}
if($LASTEXITCODE -ne 0){throw 'Service registration failed.'}
& sc.exe description $serviceName 'Plays Snapcast audio independently of interactive logins.' | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= 'restart/5000/restart/10000/restart/30000' | Out-Null
Start-Service $serviceName

