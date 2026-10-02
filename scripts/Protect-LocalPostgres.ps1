#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$taskConfigPath = 'C:\Program Files\PostgreSQL\18\data\postgresql.conf'
$taskAutoConfigPath = 'C:\Program Files\PostgreSQL\18\data\postgresql.auto.conf'
$taskServiceName = 'postgresql-x64-18'
$taskUtf8 = New-Object Text.UTF8Encoding $false
$taskConfig = [IO.File]::ReadAllText($taskConfigPath)
$taskPattern = '(?m)^(\s*listen_addresses\s*=\s*)''[^''\r\n]*''(.*)$'
if ([regex]::Matches($taskConfig, $taskPattern).Count -ne 1) {
    throw 'Expected exactly one active listen_addresses setting; configuration was not changed.'
}
if ((Test-Path -LiteralPath $taskAutoConfigPath) -and
    (Select-String -LiteralPath $taskAutoConfigPath -Pattern '^\s*listen_addresses\s*=' -Quiet)) {
    throw 'postgresql.auto.conf overrides listen_addresses; configuration was not changed.'
}
$taskUpdated = [regex]::Replace($taskConfig, $taskPattern, '${1}''localhost''${2}')
$taskBackup = $null
if ($taskUpdated -cne $taskConfig) {
    $taskBackup = "$taskConfigPath.security-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item -LiteralPath $taskConfigPath -Destination $taskBackup -ErrorAction Stop
    try {
        [IO.File]::WriteAllText($taskConfigPath, $taskUpdated, $taskUtf8)
        Restart-Service -Name $taskServiceName -ErrorAction Stop
        (Get-Service -Name $taskServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    } catch {
        Copy-Item -LiteralPath $taskBackup -Destination $taskConfigPath -Force
        Start-Service -Name $taskServiceName -ErrorAction SilentlyContinue
        throw
    }
}
$taskListeners = Get-NetTCPConnection -State Listen -LocalPort 5432
if ($taskListeners.LocalAddress | Where-Object { $_ -notin '127.0.0.1','::1' }) {
    throw 'A PostgreSQL listener still binds a non-loopback address. Review service configuration.'
}
$taskReportDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'TestResults/security'
New-Item -ItemType Directory -Path $taskReportDirectory -Force | Out-Null
@{State='PASS';Service=$taskServiceName;Backup=$taskBackup;Listeners=@($taskListeners.LocalAddress)} |
    ConvertTo-Json | Out-File -LiteralPath (Join-Path $taskReportDirectory 'local-postgres.json') -Encoding utf8
Write-Output 'PostgreSQL Windows now listens only on localhost. Configuration backup was preserved.'
