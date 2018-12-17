param (
    [string]$WorkPath,
    [string]$CSVPath,
    [string]$TFM='net471'
)

# Local Build Provisioning Script
# Developed by Den Delimarsky (dendeli@microsoft.com)
# Last revision: December 22, 2017

if ([string]::IsNullOrWhiteSpace($WorkPath) -or [string]::IsNullOrWhiteSpace($CSVPath))
{
    Write-Error 'No suitable paths were specified.'
    exit
}

# If there is no proper CSV, we need to exit.
If (-not (Test-Path $CSVPath)) {
    Write-Error 'The specified package CSV does not exist.'
    exit
}

# Create the specified folders, if necessary.
if (-not (Test-Path $WorkPath))
{
    Write-Output 'Creating the work directory...'
    New-Item $WorkPath -Type directory -Force
}

## Provisioning URLs
$popImportUrl = "https://bindrop.blob.core.windows.net/tools/popimport.zip"
$nueUrl = "https://bindrop.blob.core.windows.net/tools/NuePackage.zip"
$mdocUrl = "https://github.com/mono/api-doc-tools/releases/download/mdoc-5.4/mdoc-5.4.zip"
$tripledUrl = "https://bindrop.blob.core.windows.net/tools/tripled.zip"
$nugetUrl = "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe"

# Folder provisioning

# Folder for binaries
$outputBinaries = [io.path]::combine($WorkPath, 'binaries')
New-Item $outputBinaries -Type directory -Force

# Folder for mdoc-generated documentation
$documentationPath = [io.path]::combine($WorkPath, 'documentation')
New-Item $documentationPath -Type Directory -Force

# Folder for tool downloads
$folderName = [io.path]::combine($WorkPath, '_tool_dl')
New-Item $folderName -Type Directory -Force

# Folder for extracted tools
$folderName = [io.path]::combine($WorkPath, '_tool_bin')
New-Item $folderName -Type Directory -Force

# Output URLs
$popImportOutput = [io.path]::combine($WorkPath, '_tool_dl', 'popimport.zip')
$nueOutput = [io.path]::combine($WorkPath, '_tool_dl','nue.zip')
$mdocOutput = [io.path]::combine($WorkPath, '_tool_dl','mdoc.zip')
$tripledOutput = [io.path]::combine($WorkPath, '_tool_dl','tripled.zip')
$nugetOutput = [io.path]::combine($WorkPath, '_tool_bin','nuget.exe')

# Download Triggers
Write-Output 'Downloading tools...'
# Force TLS 1.2 (for GitHub in particular)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $popImportUrl -OutFile $popImportOutput
Invoke-WebRequest -Uri $nueUrl -OutFile $nueOutput
Invoke-WebRequest -Uri $mdocUrl -OutFile $mdocOutput
Invoke-WebRequest -Uri $tripledUrl -OutFile $tripledOutput
Invoke-WebRequest -Uri $nugetUrl -OutFile $nugetOutput -Verbose

# Extract tools
Write-Output 'Extracting tools...'
$toolBinPath = [io.path]::combine($WorkPath, '_tool_bin')
Add-Type -assembly "system.io.compression.filesystem"
[io.compression.zipfile]::ExtractToDirectory($popImportOutput, $toolBinPath)
[io.compression.zipfile]::ExtractToDirectory($nueOutput, $toolBinPath)
[io.compression.zipfile]::ExtractToDirectory($mdocOutput, $toolBinPath)
[io.compression.zipfile]::ExtractToDirectory($tripledOutput, $toolBinPath)

Write-Output 'Getting packages...'
$nuePath = [io.path]::combine($toolBinPath,'a\Nue\Nue\bin\Release\Nue.exe')
& $nuePath -m extract -p $CSVPath -o $outputBinaries -f $TFM

$mdocPath = [io.path]::combine($toolBinPath,'mdoc.exe')
& $mdocPath fx-bootstrap $outputBinaries

$popImportPath = [io.path]::combine($toolBinPath,'pi\popimport.exe')
& $popImportPath -f $outputBinaries

Write-Output 'Binaries: '$outputBinaries
Write-Output 'Documentation: '$documentationPath

cd $toolBinPath
& $mdocPath update -fx $outputBinaries -o $documentationPath -lang docid -lang vb.net -lang fsharp -lang javascript --debug

$tripledPath = [io.path]::combine($toolBinPath,'tripled\tripled.exe')
& $tripledPath -x $documentationPath -l true
