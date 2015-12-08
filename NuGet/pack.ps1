$root = (split-path -parent $MyInvocation.MyCommand.Definition) + '\..'
$version = [System.Reflection.Assembly]::LoadFile("$root\SOCVR.Net\bin\Release\SOCVR.Net.dll").GetName().Version
$versionStr = "{0}.{1}.{2}-rc5" -f ($version.Major, $version.Minor, $version.Revision)

Write-Host "Setting .nuspec version tag to $versionStr"

$content = (Get-Content $root\nuget\SOCVR.Net.nuspec) 
$content = $content -replace '\$version\$',$versionStr

$content | Out-File $root\nuget\SOCVR.Net.compiled.nuspec

& $root\nuget\NuGet.exe pack $root\nuget\SOCVR.Net.compiled.nuspec