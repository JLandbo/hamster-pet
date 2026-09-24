$publish = Join-Path $PSScriptRoot "publish"
$exe = Join-Path $publish "Hamster.exe"
Get-Process Hamster -ErrorAction SilentlyContinue | Where-Object Path -eq $exe | Stop-Process -Force
dotnet publish "$PSScriptRoot\src\Hamster" -c Release -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish fejlede." }

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut("$([Environment]::GetFolderPath('Startup'))\Hamster.lnk")
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $publish
$shortcut.Save()
Write-Host "Autostart oprettet: $($shortcut.FullName)"
explorer.exe $exe
