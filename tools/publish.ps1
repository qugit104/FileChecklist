$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $version = ([xml](Get-Content src/FileChecklist.Desktop/FileChecklist.Desktop.csproj -Raw)).Project.PropertyGroup.Version
    foreach ($package in @(
        @{ Project='src/FileChecklist.Desktop'; Name="FileChecklist-$version-win-x64"; Exe='FileChecklist.exe' },
        @{ Project='src/FileChecklist.Cli'; Name="filechecklist-cli-$version-win-x64"; Exe='filechecklist.exe' }
    )) {
        $outDir = Join-Path 'dist' $package.Name
        dotnet publish $package.Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $outDir
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($package.Name)" }
        Copy-Item -LiteralPath README.md,README.zh-CN.md,CONTRIBUTING.md,LICENSE,THIRD-PARTY-NOTICES.md -Destination $outDir
        $docsOut = Join-Path $outDir 'docs'
        New-Item -ItemType Directory -Path $docsOut -Force | Out-Null
        Copy-Item -LiteralPath docs/CLI.md,docs/BROWSER.md,docs/RELEASE-0.2.md,docs/RELEASE-0.3.md,docs/desktop.png,docs/preview.png,docs/desktop-en.png,docs/preview-en.png,docs/import-excel.png,docs/import-excel-en.png,docs/RELEASE-0.4.md -Destination $docsOut -Force
        Copy-Item -LiteralPath examples,licenses -Destination $outDir -Recurse -Force
        $hash = Get-FileHash -LiteralPath (Join-Path $outDir $package.Exe) -Algorithm SHA256
        ($hash.Hash + '  ' + $package.Exe) | Set-Content -LiteralPath (Join-Path $outDir 'SHA256.txt') -Encoding Ascii
        Compress-Archive -Path $outDir -DestinationPath ($outDir + '.zip') -Force
        Write-Output ('Portable package: ' + $outDir + '.zip')
    }
} finally { Pop-Location }
