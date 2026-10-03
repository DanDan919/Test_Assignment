# Windows-only recovery for stale Docker Desktop AF_UNIX runtime sockets.
# Images, volumes, settings and WSL distributions are never removed.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This recovery script is for Docker Desktop on Windows only.'
}
$taskDocker = (Get-Command docker -CommandType Application | Select-Object -First 1).Source
$taskDesktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
if (!(Test-Path -LiteralPath $taskDesktop -PathType Leaf)) {
    throw "Docker Desktop executable not found: $taskDesktop"
}

function Invoke-BoundedProcess {
    param([string] $FilePath, [string] $Arguments, [int] $TimeoutSeconds = 10)
    $taskStart = New-Object Diagnostics.ProcessStartInfo
    $taskStart.FileName = $FilePath
    $taskStart.Arguments = $Arguments
    $taskStart.UseShellExecute = $false
    $taskStart.CreateNoWindow = $true
    $taskStart.RedirectStandardOutput = $true
    $taskStart.RedirectStandardError = $true
    $taskChild = New-Object Diagnostics.Process
    $taskChild.StartInfo = $taskStart
    try {
        if (!$taskChild.Start()) { throw "Could not start $FilePath" }
        $taskOutput = $taskChild.StandardOutput.ReadToEndAsync()
        $taskError = $taskChild.StandardError.ReadToEndAsync()
        if (!$taskChild.WaitForExit($TimeoutSeconds * 1000)) {
            $taskChild.Kill()
            $taskChild.WaitForExit()
            return [PSCustomObject]@{ ExitCode = -1; Output = ''; Error = 'Timed out' }
        }
        return [PSCustomObject]@{
            ExitCode = $taskChild.ExitCode
            Output = $taskOutput.GetAwaiter().GetResult().Trim()
            Error = $taskError.GetAwaiter().GetResult().Trim()
        }
    }
    finally { $taskChild.Dispose() }
}

$taskInfo = Invoke-BoundedProcess $taskDocker 'info --format {{.ServerVersion}}'
if ($taskInfo.ExitCode -eq 0) {
    Write-Output "Docker engine is already healthy ($($taskInfo.Output)); nothing changed."
    return
}

Write-Output 'Docker engine is unavailable. Stopping Docker Desktop before preserving runtime directories.'
# Try the supported graceful stop first; its CLI can hang when the backend has crashed.
$null = Invoke-BoundedProcess $taskDocker 'desktop stop --timeout 20' 25
$taskProcessNames = @('Docker Desktop', 'com.docker.backend', 'com.docker.proxy', 'dockerd', 'docker-ai', 'docker-desktop')
for ($taskAttempt = 0; $taskAttempt -lt 3; $taskAttempt++) {
    $taskRemaining = @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -in $taskProcessNames })
    if ($taskRemaining.Count -eq 0) { break }
    foreach ($taskProcess in $taskRemaining) {
        if ($taskProcess.HasExited) { continue }
        Stop-Process -Id $taskProcess.Id -Force -ErrorAction Stop
        if (!$taskProcess.WaitForExit(5000)) {
            throw "Docker process did not exit: $($taskProcess.ProcessName) ($($taskProcess.Id))."
        }
    }
}
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in $taskProcessNames }) {
    throw 'Docker processes are still running. Runtime directories were not renamed.'
}

$taskWsl = (Get-Command wsl -CommandType Application | Select-Object -First 1).Source
$taskDistributions = Invoke-BoundedProcess $taskWsl '--list --quiet'
if ($taskDistributions.ExitCode -ne 0) { throw 'Could not enumerate WSL distributions.' }
if (($taskDistributions.Output -replace "`0", '') -split '\r?\n' | Where-Object { $_.Trim() -eq 'docker-desktop' }) {
    $taskTerminate = Invoke-BoundedProcess $taskWsl '--terminate docker-desktop' 30
    if ($taskTerminate.ExitCode -ne 0) { throw "Could not stop Docker WSL: $($taskTerminate.Error)" }
}

$taskLocalAppData = [IO.Path]::GetFullPath([Environment]::GetFolderPath('LocalApplicationData'))
$taskStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
# Only these two disposable runtime parents are in scope; no recursive delete or WSL unregister.
foreach ($taskRelative in @('Docker\run', 'docker-secrets-engine')) {
    $taskRuntime = [IO.Path]::GetFullPath((Join-Path $taskLocalAppData $taskRelative))
    if (!(Test-Path -LiteralPath $taskRuntime)) { continue }
    $taskBackupName = (Split-Path -Leaf $taskRuntime) + '-broken-' + $taskStamp
    Rename-Item -LiteralPath $taskRuntime -NewName $taskBackupName -ErrorAction Stop
    Write-Output "Preserved: $taskRuntime -> $taskBackupName"
}

Start-Process -FilePath $taskDesktop -WindowStyle Hidden
$taskDeadline = (Get-Date).AddSeconds(120)
do {
    $taskInfo = Invoke-BoundedProcess $taskDocker 'info --format {{.ServerVersion}}' 5
    if ($taskInfo.ExitCode -eq 0) {
        Write-Output "Docker engine is ready ($($taskInfo.Output)). Run docker compose up -d."
        return
    }
    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $taskDeadline)
throw 'Docker engine did not become ready. Runtime backups are preserved; inspect Docker Desktop logs.'
