# Build the desktop pet.
#  1. cut the background out of the source art            (Python + Pillow)
#  2. compile the C# app with the in-box .NET compiler    (no installs needed)
#  3. embed the sprite PNG and the icon into a single .exe
$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo   = Split-Path -Parent $root
$py     = 'C:\Users\18384\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
$csc    = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$art    = Join-Path $repo 'a88dfc3c269759ee8656b3c8f4fb43166d22df2d.jpg'

$build  = Join-Path $root 'build'
$dist   = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $build, $dist | Out-Null

$png    = Join-Path $build 'sprite.png'
$ico    = Join-Path $build 'pet.ico'
$prev   = Join-Path $build 'preview.png'

Write-Host '--- [1/3] background removal ---'
& $py (Join-Path $root 'tools\make_sprite.py') $art $png $ico $prev
if ($LASTEXITCODE -ne 0) { throw "sprite step failed ($LASTEXITCODE)" }

Write-Host '--- [2/3] compile ---'
$exe = Join-Path $dist 'Pet.exe'
if (Test-Path $exe) { Remove-Item $exe -Force }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /unsafe `
    "/win32icon:$ico" `
    "/resource:$png,sprite.png" `
    "/out:$exe" `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    (Join-Path $root 'src\Pet.cs')
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }

Write-Host '--- [3/3] result ---'
$fi = Get-Item $exe
Write-Host ("EXE: {0}  ({1:N0} bytes)" -f $fi.FullName, $fi.Length)
