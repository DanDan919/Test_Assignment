param([switch] $Production)
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRepository
$taskReportDirectory = Join-Path $taskRepository 'TestResults/security'
New-Item -ItemType Directory -Path $taskReportDirectory -Force | Out-Null
$taskResults = New-Object 'Collections.Generic.List[object]'
$taskCurl = (Get-Command curl.exe -ErrorAction SilentlyContinue).Source
if (!$taskCurl) { $taskCurl = (Get-Command curl -CommandType Application).Source }
$taskBaseUrl = if ($Production) { 'https://localhost:9443' } else { 'http://127.0.0.1:8090' }
$taskApiKey = if ($Production) { [IO.File]::ReadAllText((Join-Path $taskReportDirectory '.secrets/api_key')) } else { $null }

function Test-Http {
    param([string] $Name, [string] $Path, [int] $ExpectedStatus,
        [string] $PayloadPath, [string] $ErrorCode, [string[]] $Headers = @(),
        [string] $ExpectedResult, [switch] $NoKey, [string] $ContentType = 'application/json')
    $taskBodyPath = Join-Path $taskReportDirectory "$Name-response.json"
    $taskArguments = @('--silent', '--show-error', '--noproxy', '*', '--max-time', '30',
        '--output', $taskBodyPath, '--write-out', '%{http_code}')
    if ($Production) {
        $taskArguments += @('--cacert', (Join-Path $taskReportDirectory 'localhost-ca.crt'))
        if ($env:OS -eq 'Windows_NT') {
            # The local test CA has no revocation distribution point. Keep chain/hostname validation.
            $taskArguments += '--ssl-revoke-best-effort'
        }
    }
    if ($PayloadPath) { $taskArguments += @('--header', "Content-Type: $ContentType", '--data-binary', "@$PayloadPath") }
    $taskRequestHeaders = @($Headers)
    if ($taskApiKey -and !$NoKey) { $taskRequestHeaders += "X-API-Key: $taskApiKey" }
    $taskArguments += @('--header', '@-', "$taskBaseUrl$Path")
    # Secrets go through stdin, never command-line arguments, report content, or console output.
    $taskStatus = ($taskRequestHeaders | & $taskCurl @taskArguments) -join ''
    if ($LASTEXITCODE -ne 0) { throw "curl failed for $Name" }
    if ([int] $taskStatus -ne $ExpectedStatus) { throw "$Name expected $ExpectedStatus, received $taskStatus" }
    if ($ErrorCode -or $ExpectedResult) {
        $taskBody = Get-Content -LiteralPath $taskBodyPath -Raw | ConvertFrom-Json
        if ($ErrorCode -and ($taskBody.is_error -ne 1 -or $taskBody.error_code -notin ($ErrorCode -split ','))) {
            throw "$Name returned an unexpected error contract"
        }
        if ($ExpectedResult) {
            $taskExpected = Get-Content -LiteralPath $ExpectedResult -Raw | ConvertFrom-Json | ConvertTo-Json -Depth 40 -Compress
            $taskActual = $taskBody | ConvertTo-Json -Depth 40 -Compress
            if ($taskActual -cne $taskExpected) { throw "$Name does not match the saved actual API response" }
        }
    }
    $taskResults.Add([PSCustomObject]@{Check=$Name;Status=[int]$taskStatus;Result='PASS'})
}

if ($Production) {
    Test-Http -Name prod-missing-key -Path '/api/process' -ExpectedStatus 401 -PayloadPath 'json_payload_1.txt' -ErrorCode UNAUTHORIZED -NoKey
    Test-Http -Name prod-wrong-key -Path '/api/process' -ExpectedStatus 401 -PayloadPath 'json_payload_1.txt' -ErrorCode UNAUTHORIZED -NoKey -Headers 'X-API-Key: wrong'
    Test-Http -Name prod-duplicate-key -Path '/api/process' -ExpectedStatus 401 -PayloadPath 'json_payload_1.txt' -ErrorCode UNAUTHORIZED -Headers 'X-API-Key: wrong'
    Test-Http -Name prod-swagger-disabled -Path '/api/swagger/v1/swagger.json' -ExpectedStatus 404 -ErrorCode INVALID_REQUEST
} else {
    Test-Http -Name dev-root -Path '/' -ExpectedStatus 302
    Test-Http -Name dev-swagger -Path '/api/swagger' -ExpectedStatus 301
    Test-Http -Name dev-swagger-ui -Path '/api/swagger/index.html' -ExpectedStatus 200
    Test-Http -Name dev-openapi -Path '/api/swagger/v1/swagger.json' -ExpectedStatus 200
}
Test-Http -Name "payload1-$Production" -Path '/api/process' -ExpectedStatus 200 -PayloadPath 'json_payload_1.txt' -ExpectedResult 'json_result_1.txt'
Test-Http -Name "payload2-$Production" -Path '/api/process' -ExpectedStatus 200 -PayloadPath 'json_payload_2.txt' -ExpectedResult 'json_result_2.txt'

# SQL-looking attribute content must remain ordinary data, not an executable query.
$taskSqlValue = "x'); DROP TABLE public.elements;--"
$taskSqlPayload = Get-Content -LiteralPath 'json_payload_1.txt' -Raw | ConvertFrom-Json
$taskSqlPayload.selector = 'a'
$taskSqlPayload.attribute = 'data-security'
$taskSqlPayload.page_b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("<a data-security=`"$taskSqlValue`">safe</a>"))
$taskSqlPath = Join-Path $taskReportDirectory 'sql-looking-data.json'
[IO.File]::WriteAllText($taskSqlPath, ($taskSqlPayload | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
Test-Http -Name "sql-looking-data-$Production" -Path '/api/process' -ExpectedStatus 200 -PayloadPath $taskSqlPath
$taskSqlResponse = Get-Content -LiteralPath (Join-Path $taskReportDirectory "sql-looking-data-$Production-response.json") -Raw | ConvertFrom-Json
if ($taskSqlResponse.is_error -ne 0 -or $taskSqlResponse.elements_count -ne 1 -or $taskSqlResponse.elements_attr_list[0] -cne $taskSqlValue) {
    throw 'SQL-looking content was not handled as ordinary attribute data'
}

foreach ($taskComplexity in @('depth', 'nodes')) {
    $taskComplexPayload = Get-Content -LiteralPath 'json_payload_1.txt' -Raw | ConvertFrom-Json
    $taskComplexHtml = if ($taskComplexity -eq 'depth') { ('<div>' * 130) + 'x' + ('</div>' * 130) } else { '<b></b>' * 10001 }
    $taskComplexPayload.page_b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($taskComplexHtml))
    $taskComplexPath = Join-Path $taskReportDirectory "dom-$taskComplexity.json"
    [IO.File]::WriteAllText($taskComplexPath, ($taskComplexPayload | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
    Test-Http -Name "dom-$taskComplexity-$Production" -Path '/api/process' -ExpectedStatus 413 -PayloadPath $taskComplexPath -ErrorCode LIMIT_EXCEEDED
}
Test-Http -Name "cross-site-$Production" -Path '/api/process' -ExpectedStatus 403 -PayloadPath 'json_payload_1.txt' -ErrorCode FORBIDDEN -Headers 'Sec-Fetch-Site: cross-site'
if (!$Production) {
    Test-Http -Name dev-unknown-host -Path '/api/process' -ExpectedStatus 403 -PayloadPath 'json_payload_1.txt' -ErrorCode FORBIDDEN -Headers 'Host: attacker.example'
}

$taskMalformed = Join-Path $taskReportDirectory 'malformed.json'
[IO.File]::WriteAllText($taskMalformed, '{', (New-Object Text.UTF8Encoding $false))
Test-Http -Name "malformed-$Production" -Path '/api/process' -ExpectedStatus 400 -PayloadPath $taskMalformed -ErrorCode VALIDATION_ERROR
Test-Http -Name "unsupported-media-$Production" -Path '/api/process' -ExpectedStatus 415 -PayloadPath $taskMalformed -ErrorCode INVALID_REQUEST -ContentType 'text/plain'
$taskOversized = Join-Path $taskReportDirectory 'oversized.json'
[IO.File]::WriteAllText($taskOversized, ('{"page_b64":"' + ('A' * 1048576) + '"}'), (New-Object Text.UTF8Encoding $false))
# Production proxy uses its own predictable INVALID_REQUEST error for HTTP rejections.
$taskTooLargeCode = if ($Production) { 'INVALID_REQUEST,REQUEST_TOO_LARGE' } else { 'REQUEST_TOO_LARGE' }
Test-Http -Name "oversized-$Production" -Path '/api/process' -ExpectedStatus 413 -PayloadPath $taskOversized -ErrorCode $taskTooLargeCode
Test-Http -Name "chunked-oversized-$Production" -Path '/api/process' -ExpectedStatus 413 -PayloadPath $taskOversized -ErrorCode $taskTooLargeCode -Headers 'Transfer-Encoding: chunked'
$taskResults | Format-Table -AutoSize
$taskResults | ConvertTo-Json | Out-File -LiteralPath (Join-Path $taskReportDirectory "http-$Production.json") -Encoding utf8
