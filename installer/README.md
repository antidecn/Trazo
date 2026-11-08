# Instalador de Trazo

Instalador MSI hecho con WiX Toolset 5. El asistente tiene el mismo aspecto que Trazo: fondo oscuro, texto claro, botones grises redondeados y el logo de la aplicación.

```powershell
dotnet build installer\Trazo.Installer.wixproj -c Release
```

Genera `installer\bin\x64\Release\Trazo-<versión>.msi`. Antes de empaquetar, publica la aplicación y genera las imágenes del asistente.

## Archivos

| Ruta | Contenido |
|---|---|
| `Package.wxs` | Paquete (archivos, accesos, registro, actualizaciones) y todos los diálogos del asistente |
| `Trazo.Installer.wixproj` | Proyecto: publica Trazo, genera las imágenes y compila el MSI |
| `art/` | Generador de las imágenes del asistente (C#, .NET 10) |
| `titlebar/` | Programa pequeño que pone la barra de título del asistente con los colores de Trazo (C#, .NET Framework 4.8) |

## Cómo se consigue el aspecto de Trazo

Windows Installer no deja cambiar el color de sus controles, así que el asistente no usa los diálogos estándar de WixUI, sino diálogos propios:

- **Fondo:** una imagen ocupa todo el diálogo (oscuro, con la franja de los botones abajo). Los textos van encima, transparentes, con los colores de Trazo (`TextStyle`).
- **Botones:** son botones con imagen. Las imágenes copian los botones de la configuración de Trazo: el principal en gris claro y los demás en gris oscuro con borde.
- **Opciones y casillas:** son pares de botones con imagen, uno elegido y otro sin elegir. Se muestran u ocultan según la propiedad y la cambian al pulsarlos. Así son el tipo de instalación, "Avisarme cuando haya una versión nueva", "Abrir Trazo" y reparar/desinstalar.
- **Barra de título:** del mismo color que el fondo, con el texto claro y el icono de Trazo (también en la barra de tareas). Ver más abajo.
- **Controles de Windows:** la barra de progreso, el campo de la carpeta, el explorador de carpetas y la lista de programas abiertos no se pueden recolorear.

No hay pantalla de "Preparando…": el asistente aparece directamente con la pantalla que toca (instalar, actualizar o reparar/desinstalar).

Antes del asistente, el propio `msiexec` enseña un instante su ventanita "Windows Installer — Preparando la instalación…", mientras abre el paquete. No forma parte del MSI y no se puede quitar desde aquí:

- **Al actualizar desde Trazo** ("Buscar actualizaciones…") no se ve. `InstallerLauncher.cs` lanza `msiexec` en modo oculto y además oculta esa ventanita si llegara a crearse; Trazo se cierra cuando ya ha salido el asistente.
- **Al abrir el `.msi` a mano** (doble clic), Windows la sigue mostrando como un cuarto de segundo.

### Barra de título

Windows Installer no deja cambiar la barra de título de sus diálogos, pero Windows sí permite cambiar sus colores desde otro programa (`DwmSetWindowAttribute`). Lo hace `titlebar\`, que va dentro del MSI:

1. Al abrirse el asistente, antes de la primera pantalla, Windows Installer lo ejecuta y espera a que termine.
2. Se copia a `%TEMP%\TrazoTitleBar\` y arranca esa copia. Windows Installer borra su archivo temporal al acabar la acción, así que no puede seguir en marcha desde ahí.
3. La copia vigila las ventanas nuevas de Windows Installer y les pone los colores y el icono antes de que se vean. Avisa en cuanto está lista, así la primera pantalla ya sale oscura.
4. Termina sola cuando se cierra el instalador. La copia (unos 60 KB) se borra la próxima vez que se abra un instalador de Trazo.

En Windows 11 la barra queda exactamente del color del fondo. En Windows 10 solo se pone en modo oscuro. Si algo fallara, el asistente funciona igual con la barra normal. En las instalaciones silenciosas (`/qn`) no hay ventanas y no se ejecuta.

Las imágenes de los botones no se pueden escalar sin que se vea el marco nativo alrededor, así que se generan a su tamaño exacto para cada escala de pantalla habitual: 100, 125, 150, 175 y 200 %. Al arrancar, la propiedad `TRAZOUISCALE` toma la altura del texto del sistema (`TextHeight`, 16 px al 100 %) redondeada a 16, 20, 24, 28 o 32. Cada control elige su imagen por nombre, por ejemplo `Text="TzBtnNext[TRAZOUISCALE]"`.

Pantallas: bienvenida, tipo de instalación, carpeta, avisos de actualizaciones, listo, progreso y fin. Para actualizar se salta el tipo de instalación y la carpeta. Si Trazo ya está instalado y se abre el MSI, pregunta si reparar o desinstalar. Hay también diálogos para cancelar, errores, archivos en uso y explorador de carpetas.

Si se desinstala desde **Configuración de Windows → Aplicaciones**, Windows usa su propia ventanita ("¿Desea desinstalar…?") con una barra de progreso básica, y eso no se puede personalizar. La pregunta de Trazo sobre borrar la configuración sí sale con su estilo.

## Regenerar las imágenes

No hace falta hacer nada: al compilar el instalador, `Trazo.Installer.wixproj` ejecuta el generador si ha cambiado algo en `art\` o en `assets\Trazo-simbolo.ai` (y compila `titlebar\` en `installer\obj\titlebar\` si ha cambiado). Deja las imágenes en `installer\obj\art\`, junto con `TrazoArt.wxs`, que las mete en el MSI. Nada de esto se sube al repositorio.

Para generarlas a mano, por ejemplo para revisarlas:

```powershell
dotnet run --project installer\art -c Release -- installer\obj\art
```

El argumento es la carpeta de salida. Hace falta el SDK de .NET 10.

Qué cambiar y dónde:

- **Colores, formas o fuentes de las imágenes:** clase `Art` en `art\Program.cs`. Los colores son los de `SettingsForm.cs`.
- **Texto de un botón, opción o casilla:** lista de piezas al principio de `art\Program.cs`. Ese texto va dentro de la imagen.
- **Textos del asistente** (títulos, explicaciones): `Package.wxs`.
- **Tamaño o posición de un control con imagen:** cámbialo a la vez en `Package.wxs` (`Width`/`Height` en unidades de diálogo) y en `art\Program.cs` (los mismos números). Si no coinciden, la imagen sale recortada o con el marco de Windows alrededor.
- **Logo:** sale de `assets\Trazo-simbolo.ai`, igual que los iconos de la aplicación.

Las imágenes se guardan como BMP de 8 bits con compresión RLE8. Son casi todo superficies planas y ocupan poco: unos 700 KB para las 165 imágenes (33 piezas en cinco escalas).

### Detalles de Windows Installer a tener en cuenta

- El fondo de cada diálogo lleva `TabSkip="no"`. Así entra en el orden de creación antes que los botones y no los tapa.
- El texto normal usa el gris 230 y no el 240 de la aplicación. Windows Installer pinta en negro los colores parecidos al fondo de los diálogos de Windows (240).
- En el diálogo de error, `ErrorText` tiene que ser el primer control.
