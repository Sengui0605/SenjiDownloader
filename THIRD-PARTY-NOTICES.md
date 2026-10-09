# Componentes distribuidos

SenjiDownloader es una aplicación C# independiente. Los motores se ejecutan como
procesos separados únicamente cuando son necesarios. No se instala ni se necesita
un intérprete Python en la computadora; el ejecutable oficial de yt-dlp contiene
su propio runtime y sus scripts de extracción.

| Componente | Licencia / origen | Código fuente |
| --- | --- | --- |
| .NET / Windows Forms | MIT, con avisos de componentes de terceros | https://github.com/dotnet/runtime y https://github.com/dotnet/winforms |
| yt-dlp y componentes del ejecutable oficial | El código fuente es Unlicense; el ejecutable oficial combinado es GPL v3+ | https://github.com/yt-dlp/yt-dlp y https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt |
| FFmpeg essentials de Gyan | GPL v3, compilación estática con bibliotecas incluidas | https://www.gyan.dev/ffmpeg/builds/ y https://ffmpeg.org/download.html |
| Deno | MIT, con dependencias de terceros | https://github.com/denoland/deno |

El ejecutable lleva los textos de licencia en su paquete integrado y los extrae
a `tools/licenses`; el ZIP también los incluye en `licenses`. Las versiones y la
configuración de FFmpeg aparecen en `tools/versions.json` y en `ffmpeg -version`.
Los motores mantienen sus licencias originales. El icono y la interfaz se crean
específicamente para SenjiDownloader. No se distribuyen el código Python original,
el entorno virtual ni los datos del usuario.
