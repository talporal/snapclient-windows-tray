param([string]$Version='0.2.0',[string]$OutputDirectory=(Join-Path $PSScriptRoot '..\artifacts'))
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stage=Join-Path $env:RUNNER_TEMP ('sendspin-package-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage,$OutputDirectory | Out-Null
foreach($component in @('Service','Tray')){
 dotnet publish (Join-Path $root "src\Sendspin.$component\Sendspin.$component.csproj") -c Release -r win-x64 -p:Version=$Version --self-contained true -o (Join-Path $stage $component)
 if($LASTEXITCODE -ne 0){throw "$component publish failed."}
}
# This milestone tests protocol/pipeline code in CI, then GUI/service/audio on a signed-in desktop.
# Do not gate packaging on a Session 0 GUI or pre-login playback test.
$scripts=Join-Path $stage 'Scripts'
$notices=Join-Path $stage 'Notices'
New-Item -ItemType Directory -Force -Path $scripts,$notices | Out-Null
Copy-Item (Join-Path $PSScriptRoot '*service.ps1') $scripts
Copy-Item (Join-Path $PSScriptRoot 'prepare-upgrade.ps1') $scripts
Copy-Item (Join-Path $root 'LICENSE') $notices
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $notices
Copy-Item (Join-Path $root 'notices\*') $notices
$inno=Join-Path $env:RUNNER_TEMP 'inno-setup-7.1.0'
$iscc=Join-Path $inno 'ISCC.exe'
if(!(Test-Path $iscc)){
 $bootstrap=Join-Path $stage 'innosetup.exe'
 Invoke-WebRequest -UseBasicParsing 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $bootstrap
 $sig=Get-AuthenticodeSignature $bootstrap
 if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.'){throw 'Inno Setup signature invalid.'}
 $p=Start-Process $bootstrap -Wait -PassThru -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/PORTABLE=1',('/DIR="'+$inno+'"'))
 if($p.ExitCode -ne 0){throw 'Inno Setup initialization failed.'}
}
& $iscc "--define=SourceRoot=$stage" "--define=AppVersion=$Version" "--define=OutputDir=$((Resolve-Path $OutputDirectory).Path)" (Join-Path $PSScriptRoot 'SendspinWindows.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$file=Join-Path $OutputDirectory "SendspinWindows-$Version-x64.exe"
Write-Host ('Installer bytes: '+(Get-Item $file).Length)
Get-FileHash $file | Format-List
