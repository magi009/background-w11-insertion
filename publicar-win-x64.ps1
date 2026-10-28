$ErrorActionPreference = 'Stop'
dotnet publish "$PSScriptRoot\src\FondoClaro.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
Write-Host "Listo. Busca FondoClaro.exe dentro de src\bin\Release\net8.0-windows\win-x64\publish\"
