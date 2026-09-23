$publish = Join-Path $PSScriptRoot "publish"
$exe = Join-Path $publish "Hamster.exe"
# The running pet locks its files, so publishing over it would fail.
Get-Process Hamster -ErrorAction SilentlyContinue | Where-Object Path -eq $exe | Stop-Process -Force
dotnet publish "$PSScriptRoot\src\Hamster" -c Release -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish fejlede." }

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut("$([Environment]::GetFolderPath('Startup'))\Hamster.lnk")
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $publish
$shortcut.Save()
Write-Host "Autostart oprettet: $($shortcut.FullName)"
# Through Explorer, so it gets the same clean environment as at logon instead of this shell's.
explorer.exe $exe
