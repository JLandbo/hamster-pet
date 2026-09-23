$publish = Join-Path $PSScriptRoot "publish"
dotnet publish "$PSScriptRoot\src\Hamster" -c Release -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish fejlede (kører Hamster.exe fra publish-mappen? Luk den først)." }

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut("$([Environment]::GetFolderPath('Startup'))\Hamster.lnk")
$shortcut.TargetPath = Join-Path $publish "Hamster.exe"
$shortcut.WorkingDirectory = $publish
$shortcut.Save()
Write-Host "Autostart oprettet: $($shortcut.FullName)"
