param([switch]$NoPublish)
$ErrorActionPreference='Stop'
# A Windows PowerShell child of PowerShell 7 must not inherit Core-only modules.
# This changes only the build process environment, never Windows security policy.
if($PSVersionTable.PSEdition -eq 'Desktop') {
 $env:PSModulePath=([Environment]::GetFolderPath('MyDocuments')+'\WindowsPowerShell\Modules;'+$env:ProgramFiles+'\WindowsPowerShell\Modules;'+$PSHOME+'\Modules')
}
function File-SHA256([string]$path) {
 $stream=[IO.File]::OpenRead($path);$algorithm=[Security.Cryptography.SHA256]::Create()
 try{return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','')}finally{$algorithm.Dispose();$stream.Dispose()}
}
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 . ./tools/Prepare-Tools.ps1
 $compiler=Join-Path $root 'vendor/inno/ISCC.exe'
 if(!(Test-Path $compiler)) {
  $bootstrap=Join-Path $root 'vendor/innosetup-6.4.3.exe'
  $expected='F3C42116542C4CC57263C5BA6C4FEABFC49FE771F2F98A79D2F7628B8762723B'
  if(!(Test-Path $bootstrap)) {
   & curl.exe --fail --location --retry 2 --max-time 180 --output $bootstrap 'https://github.com/jrsoftware/issrc/releases/download/is-6_4_3/innosetup-6.4.3.exe'
   if($LASTEXITCODE -ne 0){throw 'Could not download the official build-time installer compiler.'}
  }
  if((File-SHA256 $bootstrap) -ne $expected){throw 'Installer compiler checksum mismatch. Nothing was executed.'}
  $compilerDirectory=Join-Path $root 'vendor/inno'
  $arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /TASKS= /DIR="'+$compilerDirectory+'"'
  $process=Start-Process -FilePath $bootstrap -ArgumentList $arguments -Wait -PassThru
  if($process.ExitCode -ne 0 -or !(Test-Path $compiler)){throw 'Build-time compiler preparation failed.'}
 }
 if(!$NoPublish) {
  & dotnet publish src/Moeller.FileAssistant/Moeller.FileAssistant.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:SkipInstallerBuild=true -p:PublishTrimmed=false -p:PublishSingleFile=false -o publish
  if($LASTEXITCODE -ne 0){throw 'Self-contained application publishing failed.'}
 }
 $publish=Join-Path $root 'publish'
 if(!(Test-Path (Join-Path $publish 'Moeller.FileAssistant.exe'))){throw 'Published application is missing.'}
 New-Item (Join-Path $publish 'tools') -ItemType Directory -Force | Out-Null
 Get-ChildItem (Join-Path $root 'vendor/exiftool') | Copy-Item -Destination (Join-Path $publish 'tools') -Recurse -Force
 Copy-Item README.md (Join-Path $publish 'README.md') -Force
 Copy-Item docs $publish -Recurse -Force
 $licenses=Join-Path $publish 'licenses';New-Item $licenses -ItemType Directory -Force | Out-Null
 Copy-Item (Join-Path (Split-Path $compiler -Parent) 'license.txt') (Join-Path $licenses 'Inno-Setup-6.4.3-license.txt') -Force
 $nuget=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
 foreach($package in @('pdfsharp','microsoft.data.sqlite','microsoft.data.sqlite.core','sqlitepclraw.core','sqlitepclraw.bundle_e_sqlite3','sqlitepclraw.lib.e_sqlite3','system.security.cryptography.pkcs','microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
  $folder=Join-Path $nuget $package
  if(Test-Path $folder){Get-ChildItem $folder -Recurse -File|Where-Object{$_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD-PARTY|THIRDPARTY)' -and $_.Extension -notin @('.dll','.exe')}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenses ($package+'-'+$_.Name)) -Force}}
 }
 if(Get-ChildItem $publish -Recurse -File | Where-Object {$_.Extension -in @('.ttf','.otf','.woff','.woff2')}){throw 'Unexpected font binaries. Packaging stopped.'}
 $artifacts=Join-Path $root 'artifacts';New-Item $artifacts -ItemType Directory -Force | Out-Null
 & $compiler installer/MoellerFileAssistant.iss 2>&1 | Out-File (Join-Path $artifacts 'installer-compile.log') -Encoding utf8
 if($LASTEXITCODE -ne 0){Get-Content (Join-Path $artifacts 'installer-compile.log') -Tail 20;throw 'Installer compilation failed.'}
 $setup=Join-Path $artifacts 'Moeller-File-Assistant-Demo-0.2.0-Setup.exe'
 if(!(Test-Path $setup)){throw 'Compiled setup file is missing.'}
 $stage=Join-Path $root '_installer-package';New-Item $stage -ItemType Directory -Force | Out-Null
 Copy-Item $setup $stage -Force
 Copy-Item docs/SETUP-NOTES.txt (Join-Path $stage 'START-HERE.txt') -Force
 Copy-Item $licenses $stage -Recurse -Force
 ((File-SHA256 $setup)+'  '+[IO.Path]::GetFileName($setup)) | Set-Content (Join-Path $stage 'SHA256.txt')
 $zip=Join-Path $artifacts 'Moeller-File-Assistant-Demo-0.2.0-Installer.zip'
 if(Test-Path $zip){Remove-Item $zip -Force}
 [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip)
 Write-Output "SETUP READY: $setup"
 Write-Output "INSTALLER ZIP: $zip"
} finally {Pop-Location}
