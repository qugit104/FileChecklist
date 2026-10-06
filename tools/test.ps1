param([string]$AppPath, [ValidateSet('zh','en')][string[]]$Languages = @('zh','en'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet run --project tests/FileChecklist.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core regression failed' }
    if (-not $AppPath) {
        dotnet build src/FileChecklist.Desktop -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
        $AppPath = Join-Path $projectRoot 'src/FileChecklist.Desktop/bin/Release/net9.0-windows/FileChecklist.exe'
    }
    $resolvedApp = (Resolve-Path -LiteralPath $AppPath).Path
    foreach ($language in $Languages) {
    $smokeOutput = Join-Path $projectRoot ('artifacts/ui-' + $language + '-' + [guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath $resolvedApp -ArgumentList @('--lang', $language, '--smoke', ('"' + $smokeOutput + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'UI smoke timed out after 45 seconds' }
    $process.Refresh()
    $report = Join-Path $smokeOutput 'smoke-result.txt'
    if ($process.ExitCode -ne 0) { if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }; throw "UI smoke exited $($process.ExitCode)" }
    if (-not (Test-Path -LiteralPath $report)) { throw 'UI smoke result missing' }
    $result = Get-Content -LiteralPath $report -Raw
    if (-not $result.StartsWith('PASS:')) { throw $result }
    Write-Output $result
    Write-Output "PASS: UI process exit 0. Artifacts: $smokeOutput"
    }
} finally { Pop-Location }
