# SenjiDownloader

Aplicación nativa de escritorio para Windows 10/11 x64, escrita en C# y .NET 10.
Interfaz oscura en español, descargas simultáneas y conversión de archivos.

## Usar

Descarga `SenjiDownloader.exe` desde [Releases](https://github.com/Sengui0605/SenjiDownloader/releases) y ábrelo. También puedes descargar el ZIP con las instrucciones y licencias.
El ejecutable incluye su runtime, yt-dlp, FFmpeg, ffprobe y Deno. Puedes copiar solamente el `.exe` a una máquina nueva; no hace falta instalar dependencias.
La interfaz aparece primero. Los componentes se preparan en segundo plano y la primera descarga espera a que estén listos. Las siguientes aperturas reutilizan la carpeta `tools`.

- Hasta seis descargas o conversiones en paralelo, con cola persistente.
- Varios enlaces pegados o importados desde un archivo TXT arrastrado a la ventana.
- MP4, MKV o MP3; calidad de hasta 2160p según la fuente.
- Conversión por lotes a MP3, WAV o MP4 sin modificar el original.
- Pausa, reanudación, cancelación, reintentos y detalles de los errores.
- Bandeja del sistema, acceso directo e inicio con Windows opcionales por equipo.
- Una sola instancia: el acceso directo vuelve a abrir la app que ya está en la bandeja.
- Actualizaciones de la app, yt-dlp y Deno en segundo plano, con comprobación SHA-256.

La interfaz aparece antes de la integración con Windows y de cualquier consulta de red.
El motor de descarga se ejecuta únicamente al usarlo. La interfaz, la cola y el
actualizador no usan Python. El ejecutable oficial de yt-dlp incluye internamente
su runtime; no necesita que el usuario instale Python.

Al cerrar se conservan los archivos parciales y el historial; los trabajos que
estaban activos aparecen pausados al reabrir. Las conversiones se reinician al
reanudar, mientras que las descargas pueden continuar desde un archivo parcial.
Para cerrar completamente la app usa `Salir por completo` en su bandeja.

## Compilar

Solo para desarrollar: instala el SDK .NET 10 y ejecuta en PowerShell:

```powershell
./scripts/Build.ps1
```

La compilación produce `dist/SenjiDownloader/` y los archivos publicables en
`dist/release/`. Los componentes descargados se verifican con sus checksums oficiales.
Los datos y preferencias de desarrollo se excluyen del ZIP.

## Verificar

```powershell
$app = './dist/SenjiDownloader/SenjiDownloader.exe'
Start-Process $app -ArgumentList '--self-test', "$PWD/integration.json" -Wait
Get-Content integration.json
Start-Process $app -ArgumentList '--ui-self-test', "$PWD/ui.json" -Wait
Get-Content ui.json
```

La prueba genera un video sintético, sirve tres archivos por HTTP local, verifica
descargas realmente simultáneas, pausa/reanudación, el hash de cada archivo, tres
conversiones reproducibles, cancelación, errores HTTP y restauración del historial.
No requiere descargar videos ajenos ni servicios externos.
La prueba de interfaz abre y cierra los cinco selectores, elige cada opción varias veces,
comprueba los eventos, navega entre páginas y redimensiona la ventana. El workflow
también copia únicamente el EXE a una carpeta vacía y repite las descargas y conversiones.

## Publicar actualizaciones

Incrementa `<Version>` en `SenjiDownloader/SenjiDownloader.csproj` y sube el cambio
a `main`. GitHub Actions compila, ejecuta las pruebas y publica una nueva Release.
Las versiones ya publicadas son inmutables; el workflow no reemplaza sus binarios.
La app comprueba el canal `Sengui0605/SenjiDownloader` al arrancar y vuelve a intentarlo
en segundo plano. Aplica las actualizaciones al quedar libre la cola, guarda una
copia anterior y comprueba que la nueva versión inicie antes de darla por aplicada.

`data/` contiene configuración, cola y registro. La carpeta completa puede moverse
entre equipos; cada equipo necesita una primera ejecución para crear su acceso
directo y registrar el inicio con Windows. Una carpeta de solo lectura utiliza
`%LOCALAPPDATA%/SenjiDownloader` para los datos; la actualización necesita poder
escribir junto al ejecutable.

Los binarios de terceros y sus fuentes se describen en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
