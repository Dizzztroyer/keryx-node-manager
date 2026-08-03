# Keryx Node Manager

*Leer en otros idiomas: [English](README.md) · [Русский](README.ru.md) · [Français](README.fr.md) · [Deutsch](README.de.md)*

Una aplicación de Windows para gestionar un nodo Keryx y un minero GPU desde una sola ventana —
sin necesidad de trabajo manual en PowerShell/WSL/Docker. Herramienta de la comunidad, no un
producto oficial de Keryx Labs.

## Descargar e instalar

Ve a la página de **[Releases](https://github.com/Dizzztroyer/keryx-node-manager/releases/latest)**
y descarga uno de estos archivos:

- **`KeryxNodeManager-Setup-X.Y.Z.exe`** — instalador normal. Ejecútalo, sigue el asistente, listo.
  Se añade un acceso directo al escritorio y al menú Inicio. No requiere permisos de administrador.
- **`KeryxNodeManager-Portable-X.Y.Z.zip`** — versión portable sin instalación. Descomprime en
  cualquier carpeta y ejecuta `KeryxNodeManager.exe`.

En el primer inicio, un asistente de configuración te guía por una comprobación del sistema, la
introducción de tu dirección de minería y la creación/selección de un perfil — después se abre
directamente el Panel principal (Dashboard), con el nodo y el minero ya en marcha.

**Requisitos:** Windows 10/11 x64, una GPU NVIDIA (para la detección automática y el
overclocking). El binario del nodo (`keryxd.exe`) y del minero (`keryx-miner.exe`) no vienen
incluidos dentro del propio instalador, pero la aplicación los descarga e instala
automáticamente la primera vez que los necesitas — no hay ninguna página de actualizaciones
aparte que visitar ni ninguna ruta manual que escribir.

## Funciones

- El Panel principal muestra el estado del nodo, del minero y de la GPU en un solo lugar, con un
  único control de Iniciar todo / Detener todo para el nodo y el minero a la vez, además de un
  icono en la bandeja del sistema con estado en vivo.
- Detección automática de GPU, asignación automática del nivel de minería según la VRAM o
  selección manual por tarjeta.
- Overclocking de GPU (núcleo/memoria) y control del ventilador — protegido por un cuadro de
  confirmación.
- Descarga de modelos oficiales en un clic (mediante HTTP + réplicas por torrent), con la opción
  manual (reanudable y con verificación de integridad) como alternativa.
- Directorio de nodos públicos y descubrimiento automático de nodos vecinos a través de tu propio
  nodo; cambio a un nodo de respaldo mientras el tuyo se sincroniza, con retorno automático una
  vez que se pone al día.
- Descarga y extracción del data-dir en un clic (enlace directo o torrent).
- Registros con enmascaramiento automático de secretos, exportación de diagnóstico.
- Protección contra sobrecalentamiento, opción de inicio automático con Windows.
- Varios perfiles, interfaz disponible en 6 idiomas (ru/en/es/it/fr/uk).
- Comprobador de actualizaciones integrado para el nodo y el minero.

## Seguridad

La aplicación nunca pide ni almacena frases semilla o claves privadas. Cualquier dirección RPC en
la que la aplicación pueda responder está vinculada únicamente a `127.0.0.1` (localhost) — nada
se expone al exterior. Consulta `docs/SECURITY.md` en el repositorio para más detalles.

## Para desarrolladores

```powershell
dotnet restore
dotnet test tests\KeryxNodeManager.Core.Tests\KeryxNodeManager.Core.Tests.csproj -c Release
dotnet run --project src\KeryxNodeManager.App -- --mock
```

`--mock` ejecuta la interfaz con GPUs virtuales, sin binarios reales de Keryx ni NVAPI
involucrados — una forma segura de previsualizar la interfaz. Consulta `docs/BUILD.md` para
detalles de compilación y `docs/RELEASE.md` para el proceso de publicación.

## Licencia y estado

Proyecto en desarrollo activo, iniciativa impulsada por la comunidad. Los informes de errores y
sugerencias son bienvenidos a través de Issues.
