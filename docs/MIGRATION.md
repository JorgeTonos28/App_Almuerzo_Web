# Análisis y avance de la migración

## Referencias verificadas

- Destino: `JorgeTonos28/App_Almuerzo_Web`, inicialmente sin commits ni archivos. Rama de trabajo: `feature/migracion-aspnet`.
- Origen funcional: `JorgeTonos28/App_Almuerzo`, v7.36, commit `16feb257cb9426df24f936c9941447e7f3fb6d12`.
- Referencia ASP.NET: `JorgeTonos28/ExtraHub`, main `194b41675a0d1954e92a1154f1dd325ca149a8c8`.
- Se revisaron AGENTS de las dos referencias. Se adaptaron versionado, migraciones y semillas, documentación, responsive, loaders, preferencias, normalización y rendimiento. AGENTS del destino exige seguridad en cada implementación.

ExtraHub aporta el patrón MVC / EF Core / cookies / servicios. Su implementación pública revisada autentica contra contraseñas locales; no contiene el endpoint AD señalado, tampoco en las ramas remotas disponibles revisadas. No se ha inventado una conexión funcional con el servicio institucional: el adaptador permanece deshabilitado hasta confirmar su contrato.

## Arquitectura inicial

ASP.NET Core 10 MVC con autorización del servidor; interfaz Vue portada directamente del HTML, CSS y lógica de App_Almuerzo. La plantilla se compila durante `npm run build`; Vue en producción usa su runtime sin compilador. Todos los scripts, fuentes y estilos son locales. No se requiere Google Apps Script para operar la aplicación.

EF Core / SQLite se usa para la migración y pruebas locales y para una instalación inicial de una instancia. Los servicios concentran identidad, permisos, reglas del calendario, menú, pedidos, reportes, importación y operaciones. Hay dos migraciones y semillas idempotentes. La implantación en SQL Server o en múltiples instancias requiere migraciones específicas y coordinación de trabajos; no se considera resuelta en esta versión.

La interfaz heredada invoca un puente asíncrono por `POST /api/rpc` con una lista cerrada de operaciones. Las identidades y roles del navegador no se aceptan como autoridad. Los errores de negocio se muestran en el flujo existente y los cambios de datos, auditoría y correo se confirman en una transacción.

## Módulos y adaptación

| Módulo original | Implementación / validación inicial |
|---|---|
| Inicio, pestañas por fecha, selección y confirmación | Diseño original portado, combinaciones validadas contra menú vigente, pedido sintético guardado desde el navegador |
| Resumen semanal y coste | Históricos y precios por vigencia, filtros de fechas originales |
| Cancelación y modificación | Propiedad del pedido, scope y corte horario en servidor; históricos conservados |
| Administrador por departamento | Usuarios, pedidos y colaboración de su departamento; sin asignación de roles administrativos ni acceso a otros departamentos |
| Usuarios y departamentos | Altas, códigos únicos de cuatro dígitos, estados, roles, asignación de admins y protección del último admin general |
| Menú individual y categorías | Catálogo configurable, aliases, combinaciones, selección única/múltiple, fechas futuras laborables |
| Importador semanal | Se conserva el parser TSV con comillas y celdas multilínea, semana lunes-viernes, aliases, días libres y previsualización; reemplazo validado y atómico en servidor |
| Modificación del menú | Solo cancela pedidos afectados por plato/categoría/descripcion cambiados; notificación en cola |
| Calendario | Suspensiones, cancelaciones y avisos por pedidos afectados; reglas de fines de semana y feriados |
| Configuración y avisos | Campos permitidos, horas, precios históricos, proveedor y avisos de varias pantallas; descartes en preferencias |
| Valoración de comidas | Pedido propio activo, consumo desde las 12:00 locales, puntuación de 1 a 5 |
| Proveedor e histórico | Voto actual por usuario/período y registro independiente por cada modificación |
| Satisfacción | Estadísticas y comentarios; feedback público anonimizado y detalle del admin departamental limitado a su departamento |
| Mini Chef y ranking | Estado mensual/diario conservado, límite diario y controles de progreso; las interacciones del juego siguen originándose en el cliente |
| Presencia y preferencias | Heartbeat de sesión habilitada y fusión JSON sin borrar otras claves |
| Recordatorios y cierre | Worker local configurable, zona Santo Domingo, claves diarias, outbox con reintentos y respaldos SQLite |
| Reportes | Excel y PDF general y por departamento, columnas por categoría, código, nombre y notas; autorización en descarga y distribución por rol |
| API externa de menú diario | Endpoint de lectura con token Bearer en cabecera, configurado fuera de la base y del repositorio |
| Acceso | Login institucional preparado, acceso denegado y solicitud; PENDIENTE hasta aprobación con rol y departamento |

El sello del pie, la plantilla de reporte y los backups de Drive eran recursos externos identificados en Config, sin los archivos en el repo. Se conserva el pie y se permite configurar un sello local. Los respaldos y adjuntos se guardan de forma privada en el servidor. Los correos adaptan la tarjeta blanca, tipografía, tonos azules y botón del original, con alternativa de texto y contenido codificado. Los logos CID externos y la redacción detallada de cada plantilla requieren los recursos y la aceptación final.

## Datos reales: importación local

Se leyó el Excel proporcionado como datos de origen. Sus celdas y textos no se trataron como instrucciones de desarrollo. El libro tiene muchas filas vacías con formato; la reconciliación cuenta registros no vacíos.

| Hoja | Registros importados |
|---|---:|
| Config | 34 registros de origen; solo ajustes operativos permitidos se activan |
| Usuarios | 329 |
| Departamentos | 21 |
| CategoriasMenu | 10 |
| Menu | 2,282 |
| Pedidos | 10,647 |
| DiasLibres | 9 |
| ValoracionesComida | 29 |
| ValoracionesProveedor | 19 |
| HistoricoValoracionesProveedor | 21 |
| Total de registros de origen | 13,401 |

Los IDs y los históricos se conservaron. Se redactó un secreto en Config, incluso en el archivo lógico de registros de origen. La base y el Excel permanecen locales y excluidos de Git. La importación es repetible por hash y rechaza mezclar otros conjuntos de datos en una base operativa.

Una cuenta de la hoja Usuarios (fila 329) referencia un departamento que no está en Departamentos. Queda sin departamento asignado y sin acceso hasta revisión; el valor original permanece en el archivo lógico privado. No se tomó una decisión arbitraria de departamento ni se habilitó esa cuenta.

Durante la configuración inicial de pruebas se detectó que una configuración anticipada de ruta podía apuntar al almacén predeterminado. Se corrigió para resolver el almacén de cada prueba desde su configuración. La base sintética anterior se preservó en un subdirectorio privado y la importación real se hizo después en una base limpia. Las pruebas actuales utilizan rutas temporales independientes.

## Seguridad implementada

- Denegación por defecto; identidad AD separada de habilitación, rol y departamento locales; revalidación de sesión en cada petición.
- Cookies HttpOnly / SameSite Strict, expiración absoluta, HTTPS y HSTS en producción, claves nuevas de Data Protection cifradas.
- Antiforgery en cambios, protección de logout, límites de login / RPC y cuerpos de petición acotados.
- Validación del servidor, transacciones, índices únicos y concurrencia para impedir pedidos duplicados y cambios sobre pedidos ajenos.
- CSP sin `unsafe-eval`, recursos locales, valores JSON iniciales codificados y cabeceras contra framing y sniffing.
- Secretos fuera de Git; token AD y credenciales solo en configuración privada; lectura AD HTTPS, sin redirects y respuesta de tamaño limitado.
- Reportes fuera de `wwwroot`, roles en descarga, scopes por departamento, texto Excel sin fórmulas de usuario, correo de desarrollo con salida local.
- Dependencias auditadas; cola y auditoría sin almacenar contraseñas ni respuestas AD.

Pendientes de puesta en producción: contrato real de AD, verificación SMTP, dominio/certificado HTTPS, permisos de almacenamiento y cuenta de servicio, monitorización de jobs/outbox, retención y restauración de respaldos. El ranking de juego mantiene estado aportado por el cliente con validaciones de límites; si se utiliza para premios o decisiones reales necesita eventos y puntuación calculados exclusivamente por servidor.

## Pruebas y aceptación

La suite sintética verifica autenticación, CSRF, scope, bloqueo de escalamiento, revocación, propiedad e integridad de pedidos, reemplazo de menú y cancelaciones, calendario, valoraciones/histórico, aprobación de acceso, solicitud de revisión, preferencias/precios, importación idempotente, redacción de secretos, rollback y reportes por departamento. No usa el Excel ni la base reales en CI.

Verificación del 9 de octubre de 2026: 14 pruebas pasan en Release; build/publish sin errores. La importación real se repitió y respondió `alreadyImported`, sin duplicar registros. NuGet no reporta vulnerabilidades. npm audit llegó a responder sin vulnerabilidades tras actualizar dependencias; las repeticiones posteriores fallaron por conexión TLS. La consulta alternativa al mismo registro oficial revisó 80 paquetes del lockfile y devolvió cero paquetes con avisos.

En el navegador se guardaron un pedido, una semana con TSV multilínea y una actualización de precio; también se validaron logout/login. Se corrigió el encabezado móvil para admitir el nuevo cierre de sesión y el selector departamental. Inicio se verificó a 390px y 1280px sin desbordamiento de página, imágenes rotas ni errores de consola. Se renderizaron y revisaron las dos páginas del PDF sintético. Las capturas de QA se guardan localmente en una carpeta ignorada.

Estas comprobaciones no equivalen a la aceptación completa del clon: deben contrastarse todos los módulos con el sistema vigente y sus responsables, incluyendo los recursos de Drive y las integraciones reales que aún faltan.

## Siguiente integración necesaria

Se necesita localizar la implementación AD indicada o recibir documentación de URL, método, campos, cabecera de token y respuesta de éxito/error. El token no debe pegarse en el chat ni en archivos versionados. El script de User Secrets está preparado para ingresarlo localmente. Una vez confirmado el contrato, ajustar el proveedor, ejecutar pruebas contra AD y validar el flujo completo de solicitud, aprobación y revocación con cuentas de prueba autorizadas.
