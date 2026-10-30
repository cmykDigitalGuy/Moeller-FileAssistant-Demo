$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$vendor=Join-Path $root 'vendor'
$tools=Join-Path $vendor 'exiftool'
$exe=Join-Path $tools 'exiftool.exe'
try {
 if (!(Test-Path $exe)) {
  New-Item $vendor -ItemType Directory -Force | Out-Null
  $zip=Join-Path $vendor 'exiftool-13.60_64.zip'
  & curl.exe --fail --location --retry 2 --max-time 180 --output $zip 'https://downloads.sourceforge.net/project/exiftool/exiftool-13.60_64.zip'
  if ($LASTEXITCODE -ne 0 -or !(Test-Path $zip) -or (Get-Item $zip).Length -lt 1000000) { throw 'Could not retrieve the official ExifTool runtime.' }
  $extract=Join-Path $vendor 'exiftool-extracted'
  Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force
  $launcher=Get-ChildItem $extract -Recurse -File | Where-Object { $_.Name -eq 'exiftool(-k).exe' } | Select-Object -First 1
  if (!$launcher) { throw 'Official package did not contain exiftool(-k).exe.' }
  New-Item $tools -ItemType Directory -Force | Out-Null
  Get-ChildItem -LiteralPath $launcher.DirectoryName | Copy-Item -Destination $tools -Recurse -Force
  Rename-Item -LiteralPath (Join-Path $tools 'exiftool(-k).exe') -NewName 'exiftool.exe'
  Get-FileHash $zip -Algorithm SHA256 | Format-List | Out-File (Join-Path $vendor 'exiftool-download-sha256.txt')
 }
 if (!(Test-Path (Join-Path $tools 'exiftool_files'))) { throw 'ExifTool runtime folder is missing.' }
 $env:MOELLER_DEMO_EXIFTOOL=$exe
 Write-Output "Verifying bundled metadata tool: $exe"
 & $exe -ver
 if ($LASTEXITCODE -ne 0) { throw "Bundled metadata tool failed with exit $LASTEXITCODE." }
} catch {
 New-Item (Join-Path $root 'artifacts') -ItemType Directory -Force | Out-Null
 $_.Exception.ToString() | Set-Content (Join-Path $root 'artifacts/dependency-setup-error.txt')
 throw
}
