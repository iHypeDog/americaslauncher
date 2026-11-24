$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$output = Join-Path $PSScriptRoot 'Americas Launcher.exe'
$icon = Join-Path $PSScriptRoot 'media/app-icon.ico'
$sources = @(
    'Launcher.xaml',
    'runtime/victory.exe',
    'runtime/VC_redist.x64.exe',
    'runtime/LICENSE',
    'runtime/victory-source.zip',
    'media/background.mp4',
    'media/background.jpg',
    'media/app-icon.png'
)
$references = @('WindowsBase', 'PresentationCore', 'PresentationFramework', 'UIAutomationTypes', 'UIAutomationProvider') |
    ForEach-Object { '/reference:{0}' -f (Join-Path $wpf "$_.dll") }
$references += @('/reference:System.Xaml.dll', '/reference:System.Windows.Forms.dll')
$resources = $sources | ForEach-Object {
    '/resource:{0},{1}' -f (Join-Path $PSScriptRoot $_), (Split-Path $_ -Leaf)
}

& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ "/out:$output" "/win32icon:$icon" @references @resources (Join-Path $PSScriptRoot 'Launcher.cs') (Join-Path $PSScriptRoot 'Updater.cs') (Join-Path $PSScriptRoot 'Prerequisites.cs')
if ($LASTEXITCODE -ne 0) { throw "A compilação falhou (código $LASTEXITCODE)." }
Get-Item -LiteralPath $output
