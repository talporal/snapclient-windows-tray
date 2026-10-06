param([string]$Version='0.1.0',[string]$OutputDirectory=(Join-Path $PSScriptRoot '..\artifacts'))
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stage=Join-Path $env:RUNNER_TEMP ('snapcast-package-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage,$OutputDirectory | Out-Null
foreach($component in @('Service','Tray')) {
 dotnet publish (Join-Path $root "src\Snapcast.$component\Snapcast.$component.csproj") -c Release -r win-x64 -p:Platform=x64 -p:Version=$Version --self-contained true -p:WindowsAppSDKSelfContained=true -o (Join-Path $stage $component)
 if($LASTEXITCODE -ne 0){throw "$component publish failed."}
}
& (Join-Path $PSScriptRoot 'test-tray-startup.ps1') -AppDirectory (Join-Path $stage 'Tray')
$zip=Join-Path $stage 'snapclient.zip'
Invoke-WebRequest -UseBasicParsing 'https://github.com/snapcast/snapcast/releases/download/v0.35.0/snapclient_win64.zip' -OutFile $zip
if((Get-FileHash $zip -Algorithm SHA256).Hash.ToLower() -ne '5a5fbabe0c1b8dea09542f0334af16c6eac9072c287fa6501bd7ff1f487bbf01'){throw 'Snapclient checksum mismatch.'}
$unpack=Join-Path $stage 'unpack'
Expand-Archive $zip $unpack
$client=Get-ChildItem $unpack -Recurse -Filter snapclient.exe | Select-Object -First 1
if(!$client){throw 'Upstream package contains no snapclient.exe.'}
$engine=Join-Path $stage 'Service\engine'
New-Item -ItemType Directory -Force -Path $engine | Out-Null
Copy-Item (Join-Path $client.Directory.FullName '*') $engine -Recurse
$redist=Get-ChildItem $unpack -Recurse -Filter '*redist*.exe' | Select-Object -First 1
if(!$redist){throw 'Upstream package contains no VC runtime installer.'}
Copy-Item $redist.FullName (Join-Path $stage 'vc_redist.exe')
# Runtime installer is needed only during setup, not in the audio engine folder.
Get-ChildItem $engine -Recurse -Filter '*redist*.exe' | Remove-Item -Force
if(Get-ChildItem $engine -Recurse -Filter '*redist*.exe'){throw 'Duplicate runtime installer remains in engine payload.'}
$signature=Get-AuthenticodeSignature (Join-Path $stage 'vc_redist.exe')
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){throw 'VC runtime signature invalid.'}
# Include exact corresponding upstream source and license for the bundled GPL engine.
$notices=Join-Path $stage 'Notices'
New-Item -ItemType Directory -Force -Path $notices | Out-Null
Invoke-WebRequest -UseBasicParsing 'https://github.com/snapcast/snapcast/archive/refs/tags/v0.35.0.zip' -OutFile (Join-Path $notices 'snapcast-v0.35.0-source.zip')
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $notices
Copy-Item (Join-Path $root 'LICENSE') $notices
$scripts=Join-Path $stage 'Scripts'
New-Item -ItemType Directory -Force -Path $scripts | Out-Null
Copy-Item (Join-Path $PSScriptRoot '*service.ps1') $scripts
Copy-Item (Join-Path $PSScriptRoot 'prepare-upgrade.ps1') $scripts
$inno=Join-Path $env:RUNNER_TEMP 'inno-setup-7.1.0'
$iscc=Join-Path $inno 'ISCC.exe'
if(!(Test-Path $iscc)) {
 $bootstrap=Join-Path $stage 'innosetup.exe'
 Invoke-WebRequest -UseBasicParsing 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $bootstrap
 $sig=Get-AuthenticodeSignature $bootstrap
 if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.'){throw 'Inno Setup signature invalid.'}
 $p=Start-Process $bootstrap -Wait -PassThru -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/PORTABLE=1',('/DIR="'+$inno+'"'))
 if($p.ExitCode -ne 0){throw 'Inno Setup initialization failed.'}
}
& $iscc "--define=SourceRoot=$stage" "--define=AppVersion=$Version" "--define=OutputDir=$((Resolve-Path $OutputDirectory).Path)" (Join-Path $PSScriptRoot 'SnapcastWindows.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
Write-Host ('Installer bytes: '+(Get-Item (Join-Path $OutputDirectory "SnapcastWindows-$Version-x64.exe")).Length)
Get-FileHash (Join-Path $OutputDirectory "SnapcastWindows-$Version-x64.exe") | Format-List

