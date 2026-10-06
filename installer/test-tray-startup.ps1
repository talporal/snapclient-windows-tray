param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$exe=Join-Path $AppDirectory 'Snapcast.Tray.exe'
$log=Join-Path $env:LOCALAPPDATA 'SnapcastWindows\tray-startup.log'
$started=Get-Date
$p=Start-Process -FilePath $exe -ArgumentList '--smoke-test' -PassThru
try {
 if(!$p.WaitForExit(45000)){throw 'Published tray startup timed out.'}
 $p.Refresh()
 if(Test-Path $log){Get-Content $log -Tail 45 | Write-Host}
 if($p.ExitCode -ne 0){throw "Published tray startup failed with exit code $($p.ExitCode)."}
 if(!(Test-Path $log) -or (Get-Item $log).LastWriteTime -lt $started){throw 'Tray startup did not create a current diagnostics log.'}
 if(!((Get-Content $log -Tail 45) -match 'SMOKE PASS')){throw 'Tray startup did not complete GUI/icon checks.'}
} finally {
 if(!$p.HasExited){$p.Kill()}
}
