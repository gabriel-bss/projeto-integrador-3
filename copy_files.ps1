$src = "c:\Users\guilh\OneDrive\Área de Trabalho\Projeto-Integrador-PI2-main\Projeto-Integrador-PI2-main"
$dst = "c:\Users\guilh\OneDrive\Área de Trabalho\Projeto-Integrador-PI2-main\local"
if(!(Test-Path $dst)){New-Item -ItemType Directory -Path $dst | Out-Null}
Copy-Item -Path "$src\*" -Destination $dst -Recurse -Force
Write-Host "Copy done"
