$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:RUNNER_TEMP 'ChoiceFlameInstalledSmoke'
$installer = Join-Path $PWD 'installer/output/Choice-Flame-Communications-Network-Setup.exe'
$server = Join-Path $installRoot 'Server/OfficeNetwork.Server.exe'
$baseUrl = 'http://127.0.0.1:5077'
$archive = Join-Path $env:RUNNER_TEMP 'choiceflame-smoke-backup.zip'
$password = 'Smoke-' + [guid]::NewGuid().ToString('N')
$staffPassword = 'Staff-' + [guid]::NewGuid().ToString('N')

function Assert-Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Wait-Server {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri "$baseUrl/api/status" -TimeoutSec 2 -SkipHttpErrorCheck
            if ([int]$response.StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    throw 'Installed server did not become healthy.'
}
function Stop-InstalledServer {
    Get-Process -Name 'OfficeNetwork.Server' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $server } | ForEach-Object { $_.Kill(); $_.WaitForExit(10000) }
}
function Json-Request([string]$method, [string]$path, $body, $headers = @{}) {
    return Invoke-RestMethod -Method $method -Uri "$baseUrl$path" -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 8)
}

try {
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=servermode', "/DIR=`"$installRoot`"") -PassThru
    Assert-Check ($process.WaitForExit(60000)) 'Silent installer did not finish within one minute.'
    Assert-Check ($process.ExitCode -eq 0) 'Silent server installation failed.'
    Assert-Check (Test-Path (Join-Path $installRoot 'Server/BACKUP-RESTORE.md')) 'Restore guide was not installed.'
    Wait-Server

    $director = Json-Request 'POST' '/api/users' @{ userName = 'smoke-director'; displayName = 'Smoke Director'; role = 1; password = $password }
    $login = Json-Request 'POST' '/api/login' @{ userName = 'smoke-director'; password = $password }
    $directorHeaders = @{ Authorization = "Bearer $($login.token)" }
    $staff = Json-Request 'POST' '/api/users' @{ userName = 'smoke-staff'; displayName = 'Smoke Staff'; role = 2; password = $staffPassword } $directorHeaders
    $staffLogin = Json-Request 'POST' '/api/login' @{ userName = 'smoke-staff'; password = $staffPassword }
    $staffHeaders = @{ Authorization = "Bearer $($staffLogin.token)" }

    $anonymous = Invoke-WebRequest -Uri "$baseUrl/api/operations/archives" -SkipHttpErrorCheck
    Assert-Check ([int]$anonymous.StatusCode -eq 401) 'Anonymous backup access was not denied.'
    foreach ($method in @('GET', 'POST')) {
        $denied = Invoke-WebRequest -Method $method -Uri "$baseUrl/api/operations/archives" -Headers $staffHeaders -SkipHttpErrorCheck
        Assert-Check ([int]$denied.StatusCode -eq 403) 'Staff backup access did not return 403.'
    }

    $file = Join-Path $env:RUNNER_TEMP 'smoke-report.txt'
    Set-Content -Path $file -Value 'Report before backup' -NoNewline
    $null = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/files/WorkingFiles/upload" -Headers $staffHeaders -Form @{ file = Get-Item $file }
    $backup = Json-Request 'POST' '/api/operations/archives' @{} $directorHeaders
    Invoke-WebRequest -Uri "$baseUrl/api/operations/archives/$($backup.fileName)" -Headers $directorHeaders -OutFile $archive
    Assert-Check ((Get-Item $archive).Length -gt 0) 'The archive download was empty.'

    Set-Content -Path $file -Value 'Report after backup' -NoNewline
    $null = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/files/WorkingFiles/upload" -Headers $staffHeaders -Form @{ file = Get-Item $file }
    $null = Json-Request 'PUT' "/api/users/$($staff.id)" @{ userName = 'smoke-staff'; displayName = 'Smoke Staff'; role = 2; isEnabled = $false } $directorHeaders
    $revoked = Invoke-WebRequest -Method Post -Uri "$baseUrl/api/session/heartbeat" -Headers $staffHeaders -SkipHttpErrorCheck
    Assert-Check ([int]$revoked.StatusCode -eq 401) 'A disabled account retained an active session.'

    Stop-InstalledServer
    & $server --restore-archive $archive --confirm-restore
    Assert-Check ($LASTEXITCODE -eq 0) 'The installed offline restore command failed.'
    $null = Start-Process -FilePath $server -WorkingDirectory (Split-Path $server) -PassThru
    Wait-Server
    $restoredDirector = Json-Request 'POST' '/api/login' @{ userName = 'smoke-director'; password = $password }
    Assert-Check ($restoredDirector.userId -eq $director.id) 'Director identity or credentials were not preserved.'
    $restoredStaff = Json-Request 'POST' '/api/login' @{ userName = 'smoke-staff'; password = $staffPassword }
    Assert-Check ($restoredStaff.userId -eq $staff.id) 'Staff account state was not restored.'
    $restoredHeaders = @{ Authorization = "Bearer $($restoredStaff.token)" }
    $restoredFile = Join-Path $env:RUNNER_TEMP 'restored-smoke-report.txt'
    Invoke-WebRequest -Uri "$baseUrl/api/files/WorkingFiles/download?fileName=smoke-report.txt" -Headers $restoredHeaders -OutFile $restoredFile
    Assert-Check ((Get-Content $restoredFile -Raw) -eq 'Report before backup') 'Office file contents were not restored.'

    $throttled = $false
    for ($attempt = 0; $attempt -lt 21; $attempt++) {
        $requestBody = @{ userName = "unknown-$attempt"; password = $password } | ConvertTo-Json
        $result = Invoke-WebRequest -Method Post -Uri "$baseUrl/api/login" -ContentType 'application/json' -Body $requestBody -SkipHttpErrorCheck
        if ([int]$result.StatusCode -eq 429) { $throttled = $true; break }
        Assert-Check ([int]$result.StatusCode -eq 401) 'An invalid login returned an unexpected status.'
    }
    Assert-Check $throttled 'Per-computer login throttling was not enforced.'
    Write-Host 'Installed server smoke checks passed: startup, accounts, backup authorization, upload, archive download, session revocation, offline restore, original credentials/files and login throttling.'
} finally {
    Stop-InstalledServer
}
