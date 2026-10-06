param([switch]$CancelQueuedBuilds)
$ErrorActionPreference='Stop'
$headers=@{Authorization="Bearer $env:GITHUB_TOKEN";Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
$api="$env:GITHUB_API_URL/repos/$env:GITHUB_REPOSITORY"
if($CancelQueuedBuilds) {
 foreach($status in @('queued','in_progress','waiting','pending','requested')) {
  $runs=Invoke-RestMethod -Headers $headers -Uri "$($api)/actions/runs?status=$status&per_page=100"
  foreach($run in $runs.workflow_runs) {
   if([string]$run.id -eq $env:GITHUB_RUN_ID -or $run.path -ne '.github/workflows/build.yml'){continue}
   try {Invoke-RestMethod -Method Post -Headers $headers -Uri "$($api)/actions/runs/$($run.id)/cancel" | Out-Null;Write-Host "Cancelled previous build $($run.id)."}
   catch {if([int]$_.Exception.Response.StatusCode -ne 409){throw}}
  }
 }
}
$count=0
[long]$bytes=0
do {
 $page=Invoke-RestMethod -Headers $headers -Uri "$($api)/actions/artifacts?per_page=100"
 $items=@($page.artifacts)
 foreach($item in $items) {
  Invoke-RestMethod -Method Delete -Headers $headers -Uri "$($api)/actions/artifacts/$($item.id)" | Out-Null
  $count++;$bytes+=$item.size_in_bytes
  Write-Host "Deleted artifact $($item.id): $($item.name) ($($item.size_in_bytes) bytes)."
 }
} while($items.Count -gt 0)
$final=Invoke-RestMethod -Headers $headers -Uri "$($api)/actions/artifacts?per_page=1"
if($final.total_count -ne 0){throw "Artifact cleanup incomplete: $($final.total_count) remaining."}
Write-Host "CLEANUP PASS: $env:GITHUB_REPOSITORY; deleted $count artifacts ($bytes bytes); remaining 0."
"Artifact cleanup: deleted $count artifacts ($bytes bytes). Remaining: 0." | Out-File $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
