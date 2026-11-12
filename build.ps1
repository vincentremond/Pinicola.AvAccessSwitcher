$ErrorActionPreference = "Stop"

dotnet tool restore
dotnet build --configuration Release
