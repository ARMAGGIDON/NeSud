param([string]$BaseUrl = 'http://localhost:5080')
$ErrorActionPreference = 'Stop'
function Assert($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Post([string]$path, $body, $headers = @{}) {
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/$path" -Headers $headers -ContentType 'application/json' -Body (ConvertTo-Json -InputObject $body -Depth 30 -Compress)
}
$id = [Guid]::NewGuid().ToString('N').Substring(0,12)
$a = Post 'register' @{user="smoke_a_$id";email="smoke_a_$id@example.com"; password='Smoke-test-password-2026'; confirmation='Smoke-test-password-2026'}
$b = Post 'register' @{user="smoke_b_$id";email="smoke_b_$id@example.com"; password='Smoke-test-password-2026'; confirmation='Smoke-test-password-2026'}
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
Assert ($otherHistory -is [array] -and $otherHistory.Count -eq 0) 'Account history isolation'
$duplicate = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/register" -ContentType 'application/json' -Body (@{user="SMOKE_A_$id";email="other_$id@example.com";password='Smoke-test-password-2026';confirmation='Smoke-test-password-2026'} | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$duplicate.StatusCode -eq 409) 'Case-insensitive unique login'
$bad = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/register" -ContentType 'application/json' -Body (@{user="smoke_c_$id";email="smoke_c_$id@example.com";password='Smoke-test-password-2026';confirmation='different-password'} | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$bad.StatusCode -eq 400) 'Password confirmation'
$email = Post 'register' @{user="smoke_email_$id";email="smoke_$id@example.com";password='Smoke-test-password-2026';confirmation='Smoke-test-password-2026'}
Assert ($email.username -eq "smoke_email_$id") 'Separate email and username'
$guest = Post 'guest' @{}
$hg = @{Authorization="Bearer $($guest.token)"}
$demo = Post 'web/new' @{difficulty=0;id=$null;revision=0} $hg
Assert ($demo.values.Count -eq 81) 'Guest board'
Assert ($demo.lives -eq 3 -and $demo.hintCredits -eq 1 -and $demo.candidateCredits -eq 1) 'Initial resources'
Assert ($demo.remaining.Count -eq 9 -and $demo.wrong.Count -eq 81) 'Board metadata'
$noLife = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/web/action" -Headers $hg -ContentType 'application/json' -Body (@{id=$demo.id;revision=$demo.revision;action='reward-start';reward='life'} | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$noLife.StatusCode -eq 400) 'No proactive life restoration'
$blockedAd = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/web/action" -Headers $hg -ContentType 'application/json' -Body (@{id=$demo.id;revision=$demo.revision;action='reward-start';reward='hint'} | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$blockedAd.StatusCode -eq 400) 'No ad while hint available'
$demo = Post 'web/action' @{id=$demo.id;revision=$demo.revision;action='hint'} $hg
Assert ($demo.hintCredits -eq 0) 'Hint depleted'
$demo = Post 'web/action' @{id=$demo.id;revision=$demo.revision;action='reward-start';reward='hint'} $hg
$claim = @{id=$demo.id;revision=$demo.revision;action='reward-claim';reward='hint'}
$early = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/web/action" -Headers $hg -ContentType 'application/json' -Body ($claim | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$early.StatusCode -eq 400) 'Early ad claim rejected'
Start-Sleep -Seconds 9
$demo = Post 'web/action' $claim $hg
Assert ($demo.hintCredits -eq 1 -and $demo.adsWatched -eq 1) 'Reward granted'
$claim.revision = $demo.revision
$again = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/web/action" -Headers $hg -ContentType 'application/json' -Body ($claim | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$again.StatusCode -eq 400) 'Reward cannot be replayed'
$scoreBefore = $demo.currentScore
$delta = $demo.candidateDelta
$demo = Post 'web/action' @{id=$demo.id;revision=$demo.revision;action='candidates'} $hg
Assert ($demo.candidateCredits -eq 0 -and $demo.candidateUses -eq 1 -and $demo.candidateChanges -eq $delta -and $demo.effectiveCandidatePenalty -eq (2*$delta) -and $demo.currentScore -le [Math]::Max(0,($scoreBefore-2*$delta))) 'Candidate scoring'
$wrongCount = 0
for ($j=0; $j -lt 81 -and $wrongCount -lt 3; $j++) {
    if ($demo.givens[$j] -ne 0) { continue }
    $r = [int][Math]::Floor($j / 9)
    $conflictDigit = 0
    for ($k=$r*9; $k -lt ($r+1)*9; $k++) { if ($demo.givens[$k] -gt 0) { $conflictDigit=$demo.givens[$k]; break } }
    if ($conflictDigit -eq 0) { continue }
    $demo = Post 'web/action' @{id=$demo.id;revision=$demo.revision;action='enter';cell=$j;digit=$conflictDigit;note=$false} $hg
    Assert ($demo.wrong[$j]) 'Wrong digit metadata'
    $wrongCount++
}
Assert ($demo.outOfLives -and $demo.reviveSeconds -gt 0 -and !$demo.finalDefeat) 'Defeat grace period'
$demo = Post 'web/action' @{id=$demo.id;revision=$demo.revision;action='reward-start';reward='life'} $hg
Assert ($demo.outOfLives -and $demo.reviveSeconds -eq 0 -and !$demo.finalDefeat) 'Ad start consumes chance'
$demo = Post 'web/action' @{id=$demo.id;revision=$demo.revision;action='reward-cancel'} $hg
Assert ($demo.finalDefeat -and $demo.lives -eq 0) 'Ad cancel finalizes defeat'
$againLife = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/web/action" -Headers $hg -ContentType 'application/json' -Body (@{id=$demo.id;revision=$demo.revision;action='reward-start';reward='life'} | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$againLife.StatusCode -eq 400) 'No second chance'
$deny = Invoke-WebRequest -Uri "$BaseUrl/api/history" -Headers $hg -SkipHttpErrorCheck
Assert ([int]$deny.StatusCode -eq 401) 'Guest account isolation'
$guest2 = Post 'guest' @{}
$hg2 = @{Authorization="Bearer $($guest2.token)"}
$demo2 = Invoke-RestMethod -Uri "$BaseUrl/api/web/session" -Headers $hg2
Assert $demo2.empty 'Independent guest sessions'
Post 'logout' @{} $hg2 | Out-Null
Post 'logout' @{} $hg | Out-Null
$daily = Invoke-RestMethod -Uri "$BaseUrl/api/daily" -Headers $ha
Assert ($daily.packs.Count -eq 0) 'No login pack'
$changes = Invoke-RestMethod -Uri "$BaseUrl/api/history/changes?after=0" -Headers $ha
Assert ($changes.records.Count -eq 1) 'Incremental history'
$empty = Invoke-RestMethod -Uri "$BaseUrl/api/history/changes?after=$($changes.cursor)" -Headers $ha
Assert ($empty.records.Count -eq 0) 'No duplicate changes'
$weekly = Invoke-RestMethod -Uri "$BaseUrl/api/weekly"
Assert ($weekly.week -match '^\d{4}-\d{2}-\d{2}$') 'Weekly API'
$stats = Invoke-RestMethod -Uri "$BaseUrl/api/accumulated?metric=perfect"
$calendar = Invoke-RestMethod -Uri "$BaseUrl/api/challenge/calendar" -Headers $ha
$day = $calendar.today
$guestChallenge = Post 'guest' @{}
$guestChallengeHeaders = @{Authorization="Bearer $($guestChallenge.token)"}
$guestCalendar = Invoke-WebRequest -Uri "$BaseUrl/api/challenge/calendar" -Headers $guestChallengeHeaders -SkipHttpErrorCheck
Assert ([int]$guestCalendar.StatusCode -eq 401) 'Guest calendar denied'
Post 'logout' @{} $guestChallengeHeaders | Out-Null
$ca = Post 'web/new' @{difficulty=1;id=$null;revision=0;challengeDay=$day} $ha
$cb = Post 'web/new' @{difficulty=1;id=$null;revision=0;challengeDay=$day} $hb
Assert (($ca.givens -join '') -eq ($cb.givens -join '') -and $ca.id -ne $cb.id) 'Shared challenge puzzle, isolated sessions'
Assert ($null -eq $ca.solution -and $ca.challengeDay -eq $day) 'Challenge view redaction'
Assert ($ca.difficulty -in 1,2,3 -and $ca.difficulty -eq $cb.difficulty) 'Shared non-easy challenge difficulty'
$regular = Invoke-RestMethod -Uri "$BaseUrl/api/web/session?pause=true" -Headers $ha
Assert ($regular.id -eq $new.id) 'Regular session preserved'
$future = Invoke-WebRequest -Uri "$BaseUrl/api/web/session?challengeDay=9999-12-31" -Headers $ha -SkipHttpErrorCheck
Assert ([int]$future.StatusCode -eq 400) 'Future challenge denied'
$profile = Invoke-RestMethod -Uri "$BaseUrl/api/profile?user=$($a.username)"
Assert ($profile.user -eq $a.username -and $profile.games -eq 1 -and $null -eq $profile.history[0].game) 'Public profile metadata only'
$hiddenEmail = Invoke-WebRequest -Uri "$BaseUrl/api/profile?user=$([Uri]::EscapeDataString("smoke_$id@example.com"))" -SkipHttpErrorCheck
Assert ([int]$hiddenEmail.StatusCode -eq 404) 'Email is not a public profile identifier'
# Test unauthorised social and profile operations without requiring SMTP.
$deniedSearch = Invoke-WebRequest -Uri "$BaseUrl/api/players?q=$($a.username)" -Headers $ha -SkipHttpErrorCheck
Assert ([int]$deniedSearch.StatusCode -eq 403) 'Unverified player search denied'
$deniedFriends = Invoke-WebRequest -Uri "$BaseUrl/api/friends" -Headers $ha -SkipHttpErrorCheck
Assert ([int]$deniedFriends.StatusCode -eq 403) 'Unverified friend list denied'
$deniedSend = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/friends" -Headers $ha -ContentType 'application/json' -Body (@{user=$b.username;action='send'} | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$deniedSend.StatusCode -eq 403) 'Unverified friend request denied'
$deniedRename = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/account/settings" -Headers $ha -ContentType 'application/json' -Body (@{username="rename_$id";email="smoke_a_$id@example.com"} | ConvertTo-Json) -SkipHttpErrorCheck
Assert ([int]$deniedRename.StatusCode -eq 403) 'Unverified rename denied'
$deniedAvatar = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/account/avatar" -Headers $ha -ContentType 'application/json' -Body '{"png":null}' -SkipHttpErrorCheck
Assert ([int]$deniedAvatar.StatusCode -eq 403) 'Unverified avatar removal denied'
$settings = Invoke-RestMethod -Uri "$BaseUrl/api/account/settings" -Headers $ha
Assert (-not $settings.verified -and $null -eq $settings.invite) 'Unverified invite unavailable'
$filtered = Invoke-RestMethod -Uri "$BaseUrl/api/profile?user=$($a.username)&difficulty=Easy"
Assert ($filtered.byDifficulty.Count -eq 4 -and $filtered.history[0].difficulty -eq 0) 'Profile difficulty breakdown'
Write-Host 'PASS authentication, uniqueness, email, confirmation, guest, game, pause, reconnect, history, isolation' -ForegroundColor Green
