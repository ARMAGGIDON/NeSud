param([string]$BaseUrl = 'http://localhost:5080')
$ErrorActionPreference = 'Stop'
function Assert($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Post([string]$path, $body, $headers = @{}) {
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/$path" -Headers $headers -ContentType 'application/json' -Body (ConvertTo-Json -InputObject $body -Depth 30 -Compress)
}
$id = [Guid]::NewGuid().ToString('N').Substring(0,12)
$a = Post 'register' @{user="smoke_a_$id"; password='Smoke-test-password-2026'; confirmation='Smoke-test-password-2026'}
$b = Post 'register' @{user="smoke_b_$id"; password='Smoke-test-password-2026'; confirmation='Smoke-test-password-2026'}
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
$history = (Invoke-WebRequest -Uri "$BaseUrl/api/history" -Headers $ha).Content | ConvertFrom-Json -NoEnumerate
Assert ($history.Count -eq 1 -and $history[0].outcome -eq 'abandoned') 'Abandoned history'
$otherHistory = (Invoke-WebRequest -Uri "$BaseUrl/api/history" -Headers $hb).Content | ConvertFrom-Json -NoEnumerate
Assert ($otherHistory.Count -eq 0) 'Account history isolation'
$duplicate = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/register" -ContentType 'application/json' -Body (@{user="SMOKE_A_$id";password='Smoke-test-password-2026';confirmation='Smoke-test-password-2026'} | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$duplicate.StatusCode -eq 409) 'Case-insensitive unique login'
$bad = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/register" -ContentType 'application/json' -Body (@{user="smoke_c_$id";password='Smoke-test-password-2026';confirmation='different-password'} | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$bad.StatusCode -eq 400) 'Password confirmation'
$email = Post 'register' @{user="smoke_$id@example.com";password='Smoke-test-password-2026';confirmation='Smoke-test-password-2026'}
Assert ($email.user -eq "smoke_$id@example.com") 'Email login'
$guest = Post 'guest' @{}
$hg = @{Authorization="Bearer $($guest.token)"}
$demo = Post 'web/new' @{difficulty=0;id=$null;revision=0} $hg
Assert ($demo.values.Count -eq 81) 'Guest board'
$deny = Invoke-WebRequest -Uri "$BaseUrl/api/history" -Headers $hg -SkipHttpErrorCheck
Assert ([int]$deny.StatusCode -eq 401) 'Guest account isolation'
Post 'logout' @{} $hg | Out-Null
Write-Host 'PASS authentication, uniqueness, email, confirmation, guest, game, pause, reconnect, history, isolation' -ForegroundColor Green
