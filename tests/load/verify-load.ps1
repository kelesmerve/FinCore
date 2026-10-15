[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$envFile = Join-Path $PSScriptRoot '.env.load'
if (-not (Test-Path -LiteralPath $envFile)) {
    throw 'Missing tests/load/.env.load. Verification requires the isolated load environment.'
}

$loadEnv = @{}
Get-Content -LiteralPath $envFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=', 2)
        $loadEnv[$parts[0].Trim()] = $parts[1].Trim()
    }
}
if ($loadEnv['POSTGRES_DB'] -ne 'fincore_load' -or $loadEnv['POSTGRES_USER'] -ne 'fincore_load') {
    throw 'Load database isolation validation failed; refusing invariant checks.'
}

$composeArgs = @('compose', '--project-name', 'fincore-load', '--env-file', $envFile, '-f', (Join-Path $PSScriptRoot 'compose.load.yaml'))
$sql = @'
SELECT json_build_object(
  'ledgerTransactions', (SELECT count(*) FROM ledger_transactions),
  'ledgerEntries', (SELECT count(*) FROM ledger_entries),
  'debitEntries', (SELECT count(*) FROM ledger_entries WHERE type = 'Debit'),
  'creditEntries', (SELECT count(*) FROM ledger_entries WHERE type = 'Credit'),
  'debitTotal', COALESCE((SELECT sum(amount) FROM ledger_entries WHERE type = 'Debit'), 0),
  'creditTotal', COALESCE((SELECT sum(amount) FROM ledger_entries WHERE type = 'Credit'), 0),
  'totalsEqual', COALESCE((SELECT sum(amount) FROM ledger_entries WHERE type = 'Debit'), 0) = COALESCE((SELECT sum(amount) FROM ledger_entries WHERE type = 'Credit'), 0),
  'duplicateIdempotencyKeys', (SELECT count(*) FROM (
    SELECT user_id, operation, key FROM idempotency_records
    GROUP BY user_id, operation, key HAVING count(*) > 1
  ) AS duplicates),
  'negativeBalances', (SELECT count(*) FROM accounts WHERE balance < 0)
)::text;
'@
$jsonText = (& docker @composeArgs exec -T postgres psql -U $loadEnv['POSTGRES_USER'] -d fincore_load -Atc $sql) -join ''
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($jsonText)) {
    throw 'Could not read invariants from the isolated load database.'
}

$result = $jsonText | ConvertFrom-Json
$validEntryShape = $result.ledgerEntries -eq ($result.debitEntries + $result.creditEntries) -and
    $result.debitEntries -eq $result.creditEntries -and
    $result.ledgerEntries -eq (2 * $result.ledgerTransactions)
$valid = $result.ledgerTransactions -gt 0 -and $validEntryShape -and
    $result.totalsEqual -and $result.duplicateIdempotencyKeys -eq 0 -and
    $result.negativeBalances -eq 0

Write-Output "Ledger transactions: $($result.ledgerTransactions); entries: $($result.ledgerEntries); debit/credit entries: $($result.debitEntries)/$($result.creditEntries)."
Write-Output "Debit and credit totals equal: $($result.totalsEqual); duplicate idempotency scope/key records: $($result.duplicateIdempotencyKeys); negative balances: $($result.negativeBalances)."
if (-not $valid) {
    Write-Error 'One or more isolated load database ledger invariants failed.'
    exit 1
}
Write-Output 'Load database invariants passed.'
