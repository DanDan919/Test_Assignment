param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$')]
    [string] $Domain,
    [string] $OutputDirectory = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$taskRepository = (Resolve-Path -LiteralPath $OutputDirectory).Path
$taskSecretDirectory = Join-Path $taskRepository '.secrets'
$taskEnvironmentPath = Join-Path $taskRepository '.env.production'
if ((Test-Path -LiteralPath $taskSecretDirectory) -or (Test-Path -LiteralPath $taskEnvironmentPath)) {
    throw 'Production secret directory or .env.production already exists. Existing credentials will not be overwritten.'
}

$taskSecretDirectory = New-Item -ItemType Directory -Path $taskSecretDirectory
if ($env:OS -eq 'Windows_NT') {
    $taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls.exe $taskSecretDirectory.FullName /inheritance:r /grant:r "${taskIdentity}:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to restrict secret directory permissions.' }
} else {
    & chmod 700 $taskSecretDirectory.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Unable to restrict secret directory permissions.' }
}

function New-RandomSecret {
    $taskRandomBytes = New-Object byte[] 32
    $taskRandom = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $taskRandom.GetBytes($taskRandomBytes)
        return [Convert]::ToBase64String($taskRandomBytes)
    } finally {
        $taskRandom.Dispose()
        [Array]::Clear($taskRandomBytes, 0, $taskRandomBytes.Length)
    }
}

$taskUtf8 = New-Object Text.UTF8Encoding $false
$taskApplicationPassword = New-RandomSecret
$taskSecretValues = @{
    api_key = New-RandomSecret
    db_admin_password = New-RandomSecret
    db_app_password = $taskApplicationPassword
    api_connection = "Host=postgres;Database=testtask;Username=testtask_app;Password=$taskApplicationPassword;Timeout=10;Command Timeout=10;Maximum Pool Size=10;Include Error Detail=false"
}
foreach ($taskEntry in $taskSecretValues.GetEnumerator()) {
    $taskSecretPath = Join-Path $taskSecretDirectory.FullName $taskEntry.Key
    [IO.File]::WriteAllText($taskSecretPath, $taskEntry.Value, $taskUtf8)
    if ($env:OS -ne 'Windows_NT') {
        # Docker Compose file secrets are bind mounts; runtime app UID needs file read access.
        # The containing host directory remains owner-only (0700).
        & chmod 644 $taskSecretPath
        if ($LASTEXITCODE -ne 0) { throw 'Unable to set secret file permissions.' }
    }
}
[IO.File]::WriteAllText($taskEnvironmentPath, "PUBLIC_DOMAIN=$Domain`n", $taskUtf8)
Write-Output 'Generated production credentials in .secrets (Git/Docker build ignored). No secrets were printed.'
