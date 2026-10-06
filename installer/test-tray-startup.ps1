param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$exe=Join-Path $AppDirectory 'Snapcast.Tray.exe'
$resultDir=Join-Path $env:RUNNER_TEMP ('snapcast-smoke-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultDir | Out-Null
$log=Join-Path $resultDir 'startup.log'
$arguments='--smoke-test --smoke-log "'+$log+'"'
$taskName=$null
$p=$null
try {
 if((Get-Process -Id $PID).SessionId -eq 0) {
  # A Windows service runner has no interactive desktop. Test under the already
  # signed-in console user, without passwords or changing the runner registration.
  $user=(Get-CimInstance Win32_ComputerSystem).UserName
  if(!$user){
   Write-Host 'TRAY SMOKE SKIPPED: runner is in Session 0 and no console user is signed in. Published UI startup remains unverified.'
   return
  }
  $sid=(New-Object System.Security.Principal.NTAccount($user)).Translate([System.Security.Principal.SecurityIdentifier]).Value
  & icacls.exe $resultDir /grant ('*'+$sid+':(OI)(CI)M') | Out-Null
  if($LASTEXITCODE -ne 0){throw 'Cannot grant smoke result directory access.'}
  & icacls.exe $AppDirectory /grant ('*'+$sid+':(OI)(CI)RX') /T /Q | Out-Null
  if($LASTEXITCODE -ne 0){throw 'Cannot grant published app read access for startup test.'}
  $taskName='Snapcast-Tray-Smoke-'+[guid]::NewGuid().ToString('N')
  $action=New-ScheduledTaskAction -Execute $exe -Argument $arguments -WorkingDirectory $AppDirectory
  $principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
  try {
   Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal | Out-Null
  } catch {
   if($_.Exception.HResult -eq -2147024891 -or $_.Exception.Message -match 'Access is denied') {
    $taskName=$null
    Write-Warning 'TRAY SMOKE SKIPPED: runner cannot register an interactive test task. Published UI startup remains unverified.'
    return
   }
   throw
  }
  Start-ScheduledTask -TaskName $taskName
  $deadline=(Get-Date).AddSeconds(50)
  do {
   Start-Sleep -Milliseconds 500
   $info=Get-ScheduledTaskInfo -TaskName $taskName
   $state=(Get-ScheduledTask -TaskName $taskName).State
   if((Test-Path $log) -and $state -ne 'Running'){break}
   if($info.LastTaskResult -notin @(0,267009,267011) -and $state -ne 'Running'){throw ('Interactive tray launch failed: '+$info.LastTaskResult)}
  } while((Get-Date) -lt $deadline)
  if($state -eq 'Running'){throw 'Interactive tray startup timed out.'}
  if($info.LastTaskResult -ne 0){throw ('Interactive tray startup failed: '+$info.LastTaskResult)}
 } else {
  $p=Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
  if(!$p.WaitForExit(45000)){throw 'Published tray startup timed out.'}
  $p.Refresh()
  if($p.ExitCode -ne 0){throw "Published tray startup failed with exit code $($p.ExitCode)."}
 }
 if(!(Test-Path $log)){throw 'Tray startup did not create a diagnostics log.'}
 $lines=Get-Content $log
 $lines | Write-Host
 if(!($lines -match 'SMOKE PASS')){throw 'Tray startup did not complete GUI/icon checks.'}
 Write-Host 'Published tray startup check passed in an interactive desktop session.'
} finally {
 if(Test-Path $log){Get-Content $log -Tail 45 | Write-Host}
 if($taskName){Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue;Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue}
 if($p -and !$p.HasExited){$p.Kill()}
 Remove-Item $resultDir -Recurse -Force -ErrorAction SilentlyContinue
}
