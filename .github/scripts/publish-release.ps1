param([Parameter(Mandatory=$true)][string]$Directory,[Parameter(Mandatory=$true)][string]$Title,[string]$Tag)
$ErrorActionPreference='Stop'
if(!$Tag){$Tag="build-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"}
$headers=@{Authorization="Bearer $env:GITHUB_TOKEN";Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
$api="$env:GITHUB_API_URL/repos/$env:GITHUB_REPOSITORY"
$files=@(Get-ChildItem -LiteralPath $Directory -Filter '*.exe' -File)
if(!$files.Count){throw 'No installer executable found to release.'}
$encodedTag=[Uri]::EscapeDataString($Tag)
try {$release=Invoke-RestMethod -Headers $headers -Uri "$($api)/releases/tags/$encodedTag"}
catch {
 if(!$_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404){throw}
 $body=@{
  tag_name=$Tag;target_commitish=$env:GITHUB_SHA;name="$Title $Tag"
  body="Automated build from commit $env:GITHUB_SHA. Build: $env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID. Desktop and physical audio acceptance are not established by compilation."
  draft=$false;prerelease=$true;make_latest='false'
 } | ConvertTo-Json
 $release=Invoke-RestMethod -Method Post -Headers $headers -Uri "$($api)/releases" -ContentType 'application/json' -Body $body
}
$uploadBase=($release.upload_url -split '\{')[0]
foreach($file in $files) {
 foreach($asset in @($release.assets | Where-Object {$_.name -eq $file.Name})) {
  Invoke-RestMethod -Method Delete -Headers $headers -Uri "$($api)/releases/assets/$($asset.id)" | Out-Null
 }
 $name=[Uri]::EscapeDataString($file.Name)
 $asset=Invoke-RestMethod -Method Post -Headers $headers -Uri "$($uploadBase)?name=$name" -ContentType 'application/octet-stream' -InFile $file.FullName
 if($asset.size -ne $file.Length -or $asset.state -ne 'uploaded'){throw 'Release upload verification failed.'}
 Write-Host "RELEASE ASSET: $($asset.browser_download_url) ($($asset.size) bytes)"
}
"Release: $($release.html_url)" | Out-File $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
Write-Host "RELEASE PASS: $($release.html_url)"
