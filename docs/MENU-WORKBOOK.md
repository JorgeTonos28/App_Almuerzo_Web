# Menú semanal desde Excel · v0.5.0

ADMIN_GEN y ADMIN_REG usan Administración → Menú → Importar menú semanal. La opción de pegar celdas se conserva. La carga de archivo está encima de esa caja y admite `.xls` (BIFF) y `.xlsx` (OOXML). No requiere Excel instalado en el servidor.

## Preparar la plantilla

1. Seleccionar la regional de trabajo y, opcionalmente, el lunes de la semana.
2. Descargar plantilla. Si no se eligió semana, utiliza el próximo lunes en Santo Domingo.
3. Compartir ese archivo con el proveedor. Tiene cinco columnas de lunes a viernes y una fecha editable en B3. Las fechas de los cinco días se calculan desde B3.
4. Cada bloque contiene una categoría con lista desplegable y tres casillas de platos para cada día. Las categorías pueden diferir entre días. Un plato por casilla; para más platos, repetir la categoría en otro bloque. Dejar vacías las casillas que no se ofrecen.
5. Cargar el archivo, revisar fechas, regional, platos y avisos; confirmar para guardar.

El catálogo desplegable usa los nombres activos de la regional al descargar, sin datos de colaboradores ni secretos. Se incluyen al menos diez bloques y hasta veinte categorías. No insertar filas/columnas ni escribir fuera de los bloques: repetir categorías en otro bloque disponible. No necesita macros. Los metadatos de formato y regional y la lista de categorías se guardan en una hoja técnica oculta; no otorgan permisos. Si cambió el catálogo, descargar una nueva plantilla.

## Archivo del proveedor

También se reconoce el modelo de cinco columnas A:E: una fila LUNES/MARTES/MIÉRCOLES/JUEVES/VIERNES, categorías y platos debajo. La categoría se sigue por cada columna, por lo que puede variar entre días. Las celdas multilínea siguen siendo un solo plato, normalizado sin mayúsculas sostenidas. Vacíos y `N/A` se omiten. Se admite el título `DEL 05 AL 09 OCTUBRE 2026` para reconocer la semana; si no hay una fecha reconocible, el administrador debe elegir el lunes.

Solo se resuelven nombres y alias del catálogo regional, más las equivalencias explícitas del modelo (`Almuerzo Vegano` → `Vegetariana`, `Comida Rapida` → `Opcion_Rapida`, `Caldos` → `Caldo`) cuando existen esas categorías. No se fusiona una categoría distinta por semejanza. Por ejemplo, si el archivo contiene `Lasañas y/o Pastelones` y no existe en el catálogo, se detiene con la celda y categoría para que ADMIN_GEN revise el catálogo/alias o el proveedor utilice una categoría válida. ADMIN_REG no recibe permisos para crearlas.

Elegir otra semana aplica el contenido a las nuevas fechas y muestra el cambio explícitamente en la vista previa. Una semana pasada se puede inspeccionar, pero no confirmar. Se excluyen feriados nacionales y cierres regionales con aviso. Los días sin platos no se modifican. Al confirmar, se reemplaza el menú de los días mostrados; los pedidos afectados por platos retirados se cancelan con los avisos existentes en outbox.

## Integridad y límites

La vista previa no escribe en la base, no guarda el Excel ni envía correos. Confirmar utiliza `apiSaveWeeklyMenu`: transacción, permisos actuales, fechas futuras laborables, categorías activas de la regional, campos acotados, auditoría y correos en cola. Las categorías y menús se consultan en lote; no se consulta una categoría por cada plato. Se rechazan duplicados en una categoría del mismo día.

La plantilla está asociada a una regional. Un administrador general puede cambiar de regional y descargar la correspondiente. Copiar/manipular el identificador del archivo nunca sustituye la autorización del servidor. USER y ADMIN_DEP no acceden a generación/lectura; ADMIN_REG solo opera en su regional. La carga exige CSRF; generación y lectura comparten un límite de 12 solicitudes por minuto y cuenta.

Máximo 2 MB por archivo, cinco hojas, 32 columnas y 1000 filas por hoja; cada menú ocupa solo A:E. OOXML admite hasta 300 entradas ZIP, 16 MB descomprimidos totales y 8 MB por parte, verificando también la descompresión real. Se acota tiempo y longitud de celdas y se limita a 200 platos diarios. Se comprueba extensión y firma; se rechazan `.xlsm`, archivos renombrados, vínculos externos y objetos incrustados en OOXML. El lector no ejecuta macros ni fórmulas: solo obtiene valores guardados; las fechas de la plantilla se reconstruyen desde B3. Archivos cifrados o ilegibles se rechazan sin exponer contenido ni detalles internos.

Antes de mostrar la confirmación también se verifica el tamaño del menú convertido, reservando margen para el límite de 256 KB de la petición de guardado. Si lo supera, se solicita dividir el contenido en archivos más pequeños, dejando vacíos los otros días; estos no se modifican.

Los archivos reales permanecen fuera de Git y de los paquetes de despliegue. Las pruebas usan libros sintéticos; la verificación del ejemplo del proveedor se hace solo en privado.
