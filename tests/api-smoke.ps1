param([string]$BaseUrl = 'http://localhost:5080')
$ErrorActionPreference = 'Stop'
function Assert($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Post([string]$path, $body, $headers = @{}) {
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/$path" -Headers $headers -ContentType 'application/json' -Body (ConvertTo-Json -InputObject $body -Depth 30 -Compress)
}
$id = [Guid]::NewGuid().ToString('N').Substring(0,12)
$a = Post 'register' @{user="smoke_a_$id"; password='Smoke-test-password-2026'}
$b = Post 'register' @{user="smoke_b_$id"; password='Smoke-test-password-2026'}
$ha = @{Authorization="Bearer $($a.token)"}
$hb = @{Authorization="Bearer $($b.token)"}
$s = Post 'web/new' @{difficulty=0; id=$null; revision=0} $ha
Assert ($s.values.Count -eq 81) 'Board size'
Assert ($null -eq $s.solution) 'Solution exposure'
$cell = 0
while ($s.givens[$cell] -ne 0) { $cell++ }
$s = Post 'web/action' @{id=$s.id; revision=$s.revision; action='enter'; cell=$cell; digit=1; note=$true} $ha
Assert (($s.notes[$cell] -band 2) -eq 2) 'Note persistence'
$s = Post 'web/action' @{id=$s.id; revision=$s.revision; action='pause'} $ha
Assert $s.paused 'Explicit pause'
$restored = Invoke-RestMethod -Uri "$BaseUrl/api/web/session" -Headers $ha
Assert ($restored.id -eq $s.id -and $restored.paused) 'Session restore'
$other = Invoke-RestMethod -Uri "$BaseUrl/api/web/session" -Headers $hb
Assert $other.empty 'Account session isolation'
$s = Post 'web/action' @{id=$s.id; revision=$s.revision; action='resume'} $ha
Start-Sleep -Seconds 13
$s = Invoke-RestMethod -Uri "$BaseUrl/api/web/session" -Headers $ha
Assert $s.paused 'Heartbeat expiry pause'
$new = Post 'web/new' @{difficulty=0; id=$s.id; revision=$s.revision} $ha
$history = @(Invoke-RestMethod -Uri "$BaseUrl/api/history" -Headers $ha)
Assert ($history.Count -eq 1 -and $history[0].outcome -eq 'abandoned') 'Abandoned history'
$otherHistory = @(Invoke-RestMethod -Uri "$BaseUrl/api/history" -Headers $hb)
Assert ($otherHistory.Count -eq 0) 'Account history isolation'
Write-Host 'PASS authentication, game, pause, reconnect, history, isolation' -ForegroundColor Green
