[CmdletBinding()]
param(
    [switch]$ResetLoadVolume
)

$ErrorActionPreference = 'Stop'
$loadDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $loadDir '..\..')).Path
$envFile = Join-Path $loadDir '.env.load'
$composeFile = Join-Path $loadDir 'compose.load.yaml'
$generatedFile = Join-Path $loadDir 'data\accounts.generated.json'
$projectName = 'fincore-load'
$volumeName = 'fincore_load_postgres_data'
$databaseName = 'fincore_load'

function Stop-Safely([string]$Message) {
    throw $Message
}

if (-not (Test-Path -LiteralPath $envFile)) {
    Stop-Safely 'Missing tests/load/.env.load. Copy .env.load.example and set unique synthetic test values first.'
}

# Load only this environment file into this PowerShell process; never print values.
Get-Content -LiteralPath $envFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=', 2)
        [Environment]::SetEnvironmentVariable($parts[0].Trim(), $parts[1].Trim(), 'Process')
    }
}

$required = @('POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'JWT_SECRET', 'TEST_USER_PASSWORD')
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        Stop-Safely "Required synthetic load-test variable $name is empty."
    }
}

if ($env:POSTGRES_DB -ne $databaseName) {
    Stop-Safely "POSTGRES_DB must be '$databaseName' so migrations cannot target the regular database."
}
if ($env:POSTGRES_USER -ne 'fincore_load') {
    Stop-Safely "POSTGRES_USER must be 'fincore_load' for the isolated load environment."
}
if (($env:LOAD_API_PORT -and $env:LOAD_API_PORT -ne '18080') -or ($env:LOAD_POSTGRES_PORT -and $env:LOAD_POSTGRES_PORT -ne '55432')) {
    Stop-Safely 'Use the reserved isolated load ports LOAD_API_PORT=18080 and LOAD_POSTGRES_PORT=55432.'
}
if ($env:POSTGRES_PASSWORD -match '^(change_me|replace_with_)' -or $env:JWT_SECRET -match '^(replace_with_)' -or $env:TEST_USER_PASSWORD -match '^replace_with_') {
    Stop-Safely 'Replace the example placeholders with unique synthetic load-test-only values.'
}
if ($env:JWT_SECRET.Length -lt 32) {
    Stop-Safely 'JWT_SECRET must contain at least 32 characters for the isolated load API.'
}
if ($env:TEST_USER_PASSWORD.Length -lt 8 -or $env:TEST_USER_PASSWORD -notmatch '[A-Z]' -or $env:TEST_USER_PASSWORD -notmatch '[a-z]' -or $env:TEST_USER_PASSWORD -notmatch '\d' -or $env:TEST_USER_PASSWORD -notmatch '[^a-zA-Z0-9\s]') {
    Stop-Safely 'TEST_USER_PASSWORD must meet registration rules using a synthetic password.'
}

$apiPort = if ($env:LOAD_API_PORT) { $env:LOAD_API_PORT } else { '18080' }
$pgPort = if ($env:LOAD_POSTGRES_PORT) { $env:LOAD_POSTGRES_PORT } else { '55432' }
$expectedBaseUrl = 'http://api:8080'
if ($env:BASE_URL -and $env:BASE_URL.TrimEnd('/') -ne $expectedBaseUrl) {
    Stop-Safely 'BASE_URL must be exactly http://api:8080. External or unknown targets are blocked.'
}
$env:BASE_URL = $expectedBaseUrl

if ($env:TEST_USER_EMAIL -notmatch '@example\.test$' -or $env:TEST_RECEIVER_EMAIL -notmatch '@example\.test$' -or $env:TEST_USER_EMAIL -eq $env:TEST_RECEIVER_EMAIL) {
    Stop-Safely 'Both account emails must be distinct synthetic @example.test addresses.'
}

$composeArgs = @('compose', '--project-name', $projectName, '--env-file', $envFile, '-f', $composeFile)
$normalVolume = 'fincore_postgres_data'
if ($ResetLoadVolume) {
    # Confirm the one fixed target before invoking a volume removal.
    if ($volumeName -ne 'fincore_load_postgres_data' -or $volumeName -eq $normalVolume) {
        Stop-Safely 'Load volume safety check failed; refusing reset.'
    }
    Write-Host 'Resetting only fincore_load_postgres_data as explicitly requested.'
    & docker @composeArgs down
    if ($LASTEXITCODE -ne 0) { Stop-Safely 'Could not stop the load-test Compose project.' }
    & docker volume rm $volumeName 2>$null | Out-Null
    # It is acceptable for the volume not to exist yet; Compose will create it below.
}

Push-Location $repoRoot
try {
    # Compare resource names and the target database before starting or migrating.
    $normalCompose = Get-Content -Raw (Join-Path $repoRoot 'compose.yaml')
    if ($normalCompose -notmatch 'fincore_postgres_data' -or $normalCompose -notmatch 'POSTGRES_DB') {
        Stop-Safely 'Could not verify the normal Compose database/volume identifiers.'
    }
    if ($normalCompose -match 'fincore_load_postgres_data') {
        Stop-Safely 'Load volume collides with normal Compose configuration.'
    }
    if ($env:POSTGRES_DB -eq 'fincore' -or $volumeName -eq $normalVolume) {
        Stop-Safely 'Load database or volume collides with normal FinCore resources.'
    }
    $normalEnv = @{}
    Get-Content -LiteralPath (Join-Path $repoRoot '.env') -ErrorAction SilentlyContinue | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
            $parts = $line.Split('=', 2)
            $normalEnv[$parts[0].Trim()] = $parts[1].Trim().Trim("'").Trim('"')
        }
    }
    if (($normalEnv.ContainsKey('POSTGRES_PASSWORD') -and $env:POSTGRES_PASSWORD -eq $normalEnv['POSTGRES_PASSWORD']) -or
        ($normalEnv.ContainsKey('JWT_SECRET') -and $env:JWT_SECRET -eq $normalEnv['JWT_SECRET'])) {
        Stop-Safely 'Load credentials must be unique synthetic values and must not reuse a value from the normal .env.'
    }

    & docker @composeArgs up -d --build postgres api
    if ($LASTEXITCODE -ne 0) { Stop-Safely 'Load-test PostgreSQL/API failed to start.' }

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$apiPort/health/ready" -TimeoutSec 3 -UseBasicParsing
            $responseBody = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
            if ($response.StatusCode -eq 200 -and $responseBody -eq 'Healthy') { $ready = $true; break }
        } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $ready) { Stop-Safely 'Load API did not become ready on the isolated host port.' }

    # The migration connection is deliberately pinned to the load-only host port/database.
    $connectionString = "Host=127.0.0.1;Port=$pgPort;Database=$databaseName;Username=$($env:POSTGRES_USER);Password=$($env:POSTGRES_PASSWORD)"
    if ($connectionString -notmatch '^Host=127\.0\.0\.1;Port=55432;Database=fincore_load;Username=fincore_load;Password=' -or
        $connectionString -match 'Database=fincore;' -or $connectionString -match 'Port=5433;') {
        Stop-Safely 'Migration connection safety check failed.'
    }
    & dotnet tool restore --tool-manifest (Join-Path $repoRoot 'dotnet-tools.json') *> $null
    if ($LASTEXITCODE -ne 0) { Stop-Safely 'dotnet tool restore failed.' }
    Write-Host 'Applying migrations to the isolated fincore_load database.'
    $env:Logging__EventLog__LogLevel__Default = 'None'
    & dotnet tool run dotnet-ef database update --project (Join-Path $repoRoot 'src\FinCore.Infrastructure') --startup-project (Join-Path $repoRoot 'src\FinCore.Api') --connection $connectionString *> $null
    Remove-Item Env:Logging__EventLog__LogLevel__Default -ErrorAction SilentlyContinue
    if ($LASTEXITCODE -ne 0) { Stop-Safely 'Migrations failed against the isolated load database.' }

    $registerUrl = "http://127.0.0.1:$apiPort/api/auth/register"
    $headers = @{ 'Content-Type' = 'application/json' }
    $senderEmail = $env:TEST_USER_EMAIL
    $receiverEmail = $env:TEST_RECEIVER_EMAIL
    $senderAccountId = $null
    $receiverAccountId = $null
    foreach ($entry in @(
        @{ Email = $senderEmail; Kind = 'sender' },
        @{ Email = $receiverEmail; Kind = 'receiver' }
    )) {
        $body = @{ email = $entry.Email; password = $env:TEST_USER_PASSWORD } | ConvertTo-Json -Compress
        try {
            $result = Invoke-RestMethod -Method Post -Uri $registerUrl -Headers $headers -Body $body -TimeoutSec 30
        } catch {
            $status = $_.Exception.Response.StatusCode.value__
            if ($status -ne 409) { Stop-Safely "Synthetic $($entry.Kind) registration failed (HTTP $status). Secret response details suppressed." }
            try {
                $loginBody = @{ email = $entry.Email; password = $env:TEST_USER_PASSWORD } | ConvertTo-Json -Compress
                $login = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$apiPort/api/auth/login" -Headers $headers -Body $loginBody -TimeoutSec 30
                $token = $login.accessToken
                $accounts = Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:$apiPort/api/accounts/me" -Headers @{ Authorization = "Bearer $token" } -TimeoutSec 30
                if ($entry.Kind -eq 'sender') { $senderAccountId = $accounts[0].id } else { $receiverAccountId = $accounts[0].id }
                $token = $null
                $login = $null
            } catch {
                Stop-Safely "Existing synthetic $($entry.Kind) could not be reused. Use -ResetLoadVolume or new synthetic emails. Response details suppressed."
            }
            continue
        }
        if ($entry.Kind -eq 'sender') { $senderAccountId = $result.accountId } else { $receiverAccountId = $result.accountId }
    }

    if (-not $senderAccountId -or -not $receiverAccountId -or $senderAccountId -eq $receiverAccountId) {
        Stop-Safely 'Registration did not yield distinct sender and receiver accounts.'
    }

    # Performance fixture only: directly credit the sender in the isolated load DB.
    # This bypasses ledger intentionally and must never be used with normal/production databases.
    $balance = if ($env:LOAD_TEST_BALANCE) { [decimal]::Parse($env:LOAD_TEST_BALANCE, [Globalization.CultureInfo]::InvariantCulture) } else { 100000.00 }
    if ($balance -lt 1) { Stop-Safely 'LOAD_TEST_BALANCE must be at least 1.00.' }
    $sql = 'UPDATE accounts SET balance = ' + $balance.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) + ' WHERE id = ''' + $senderAccountId + ''';'
    $updateOutput = & docker @composeArgs exec -T postgres psql -U $env:POSTGRES_USER -d $databaseName -c $sql 2>$null
    if ($LASTEXITCODE -ne 0) { Stop-Safely 'Could not prepare sender balance in the isolated load database.' }
    if (($updateOutput -join "`n") -notmatch 'UPDATE 1') { Stop-Safely 'Sender account balance fixture did not update exactly one load account.' }

    $generated = [ordered]@{
        sourceAccountId = $senderAccountId
        destinationAccountId = $receiverAccountId
    }
    $generatedJson = $generated | ConvertTo-Json
    [System.IO.File]::WriteAllText($generatedFile, $generatedJson, [System.Text.UTF8Encoding]::new($false))
    New-Item -ItemType Directory -Path (Join-Path $loadDir 'results') -Force | Out-Null
    Write-Host 'Load-test PostgreSQL and API are ready; migrations applied only to fincore_load.'
    Write-Host 'Synthetic sender and receiver accounts are available in data/accounts.generated.json.'
    Write-Host 'Sender balance is a ledger-external performance fixture in the isolated load database.'
} finally {
    $connectionString = $null
    Remove-Item Env:Logging__EventLog__LogLevel__Default -ErrorAction SilentlyContinue
    Pop-Location
}
