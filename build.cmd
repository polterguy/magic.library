
set version=%1
set key=%2

cd %~dp0
dotnet build magic.library/magic.library.csproj --configuration Release --source https://api.nuget.org/v3/index.json
dotnet nuget push magic.library/bin/Release/magic.library.%version%.nupkg -k %key% -s https://api.nuget.org/v3/index.json
