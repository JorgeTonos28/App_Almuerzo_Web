# Menú semanal desde Excel · v0.5.1

ADMIN_GEN y ADMIN_REG usan Administración → Menú → Importar menú semanal. La opción de pegar celdas se conserva. La carga de archivo está encima de esa caja y admite `.xls` (BIFF) y `.xlsx` (OOXML). No requiere Excel instalado en el servidor.

## Preparar la plantilla

1. Seleccionar la regional de trabajo y, opcionalmente, el lunes de la semana.
2. Descargar plantilla. Si no se eligió semana, utiliza el próximo lunes en Santo Domingo.
3. Compartir el archivo **Menú Semanal INFOTEP** con el proveedor. Cambiar únicamente el lunes en **B3**: las fechas de lunes a viernes, sobre los encabezados, cambian automáticamente.
4. En el propio Excel, abrir la flecha del encabezado **Categoría (A5)**, desmarcar las categorías no ofertadas y aceptar. Cada categoría ocupa tres filas de platos para los cinco días; el filtro quita el bloque completo, sin dejar separadores vacíos. Para agregarla nuevamente, volver a marcarla en ese mismo filtro. Excel conserva los platos ocultos, por lo que se pueden recuperar sin reescribirlos. Guardar el archivo después de aplicar el filtro.
5. Usar la lista desplegable de la primera celda de cada bloque si se necesita cambiar la categoría para toda la semana. Solo contiene categorías activas de esa regional. Si se cambia una categoría con un filtro ya aplicado, volver a abrir el filtro y seleccionar las categorías deseadas para actualizar las filas visibles. No se crean categorías en el Excel.
6. Escribir un plato por casilla, hasta tres por categoría y día. Dejar vacías las casillas que no se ofrecen. Las categorías pueden ofrecer platos solo algunos días. No hace falta escribir fechas diarias, insertar filas ni quitar la protección.
7. Cargar el archivo, revisar fechas, regional, platos y avisos; confirmar para guardar. **Los bloques ocultos no se importan**, aunque contengan platos. La vista previa indica cuántas categorías se excluyeron. Para conservarlas en la importación, mostrarlas antes de guardar el Excel.

El catálogo desplegable usa los nombres activos de la regional al descargar, sin datos de colaboradores ni secretos. Se genera un bloque por categoría activa, hasta veinte, sin bloques vacíos adicionales. La plantilla V2 ocupa **A:F**: A es la categoría compartida y B:F son lunes a viernes. El único filtro está en A y oculta filas completas. Las dos celdas inferiores de la categoría son fórmulas bloqueadas que repiten su clave para filtrar juntas las tres filas; el diseño muestra el nombre una sola vez. El importador no ejecuta esas fórmulas: toma la categoría de la primera fila del bloque y los valores guardados de los platos.

La estructura del libro, ambas hojas, títulos, encabezados, fórmulas y catálogo técnico quedan protegidos. Solo **B3**, la primera categoría de cada bloque y las casillas de platos están desbloqueadas. Excel no permite seleccionar las celdas bloqueadas, insertar/eliminar filas o columnas, cambiar formatos, ni agregar/renombrar/eliminar hojas; sí permite usar el filtro existente. Las listas y la fecha tienen validaciones con aviso de detención. La clave de mantenimiento de esta protección de edición está en [README](../README.md). No hay cifrado de apertura ni macros; la protección evita cambios accidentales y no sustituye la validación del servidor.

Los metadatos de formato (`ALMUERZO_MENU_V2`), regional y lista de categorías se guardan en la hoja técnica muy oculta y protegida; no otorgan permisos. Si cambió el catálogo, descargar una nueva plantilla. Las plantillas **V1** ya descargadas se siguen leyendo con sus cinco columnas, categorías independientes por día y bloques originales; el nuevo filtro solo se aplica a V2. El formato original del proveedor y el pegado TSV se conservan.

## Archivo del proveedor

También se reconoce el modelo de cinco columnas A:E: una fila LUNES/MARTES/MIÉRCOLES/JUEVES/VIERNES, categorías y platos debajo. La categoría se sigue por cada columna, por lo que puede variar entre días. Las celdas multilínea siguen siendo un solo plato, normalizado sin mayúsculas sostenidas. Vacíos y `N/A` se omiten. Se admite el título `DEL 05 AL 09 OCTUBRE 2026` para reconocer la semana; si no hay una fecha reconocible, el administrador debe elegir el lunes.

Solo se resuelven nombres y alias del catálogo regional, más las equivalencias explícitas del modelo (`Almuerzo Vegano` → `Vegetariana`, `Comida Rapida` → `Opcion_Rapida`, `Caldos` → `Caldo`) cuando existen esas categorías. No se fusiona una categoría distinta por semejanza. Por ejemplo, si el archivo contiene `Lasañas y/o Pastelones` y no existe en el catálogo, se detiene con la celda y categoría para que ADMIN_GEN revise el catálogo/alias o el proveedor utilice una categoría válida. ADMIN_REG no recibe permisos para crearlas.

Elegir otra semana aplica el contenido a las nuevas fechas y muestra el cambio explícitamente en la vista previa. Una semana pasada se puede inspeccionar, pero no confirmar. Se excluyen feriados nacionales y cierres regionales con aviso. Los días sin platos no se modifican. Al confirmar, se reemplaza el menú de los días mostrados; los pedidos afectados por platos retirados se cancelan con los avisos existentes en outbox.

## Integridad y límites

La vista previa no escribe en la base, no guarda el Excel ni envía correos. Confirmar utiliza `apiSaveWeeklyMenu`: transacción, permisos actuales, fechas futuras laborables, categorías activas de la regional, campos acotados, auditoría y correos en cola. Las categorías y menús se consultan en lote; no se consulta una categoría por cada plato. Se rechazan duplicados en una categoría del mismo día.

La plantilla está asociada a una regional. Un administrador general puede cambiar de regional y descargar la correspondiente. Copiar/manipular el identificador del archivo nunca sustituye la autorización del servidor. Se rechazan categorías ajenas, inactivas o desconocidas, incluso si su bloque está oculto. Los bloques V2 deben estar completamente visibles u ocultos: se rechaza una categoría parcialmente oculta para evitar importar solo parte de sus platos por accidente. Ocultar todas o dejar todos los platos vacíos no borra el menú. USER y ADMIN_DEP no acceden a generación/lectura; ADMIN_REG solo opera en su regional. La carga exige CSRF; generación y lectura comparten un límite de 12 solicitudes por minuto y cuenta.

Máximo 2 MB por archivo, cinco hojas, 32 columnas y 1000 filas por hoja; V2 ocupa A:F y el formato anterior A:E. OOXML admite hasta 300 entradas ZIP, 16 MB descomprimidos totales y 8 MB por parte, verificando también la descompresión real. Se acota tiempo y longitud de celdas y se limita a 200 platos diarios. Se comprueba extensión y firma; se rechazan `.xlsm`, archivos renombrados, vínculos externos y objetos incrustados en OOXML. El lector no ejecuta macros ni fórmulas: solo obtiene valores guardados; las fechas de la plantilla se reconstruyen desde B3. Archivos cifrados o ilegibles se rechazan sin exponer contenido ni detalles internos.

Antes de mostrar la confirmación también se verifica el tamaño del menú convertido, reservando margen para el límite de 256 KB de la petición de guardado. Si lo supera, se solicita dividir el contenido en archivos más pequeños, dejando vacíos los otros días; estos no se modifican.

Los archivos reales permanecen fuera de Git y de los paquetes de despliegue. Las pruebas usan libros sintéticos; la verificación del ejemplo del proveedor se hace solo en privado.
