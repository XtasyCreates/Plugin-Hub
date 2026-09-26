# Renders each plug-in's panel icon (icons\panel_light.svg) as icons\package.png, 128 px with a
# transparent background - the icon Rhino's Package Manager shows for the .yak package.
#
# Edge draws the SVG (headless), so the PNG is exactly the vector icon the toolbar shows.
# Run again after changing a panel icon; the hub picks package.png up with the next package.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # the "Plugins Rhino" folder
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }

foreach ($plugin in 'LaserExportPlugin', 'SwToLaserPlugin') {
    $svg = Join-Path $root "$plugin\icons\panel_light.svg"
    $png = Join-Path $root "$plugin\icons\package.png"
    if (-not (Test-Path $svg)) { throw "Missing $svg" }

    $page = Join-Path ([IO.Path]::GetTempPath()) "package-icon-$plugin.html"
    $src  = ([Uri]$svg).AbsoluteUri
    [IO.File]::WriteAllText($page,
        "<!doctype html><html><head><style>html,body{margin:0;background:transparent}img{width:128px;height:128px;display:block}</style></head>" +
        "<body><img src=""$src""></body></html>", (New-Object System.Text.UTF8Encoding($false)))

    # Edge reports "bytes written" on stderr, which Windows PowerShell would take for an error
    $ErrorActionPreference = 'Continue'
    & $edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 `
        --window-size=128,128 "--screenshot=$png" ([Uri]$page).AbsoluteUri 2>&1 | Out-Null
    $ErrorActionPreference = 'Stop'
    Remove-Item $page -ErrorAction SilentlyContinue

    if (-not (Test-Path $png)) { throw "Edge wrote no $png" }
    Write-Output "Written: $plugin\icons\package.png"
}
