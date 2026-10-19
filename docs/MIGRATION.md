# Análisis y avance de la migración

> Registro histórico de las versiones 0.1.x. Para la arquitectura y operación vigentes de v0.2.1 —SQL Server, regionales, ADMIN_REG, SendGrid y el nuevo juego— seguir [DEPLOYMENT-NATIONAL.md](DEPLOYMENT-NATIONAL.md) y README. Las limitaciones antiguas de SQLite/SMTP/puntuación del cliente que siguen abajo describen el estado de esas versiones.

## Referencias verificadas

- Destino: `JorgeTonos28/App_Almuerzo_Web`, inicialmente sin commits ni archivos. Rama de trabajo: `feature/migracion-aspnet`.
- Origen funcional: `JorgeTonos28/App_Almuerzo`, v7.36, commit `16feb257cb9426df24f936c9941447e7f3fb6d12`.
- Referencia ASP.NET: `JorgeTonos28/ExtraHub`, main `194b41675a0d1954e92a1154f1dd325ca149a8c8`.
- Integración AD: ExtraHub PR #15, commit `bff85a6f22b8cc2d98004d6ceb8fcd29080ecf6d`, revisado junto con su AGENTS y el WSDL del servicio institucional.
- Se revisaron AGENTS de las dos referencias. Se adaptaron versionado, migraciones y semillas, documentación, responsive, loaders, preferencias, normalización y rendimiento. AGENTS del destino exige seguridad en cada implementación.

ExtraHub aporta el patrón MVC / EF Core / cookies / servicios. El PR #15 proporcionado después de la primera entrega contiene la integración ASMX. En v0.1.1 se implementan `Autenticacion` y `DatosUsuarioAD` por HTTPS/formulario y lectura XML segura. El WSDL respondió HTTP 200 y confirmó métodos y parámetros; falta probar credenciales reales. El proveedor se activa con configuración privada, sin copiar tokens.

## Arquitectura inicial

ASP.NET Core 10 MVC con autorización del servidor; interfaz Vue portada directamente del HTML, CSS y lógica de App_Almuerzo. La plantilla se compila durante `npm run build`; Vue en producción usa su runtime sin compilador. Todos los scripts, fuentes y estilos son locales. No se requiere Google Apps Script para operar la aplicación.

EF Core / SQLite se usa para la migración y pruebas locales y para una instalación inicial de una instancia. Los servicios concentran identidad, permisos, reglas del calendario, menú, pedidos, reportes, importación y operaciones. Hay cuatro migraciones y semillas idempotentes; la tercera añade imágenes de identidad visual en la base privada y la cuarta la vinculación única de cuenta AD. La implantación en SQL Server o en múltiples instancias requiere migraciones específicas y coordinación de trabajos; no se considera resuelta en esta versión.

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

Pendientes de puesta en producción: login AD con credenciales reales, verificación SMTP, dominio/certificado HTTPS, permisos de almacenamiento y cuenta de servicio, monitorización de jobs/outbox, retención y restauración de respaldos. El ranking de juego mantiene estado aportado por el cliente con validaciones de límites; si se utiliza para premios o decisiones reales necesita eventos y puntuación calculados exclusivamente por servidor.

## Pruebas y aceptación

La suite sintética verifica autenticación, CSRF, scope, bloqueo de escalamiento, revocación, propiedad e integridad de pedidos, reemplazo de menú y cancelaciones, calendario, valoraciones/histórico, aprobación de acceso, solicitud de revisión, preferencias/precios, importación idempotente, redacción de secretos, rollback y reportes por departamento. No usa el Excel ni la base reales en CI.

Verificación del 9 de octubre de 2026: 14 pruebas pasan en Release; build/publish sin errores. La importación real se repitió y respondió `alreadyImported`, sin duplicar registros. NuGet no reporta vulnerabilidades. npm audit llegó a responder sin vulnerabilidades tras actualizar dependencias; las repeticiones posteriores fallaron por conexión TLS. La consulta alternativa al mismo registro oficial revisó 80 paquetes del lockfile y devolvió cero paquetes con avisos.

En el navegador se guardaron un pedido, una semana con TSV multilínea y una actualización de precio; también se validaron logout/login. Se corrigió el encabezado móvil para admitir el nuevo cierre de sesión y el selector departamental. Inicio se verificó a 390px y 1280px sin desbordamiento de página, imágenes rotas ni errores de consola. Se renderizaron y revisaron las dos páginas del PDF sintético. Las capturas de QA se guardan localmente en una carpeta ignorada.

Estas comprobaciones no equivalen a la aceptación completa del clon: deben contrastarse todos los módulos con el sistema vigente y sus responsables, incluyendo los recursos de Drive y las integraciones reales que aún faltan.

## Configuración de v0.1.4

Seguir [SETUP-AD-MAIL.md](SETUP-AD-MAIL.md) para introducir token y contraseña SMTP con entrada oculta, habilitar AD, iniciar con la base importada, comprobar solicitudes/aprobaciones/revocaciones y probar correos. La cola puede ejecutarse sin activar trabajos diarios. El opt-in de SMTP en Development exige un único buzón de prueba. La prueba de cierre incluye Excel/PDF consolidados y por departamento sin respaldos ni mantenimiento.

El panel general CONFIG permite cargar favicon y sello PNG y editar texto del pie. La configuración se refleja en la aplicación, login y acceso denegado; PNG se valida y sanitiza, el texto se codifica, las imágenes permanecen en SQLite privado y los archivos reemplazados se eliminan. El crédito predeterminado es `INFOTEP - DIAET © 2026` y la firma institucional incluida en `wwwroot/assets/signature.png` aparece a la derecha del footer. El administrador puede sustituirla o quitarla desde CONFIG.

Verificación de v0.1.1: 51 pruebas sintéticas pasan en Release, incluidos contrato ASMX, normalización de cuenta, perfil ambiguo, XML/XXE, límites y timeout, código verificado frente a formularios manipulados, conflictos de identidad, CSRF/rol de branding, PNG corrupto o fuera de límites, guardado/restauración, entrega Pickup, restricciones SMTP y reportes de prueba departamentales. Build/publish sin errores y snapshot EF sin cambios pendientes. La base real recibió la nueva migración y repetir el libro devolvió `alreadyImported`, sin reinsertar datos.

En v0.1.2, el código único verificado permite vincular la cuenta AD conservando el correo y pedidos de Sheets cuando existen aliases diferentes. El vínculo queda persistido y no permite que otra cuenta use después el mismo código. Los perfiles vinculados conservan nombre/código AD; su restablecimiento exige ADMIN_GEN, se audita y revoca sesiones. La guía incluye ese procedimiento. Las pruebas adicionales verifican conservación de históricos/roles, bloqueo de apropiación de vínculos y permiso/revocación al restablecerlos.

Verificación final de v0.1.2: 54 pruebas correctas en Release, publicación y modelo EF sin cambios pendientes, cuatro migraciones aplicadas a la base real e importación repetida sin duplicados. En el navegador se comprobó el perfil vinculado, la confirmación de restablecimiento y el cierre inmediato de su sesión sintética. El formulario dispone de scroll interno en pantallas cortas. Los paquetes publicados y los commits locales excluyen bases, imágenes subidas, correos y secretos.

En navegador se cargaron ambas imágenes de prueba, se guardó el texto y se restauró el favicon predeterminado/eliminó el sello; la recarga conservó los cambios. CONFIG se verificó a 390px y escritorio sin imágenes rotas ni errores de consola; se corrigió el ancho de la cuadrícula móvil. Las capturas locales de v0.1.1 están en `artifacts/qa`, fuera de Git.

En v0.1.4 se incorpora la firma institucional como recurso estático seguro y se actualiza el crédito predeterminado. La base local de pruebas y el Excel de origen siguen excluidos de Git por contener datos personales. TI debe solicitar una copia actualizada por un canal privado antes de la puesta en producción y cargarla con `scripts\Import-Legacy.cmd` en un almacenamiento persistente fuera de la carpeta pública.
