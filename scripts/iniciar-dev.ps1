# Execute no PowerShell, a partir da pasta SistemaManutencaoPIM.
# Requer .NET 8 SDK e SQL Server LocalDB (ou connection string modificada).
$ErrorActionPreference = 'Stop'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5088'
if (-not $env:Jwt__Key) {
    $dados = New-Object 'Byte[]' 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($dados) } finally { $rng.Dispose() }
    $env:Jwt__Key = [Convert]::ToBase64String($dados)
}
if (-not $env:SeedAdmin__Email) { $env:SeedAdmin__Email = 'admin@pim.local' }
if (-not $env:SeedAdmin__Senha) {
    $env:SeedAdmin__Senha = Read-Host 'Defina uma senha de admin (minimo 8 caracteres)'
}
Write-Host 'Swagger em http://localhost:5088/swagger' -ForegroundColor Green
# Opcional: se LocalDB nao estiver instalado, informe antes de iniciar:
# $env:ConnectionStrings__DefaultConnection = 'Server=localhost;Database=SistemaManutencaoPIM;User Id=sa;Password=SUA_SENHA;TrustServerCertificate=True'
dotnet run --project (Join-Path $PSScriptRoot '..\SistemaManutencao.API\SistemaManutencao.API.csproj') --launch-profile SistemaManutencao.API
