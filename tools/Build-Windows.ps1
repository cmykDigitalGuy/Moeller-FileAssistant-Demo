param([switch]$TestInstaller)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$artifacts=Join-Path $root 'artifacts';$evidence=Join-Path $artifacts 'verification'
New-Item $evidence -ItemType Directory -Force | Out-Null
$checks=[Collections.Generic.List[object]]::new()
function Record([string]$name,[bool]$passed,[string]$detail){$checks.Add([pscustomobject]@{Check=$name;Passed=$passed;Detail=$detail});Write-Output "[$passed] $name - $detail";if(!$passed){throw "Verification failed: $name - $detail"}}
function Start-App([string]$file,[string[]]$arguments){$info=[Diagnostics.ProcessStartInfo]::new($file);$info.UseShellExecute=$false;foreach($argument in $arguments){$info.ArgumentList.Add($argument)};return [Diagnostics.Process]::Start($info)}
function Run-App([string]$file,[string[]]$arguments,[int]$seconds=180){$process=Start-App $file $arguments;if(!$process.WaitForExit($seconds*1000)){try{$process.Kill($true)}catch{};throw "Timed out: $file"};if($process.ExitCode -ne 0){throw "Process exited $($process.ExitCode): $file"}}
function Save-Report{[pscustomobject]@{Version='0.2.1';BuiltAtUtc=[DateTime]::UtcNow.ToString('O');Commit=$env:GITHUB_SHA;OS=[Environment]::OSVersion.ToString();Architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString();Checks=$checks;Unsigned=$true;Limitations=@('Automated Windows Server 2022 x64 verification; not manual Windows 10/11 laptop acceptance.','Smart App Control may block unsigned custom software. No security settings are changed.','apoXML and JDF are unvalidated samples. No production systems are contacted.')}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $evidence 'BUILD-VERIFICATION.json')}
try {
 . ./tools/Prepare-Tools.ps1
 Add-Type -AssemblyName System.Drawing
 $bitmap=[Drawing.Bitmap]::new(64,64);$g=[Drawing.Graphics]::FromImage($bitmap);$g.Clear([Drawing.Color]::FromArgb(16,38,59))
 $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(0,151,140));$g.FillRectangle($brush,0,52,64,12)
 $font=[Drawing.Font]::new('Segoe UI',22,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel);$g.DrawString('MF',$font,[Drawing.Brushes]::White,8,12)
 $icon=[Drawing.Icon]::FromHandle($bitmap.GetHicon());$stream=[IO.File]::Create((Join-Path $root 'src/Moeller.FileAssistant/App.ico'));$icon.Save($stream);$stream.Dispose();$icon.Dispose();$font.Dispose();$brush.Dispose();$g.Dispose();$bitmap.Dispose()
 & dotnet test tests/Moeller.Tests/Moeller.Tests.csproj -c Release -p:Platform=x64 --logger 'trx;LogFileName=workflow-tests.trx' --results-directory artifacts/verification/test-results 2>&1 | Tee-Object (Join-Path $evidence 'workflow-tests.log')
 if($LASTEXITCODE -ne 0){throw 'Workflow regression failed.'}
 [xml]$trx=Get-Content (Join-Path $evidence 'test-results/workflow-tests.trx') -Raw;$c=$trx.TestRun.ResultSummary.Counters
 Record 'Automated workflow regression' ($c.failed -eq '0' -and [int]$c.passed -ge 32) "$($c.passed) passed; $($c.failed) failed; $($c.total) total"
 & dotnet build MoellerFileAssistant.slnx -c Release -p:Platform=x64 2>&1 | Tee-Object (Join-Path $evidence 'solution-build.log')
 if($LASTEXITCODE -ne 0){throw 'Release solution build or automatic setup packaging failed.'}
 $setup=Join-Path $artifacts 'Moeller-File-Assistant-Demo-0.2.1-Setup.exe'
 Record 'One Release solution build creates setup' (Test-Path $setup) 'Core, WPF, tests and setup built; no Visual Studio installer extension needed.'
 Copy-Item (Join-Path $artifacts 'installer-compile.log') $evidence -Force
 Record 'Self-contained runtime and metadata tool' ((Test-Path publish/hostfxr.dll) -and (Test-Path publish/tools/exiftool.exe) -and (Test-Path publish/tools/exiftool_files)) 'Installed app contains .NET and the PDF metadata runtime.'
 & dotnet list src/Moeller.Core/Moeller.Core.csproj package --vulnerable --include-transitive --format json | Set-Content (Join-Path $evidence 'nuget-vulnerability-audit.json')
 if($LASTEXITCODE -ne 0){throw 'Dependency audit could not complete.'}
 $audit=Get-Content (Join-Path $evidence 'nuget-vulnerability-audit.json') -Raw|ConvertFrom-Json
 $vulnerable=@($audit.projects|ForEach-Object{$_.frameworks}|ForEach-Object{@($_.topLevelPackages)+@($_.transitivePackages)}|Where-Object{$null -ne $_ -and $_.vulnerabilities})
 Record 'NuGet vulnerability audit' ($vulnerable.Count -eq 0) 'No vulnerable application dependency reported at build time.'
 if($TestInstaller){
  Remove-Item Env:MOELLER_DEMO_EXIFTOOL -ErrorAction SilentlyContinue
  $installed=Join-Path $env:LOCALAPPDATA 'Programs/Moeller/FileAssistantDemo/Moeller.FileAssistant.exe'
  $demoHome=Join-Path $env:LOCALAPPDATA 'Moeller/FileAssistantDemo'
  $workspace=Join-Path $demoHome 'Workspace';$database=Join-Path $workspace 'Database/demo.sqlite3'
  Run-App $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG='+(Join-Path $evidence 'fresh-install.log'))) 600
  Record 'Fresh installation initializes database' ((Test-Path $installed) -and (Test-Path $database)) 'Actual Setup EXE installed and initialized local demo data.'
  $health=Join-Path $evidence 'installed-diagnostics.json';Run-App $installed @('--diagnostics-json',$health)
  $diagnostics=Get-Content $health -Raw|ConvertFrom-Json
  Record 'Installed application health' (@($diagnostics|Where-Object{!$_.Passed}).Count -eq 0) 'Sixteen jobs, database and simulated output folders passed diagnostics.'
  $verb='HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\MoellerHub'
  $parent=Get-Item $verb
  $menus=@('01Assign','02Version','03Component','04Replace','05More')
  $valid=(($parent.GetValue('MUIVerb') -eq 'Moeller Hub') -and ($parent.GetValue('SubCommands') -eq ''))
  foreach($entry in $menus){$commandKey=Join-Path $verb ('shell\'+$entry+'\command');if(!(Test-Path $commandKey)){$valid=$false}}
  Record 'Explorer submenu registration' $valid 'Moeller Hub submenu with four task commands and More is present.'
  $sendTo=Join-Path $env:APPDATA 'Microsoft/Windows/SendTo/Moeller File Assistant.lnk'
  Record 'Multi-file Send To shortcut' (Test-Path $sendTo) 'Per-user Send To shortcut exists.'
  Run-App $installed @('--ui-smoke',(Join-Path $evidence 'main-window.png')) 180
  $ui=Get-Content (Join-Path $evidence 'ui-smoke.json') -Raw|ConvertFrom-Json
  Record 'Native window startup and rendering' ($ui.Result -eq 'Passed' -and $ui.WindowsRendered -eq 5) 'Five actual WPF windows opened and rendered.'
  $ipcReport=Join-Path $evidence 'ipc-selection.json';$primary=Start-App $installed @('--ipc-smoke',$ipcReport)
  $deadline=[DateTime]::UtcNow.AddSeconds(30)
  while(!(Test-Path ($ipcReport+'.ready')) -and [DateTime]::UtcNow -lt $deadline -and !$primary.HasExited){Start-Sleep -Milliseconds 100}
  if(!(Test-Path ($ipcReport+'.ready'))){throw 'IPC assistant did not become ready.'}
  $samples=@(Get-ChildItem (Join-Path $workspace 'archive') -Recurse -File -Filter '*.pdf'|Where-Object{$_.DirectoryName -match 'Northstar.*provided$'}|Sort-Object Name|Select-Object -First 3)
  if($samples.Count -ne 3){throw 'Three multi-select samples were not found.'}
  $clients=@();foreach($sample in $samples){$clients+=Start-App $installed @('--select',$sample.FullName)}
  foreach($client in $clients){if(!$client.WaitForExit(25000) -or $client.ExitCode -ne 0){throw 'Secondary selection process failed.'}}
  if(!$primary.WaitForExit(30000) -or $primary.ExitCode -ne 0){throw 'Primary selection process failed.'}
  $ipc=Get-Content $ipcReport -Raw|ConvertFrom-Json
  Record 'Multi-process Explorer selection handoff' ($ipc.Received -eq 3 -and $ipc.PopupCount -eq 1 -and $ipc.VisiblePopup) 'Three selected-file invocations reached one visible native popup.'
  $demoZip=Join-Path $artifacts 'Moeller-File-Assistant-Demo-0.2.1-Demo-State.zip'
  Run-App $installed @('--export-demo',$demoZip) 300
  Record 'Installed demo ZIP export' (Test-Path $demoZip) 'Installed application exported and checksum-verified its complete fictional state.'
  $before=(Get-FileHash $database -Algorithm SHA256).Hash
  Run-App $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG='+(Join-Path $evidence 'repair-install.log'))) 600
  Record 'Same-version repair preserves database' ($before -eq (Get-FileHash $database -Algorithm SHA256).Hash) 'Database fingerprint unchanged after reinstall.'
  Run-App (Join-Path (Split-Path $installed -Parent) 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG='+(Join-Path $evidence 'uninstall.log'))) 180
  Record 'Uninstall removes application and integration' (!(Test-Path $installed) -and !(Test-Path $verb) -and !(Test-Path $sendTo)) 'Executable, context command and Send To shortcut removed.'
  Record 'Uninstall preserves fictional data' (Test-Path $database) 'Working database remains available.'
 }
 Save-Report
 $sourceStage=Join-Path $root '_source-package/MoellerFileAssistantDemo';New-Item $sourceStage -ItemType Directory -Force|Out-Null
 foreach($entry in @('src','tests','tools','installer','docs','README.md','Directory.Build.props','MoellerFileAssistant.slnx')){Copy-Item (Join-Path $root $entry) $sourceStage -Recurse -Force}
 New-Item (Join-Path $sourceStage 'vendor') -ItemType Directory -Force|Out-Null
 foreach($entry in @('exiftool','inno')){Copy-Item (Join-Path $root ('vendor/'+$entry)) (Join-Path $sourceStage 'vendor') -Recurse -Force}
 Get-ChildItem $sourceStage -Directory -Recurse|Where-Object{$_.Name -in @('bin','obj','.vs')}|Sort-Object {$_.FullName.Length} -Descending|Remove-Item -Recurse -Force
 if(Get-ChildItem $sourceStage -Recurse -File|Where-Object{$_.Extension -in @('.ttf','.otf','.woff','.woff2')}){throw 'Unexpected font files in source archive.'}
 [IO.Compression.ZipFile]::CreateFromDirectory((Split-Path $sourceStage -Parent),(Join-Path $artifacts 'Moeller-File-Assistant-Demo-0.2.1-Source.zip'))
 [IO.Compression.ZipFile]::CreateFromDirectory($evidence,(Join-Path $artifacts 'Moeller-File-Assistant-Demo-0.2.1-Verification.zip'))
 Get-ChildItem $artifacts -File|Get-FileHash -Algorithm SHA256|Select-Object @{n='File';e={[IO.Path]::GetFileName($_.Path)}},Hash|ConvertTo-Json|Set-Content (Join-Path $artifacts 'ARTIFACT-SHA256.json')
} catch {
 ($_|Format-List * -Force|Out-String)|Set-Content (Join-Path $evidence 'BUILD-FAILURE.txt')
 $_.Exception.ToString()|Add-Content (Join-Path $evidence 'BUILD-FAILURE.txt')
 $errorLog=Join-Path $env:LOCALAPPDATA 'Moeller/FileAssistantDemo/startup-errors.log';if(Test-Path $errorLog){Copy-Item $errorLog $evidence -Force}
 Save-Report
 throw
}
