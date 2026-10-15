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

function Get-OrRegisterAccountId([string]$Email, [string]$Kind, [string]$RegisterUrl, [hashtable]$Headers, [string]$ApiPort) {
    $body = @{ email = $Email; password = $env:TEST_USER_PASSWORD } | ConvertTo-Json -Compress
    try {
        $registered = Invoke-RestMethod -Method Post -Uri $RegisterUrl -Headers $Headers -Body $body -TimeoutSec 30
        $body = $null
        if (-not $registered.accountId) { Stop-Safely "Synthetic $Kind registration returned no account ID." }
        return [string]$registered.accountId
    } catch {
        $status = 0
        if ($_.Exception.Response) {
            try { $status = [int]$_.Exception.Response.StatusCode } catch { $status = 0 }
        }
        $body = $null
        if ($status -ne 409) {
            Stop-Safely "Synthetic $Kind registration failed (HTTP $status). Secret response details suppressed."
        }
    }

    try {
        $loginBody = @{ email = $Email; password = $env:TEST_USER_PASSWORD } | ConvertTo-Json -Compress
        $login = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$ApiPort/api/auth/login" -Headers $Headers -Body $loginBody -TimeoutSec 30
        $loginBody = $null
        $token = [string]$login.accessToken
        $login = $null
        if ([string]::IsNullOrWhiteSpace($token)) { Stop-Safely "Existing synthetic $Kind user could not authenticate." }
        $accounts = Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:$ApiPort/api/accounts/me" -Headers @{ Authorization = "Bearer $token" } -TimeoutSec 30
        $token = $null
        $accountItems = @($accounts)
        $accounts = $null
        if ($accountItems.Count -ne 1 -or -not $accountItems[0].id) {
            Stop-Safely "Existing synthetic $Kind user did not have exactly one account. Response details suppressed."
        }
        return [string]$accountItems[0].id
    } catch {
        Stop-Safely "Existing synthetic $Kind user could not be reused. Change TEST_USER_PREFIX or explicitly use -ResetLoadVolume. Response details suppressed."
    }
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
$testUserCount = 25
if ($env:LOAD_TEST_USER_COUNT) {
    $parsedCount = 0
    if (-not [int]::TryParse($env:LOAD_TEST_USER_COUNT, [ref]$parsedCount)) {
        Stop-Safely 'LOAD_TEST_USER_COUNT must be an integer of at least 25.'
    }
    $testUserCount = $parsedCount
}
if ($testUserCount -lt 25 -or $testUserCount -gt 500) {
    Stop-Safely 'LOAD_TEST_USER_COUNT must be between 25 and 500.'
}
$userPrefix = if ($env:TEST_USER_PREFIX) { $env:TEST_USER_PREFIX } else { 'fincore-lt2' }
if ($userPrefix -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,31}$') {
    Stop-Safely 'TEST_USER_PREFIX may contain only letters, digits, and hyphens (up to 32 characters).'
}
$env:LOAD_TEST_USER_COUNT = [string]$testUserCount
$env:TEST_USER_PREFIX = $userPrefix

$apiPort = if ($env:LOAD_API_PORT) { $env:LOAD_API_PORT } else { '18080' }
$pgPort = if ($env:LOAD_POSTGRES_PORT) { $env:LOAD_POSTGRES_PORT } else { '55432' }
$expectedBaseUrl = 'http://api:8080'
if ($env:BASE_URL -and $env:BASE_URL.TrimEnd('/') -ne $expectedBaseUrl) {
    Stop-Safely 'BASE_URL must be exactly http://api:8080. External or unknown targets are blocked.'
}
$env:BASE_URL = $expectedBaseUrl

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
    $fixtures = [System.Collections.Generic.List[object]]::new()
    for ($index = 1; $index -le $testUserCount; $index++) {
        $suffix = '{0:D3}' -f $index
        $senderEmail = "$userPrefix-sender-$suffix@example.test"
        $receiverEmail = "$userPrefix-receiver-$suffix@example.test"
        $senderAccountId = Get-OrRegisterAccountId -Email $senderEmail -Kind 'sender' -RegisterUrl $registerUrl -Headers $headers -ApiPort $apiPort
        $receiverAccountId = Get-OrRegisterAccountId -Email $receiverEmail -Kind 'receiver' -RegisterUrl $registerUrl -Headers $headers -ApiPort $apiPort
        if (-not $senderAccountId -or -not $receiverAccountId -or $senderAccountId -eq $receiverAccountId) {
            Stop-Safely "Synthetic account pair $index was not distinct."
        }
        $fixtures.Add([ordered]@{
            index = $index
            senderEmail = $senderEmail
            sourceAccountId = $senderAccountId
            destinationAccountId = $receiverAccountId
        })
    }

    # Performance fixture only: directly credit the sender in the isolated load DB.
    # This bypasses ledger intentionally and must never be used with normal/production databases.
    $balance = if ($env:LOAD_TEST_BALANCE) { [decimal]::Parse($env:LOAD_TEST_BALANCE, [Globalization.CultureInfo]::InvariantCulture) } else { 100000.00 }
    if ($balance -lt 1) { Stop-Safely 'LOAD_TEST_BALANCE must be at least 1.00.' }
    $sourceIds = ($fixtures | ForEach-Object { "'$( $_.sourceAccountId )'" }) -join ', '
    $sql = 'UPDATE accounts SET balance = ' + $balance.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) + ' WHERE id IN (' + $sourceIds + ');'
    $updateOutput = & docker @composeArgs exec -T postgres psql -U $env:POSTGRES_USER -d $databaseName -c $sql 2>$null
    if ($LASTEXITCODE -ne 0) { Stop-Safely 'Could not prepare sender balance in the isolated load database.' }
    if (($updateOutput -join "`n") -notmatch "UPDATE $testUserCount") { Stop-Safely 'The balance fixture did not update exactly the configured number of isolated sender accounts.' }

    $generated = $fixtures.ToArray()
    $generatedJson = $generated | ConvertTo-Json
    [System.IO.File]::WriteAllText($generatedFile, $generatedJson, [System.Text.UTF8Encoding]::new($false))
    New-Item -ItemType Directory -Path (Join-Path $loadDir 'results') -Force | Out-Null
    Write-Host 'Load-test PostgreSQL and API are ready; migrations applied only to fincore_load.'
    Write-Host "$testUserCount synthetic sender/receiver pairs are available in data/accounts.generated.json."
    Write-Host 'Sender balances are ledger-external performance fixtures in the isolated load database.'
} finally {
    $connectionString = $null
    Remove-Item Env:Logging__EventLog__LogLevel__Default -ErrorAction SilentlyContinue
    Pop-Location
}
