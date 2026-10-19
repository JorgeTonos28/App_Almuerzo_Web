# Entrega a TI · App Almuerzo Web 0.2.1

Esta es la guía vigente. Todos los comandos son para **CMD**. El código incluye SQL Server, separación regional, ADMIN_REG y SendGrid. Falta validar contra el servidor SQL institucional, el nuevo contrato del AD y un envío real con las credenciales/remitente de SendGrid que entregue TI.

## 1. Preparar la entrega y proteger los datos

1. Solicitar al responsable de Almuerzo **la base actualizada al momento de desplegar**, por un canal privado. La copia usada durante desarrollo tiene información histórica y no representa necesariamente el estado actual.
2. Detener la app antes de actualizar/importar. Conservar el origen SQLite, su almacenamiento privado y un respaldo verificable. No copiar Excel, bases, correos, claves ni tokens a GitHub o wwwroot.
3. Elegir una sola fuente para la migración inicial: SQLite operativo completo, o Excel actualizado importado en un destino nuevo. No mezclar ambas fuentes en una base poblada.
4. Disponer de SQL Server con base exclusiva, conexión cifrada, certificado de confianza y cuenta de servicio. La instalación inicial necesita permiso de crear/modificar el esquema por las migraciones EF; coordinar con TI las restricciones posteriores y futuras actualizaciones. Respaldos, retención y restauración SQL quedan a cargo de TI.
5. Disponer de .NET 10 Hosting Bundle para IIS, HTTPS y DNS institucionales, y almacenamiento privado persistente fuera del directorio publicado, por ejemplo `C:\AlmuerzoPrivate\storage`. Dar acceso únicamente a la cuenta de servicio y administradores autorizados.

Operar una sola instancia de la app y un worker. Antes de usar varias instancias hay que implementar coordinación distribuida de trabajos/cola y compartir las claves de Data Protection adecuadamente.

## 2. Compilar y generar el paquete

Cerrar con `Ctrl+C` la consola que esté ejecutando la app antes de compilar. Si aparece MSB3027, el error identifica el PID que mantiene el ejecutable abierto: comprobar que corresponde a esta aplicación y detener esa instancia. No terminar todos los procesos dotnet.

```cmd
cd /d C:\Dev\AppsWeb\App_Almuerzo
npm ci
npm run build
dotnet restore Almuerzo.sln
dotnet build Almuerzo.sln -c Release --no-restore
dotnet test Almuerzo.sln -c Release --no-build
dotnet publish src\Almuerzo.Web -c Release --no-build -o artifacts\publish-v021
```

El paquete publicado no incluye la base ni el Excel. Para ensayar sin datos reales, usar la demo descrita en README. No usar `--demo` en producción.

## 3. Configuración local con User Secrets

Para evitar escribir secretos en la línea de comandos, guardar un JSON temporal **fuera del repo**, con permisos privados, e importarlo por stdin. No usar `dotnet user-secrets list` en una consola compartida. No enviar el JSON por chat ni guardarlo en capturas/logs.

```cmd
mkdir C:\AlmuerzoPrivate
notepad C:\AlmuerzoPrivate\secrets.local.json
```

Ejemplo de estructura; sustituir los valores de TI. La API key y el token de ejemplo no son credenciales válidas:

```json
{
  "Database:Provider": "SqlServer",
  "ConnectionStrings:DefaultConnection": "Server=SERVIDOR_SQL;Database=Almuerzo;Integrated Security=true;Encrypt=true;TrustServerCertificate=false",
  "Storage:Path": "C:\\AlmuerzoPrivate\\storage",
  "Ad:Enabled": true,
  "Ad:Token": "TOKEN_ENTREGADO_POR_TI",
  "Ad:AutoProvisioningEnabled": false,
  "Mail:Mode": "Pickup",
  "Mail:From": "REMITENTE_VERIFICADO_POR_TI",
  "SendGrid:ApiKey": "API_KEY_ENTREGADA_POR_TI",
  "SendGrid:DataResidency": "Global",
  "Operations:Enabled": false,
  "Operations:ScheduledJobsEnabled": false
}
```

```cmd
dotnet user-secrets set --project src\Almuerzo.Web < C:\AlmuerzoPrivate\secrets.local.json
```

La importación fusiona esas claves; conservar las demás configuraciones privadas existentes. Retirar el archivo temporal según la política de secretos de TI. Para LocalDB instalado en el equipo, la conexión de desarrollo puede usar `Server=(localdb)\MSSQLLocalDB;Database=Almuerzo_Pruebas;Integrated Security=true;Encrypt=true;TrustServerCertificate=false`. LocalDB no sustituye al servidor de producción.

## 4. Pasar SQLite a SQL Server

El destino debe tener esquema vacío de datos; no iniciar la web sobre él antes de la transferencia, porque el arranque normal inserta catálogos/ajustes. El comando de transferencia aplica el esquema necesario antes de copiar y no ejecuta la web ni envía correos.

En desarrollo, una vez configurada la conexión a una base SQL nueva:

```cmd
scripts\Migrate-Sqlite.cmd C:\AlmuerzoPrivate\almuerzo.db
```

Con el binario publicado, en la consola privada de instalación y con las variables de producción ya inyectadas por TI:

```cmd
cd /d C:\AlmuerzoDeploy
set ASPNETCORE_ENVIRONMENT=Production
set Database__Provider=SqlServer
set Operations__Enabled=false
dotnet Almuerzo.Web.dll --migrate-sqlite C:\AlmuerzoPrivate\almuerzo.db
```

La conexión `ConnectionStrings__DefaultConnection` y `Storage__Path` deben estar configuradas para esa consola. Las configuraciones del Application Pool no se heredan automáticamente en una consola de CMD.

La transferencia hace un respaldo SQLite consistente a un archivo temporal, migra esa copia, conserva IDs, vínculos AD, pedidos, preferencias, precios, auditoría, valoraciones y registros originales, y copia todas las tablas en una transacción SQL. Reconcilia conteos antes del commit. Devuelve `transferred:true` y conteos por tabla. Si se repite contra un destino ya poblado, lo rechaza sin modificar los datos. El SQLite original no cambia.

Si existen correos pendientes con adjuntos, sus rutas privadas se conservan: trasladar también Reports al mismo `Storage:Path`/ruta antes de activar el worker. Conservar `keys` y la identidad de servicio para mantener las sesiones; si se cambia de equipo/cuenta, DPAPI puede exigir renovar sesiones o migrar claves con un mecanismo de TI. La firma por defecto es parte del paquete; las imágenes personalizadas permanecen en la base.

Todos los datos anteriores quedan en la regional `central`, denominada inicialmente `Sede inicial`. No repartir históricos por nombres de departamento de forma automática. Renombrar/vincular esa regional desde el panel; los históricos conservan el ámbito en el que se registraron.

### Alternativa: Excel actualizado

Usar otra base SQL vacía para un libro nuevo. El importador mantiene el reconocimiento por hash y rechaza combinar otros conjuntos de datos en una base operativa.

```cmd
scripts\Import-Legacy.cmd C:\AlmuerzoPrivate\DB_Almuerzo.xlsx C:\AlmuerzoPrivate\storage
```

En el servidor publicado:

```cmd
dotnet Almuerzo.Web.dll --import C:\AlmuerzoPrivate\DB_Almuerzo.xlsx
```

La importación incluye los roles aprobados de origen. Las semillas nunca crean un administrador con contraseña conocida. Si se parte completamente de cero, preparar con TI un archivo privado de importación que contenga al administrador institucional autorizado; no conceder ADMIN_GEN por registrarse mediante AD.

## 5. Arrancar y configurar las regionales

Después de importar, iniciar localmente:

```cmd
set ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src\Almuerzo.Web -c Release --no-build --no-launch-profile --urls http://localhost:5062
```

1. Entrar por AD con una cuenta ADMIN_GEN importada. El login permanece en la misma pestaña.
2. Abrir administración y **Administrar regionales y códigos AD**. Renombrar Sede inicial y registrar su código institucional. Crear las demás regionales y sus códigos exactos; no inventar códigos.
3. Usar el selector del encabezado para trabajar en cada regional. Cada una recibe catálogos y valores iniciales seguros, con modo de correo de prueba. Revisar proveedor/precio, horarios, menú, calendario, destinatarios e identidad visual de cada una.
4. En Usuarios, ADMIN_GEN asigna roles y estado general a cuentas ya incorporadas por AD/importación. Nombre y código son de solo lectura. El departamento verificado también queda protegido. No existe creación manual de identidades. ADMIN_REG no modifica roles ni habilitación general.
5. Como ADMIN_GEN, en Departamentos, vincular el código AD de los departamentos ya existentes antes de activar la sincronización. Asignar uno o varios responsables habilitados como ADMIN_DEP; deben pertenecer ya al departamento, sin suspensión regional. Esta operación nunca los traslada. Esos responsables reciben el Excel/PDF departamental y coordinan el retiro; ADMIN_REG recibe el consolidado de su regional, y ADMIN_GEN los consolidados regionales.
6. Si un departamento nuevo llega desde AD, se crea sin responsables; un administrador debe asignarlos. El acceso USER no depende de esa asignación. Revisar los departamentos sin responsable antes de operar.

| Rol | Ámbito y permisos |
|---|---|
| USER | Pedidos, preferencias, juego y valoraciones propios en su regional |
| ADMIN_DEP | Consulta de colaboradores y gestión de pedidos/reportes de su departamento; no modifica cuentas |
| ADMIN_REG | Menú, calendario, pedidos, reportes, avisos y suspensión regional USER/ADMIN_DEP; identidades/departamentos de consulta |
| ADMIN_GEN | Gestión nacional mediante selector; permisos, responsables, catálogos, reglas, configuración y proveedor/precios; respeta datos verificados de AD |

Consultar la [matriz detallada de permisos](ACCESS-CONTROL.md) antes de asignar roles. La suspensión regional no modifica el estado general: retirarla no reactiva INACTIVO ni aprueba PENDIENTE. Nombre/código y organización verificada se corrigen en AD. ADMIN_REG publica AVISOS, sin editor de configuración general, correos de prueba o identidad visual.

El selector cambia el contexto administrativo, no traslada al administrador ni sus pedidos personales a otra regional. Para pedir o jugar debe volver a su regional. Los reportes y valoraciones no se combinan entre regionales.

Para cambiar proveedor: CONFIG → Proveedor → Gestionar, escribir nombre y precio y confirmar. El importe se exige aunque sea igual al anterior. Proveedor, precio y nuevo período de valoración se guardan juntos. Las valoraciones anteriores permanecen archivadas y los precios históricos mantienen sus vigencias.

## 6. AD actual y preparación del nuevo contrato

Contrato actual: HTTPS POST a `https://ona-app-01.infotep.gob.do:8005/ADService.asmx/Autenticacion` (`Token`, `NombreUsuario`, `Password`); después `DatosUsuarioAD` (`Token`, `NombreUsuario`). La contraseña no se persiste ni se registra. El perfil verificado aporta nombre y código, y vincula la cuenta importada conservando correo/históricos. Conflictos de cuenta/código bloquean el login; ADMIN_GEN puede restablecer el vínculo desde Usuarios con auditoría y revocación de sesión.

Mantener `Ad:AutoProvisioningEnabled=false` mientras TI no entregue el contrato nuevo. El modo actual sigue permitiendo solicitudes y aprobación administrativa, avisando a generales y regionales del ámbito.

Pedir a TI: URL definitiva, métodos/parámetros, XML de ejemplo anonimizado del perfil del usuario autenticado, columna de código/nombre, **código estable de regional**, **código de departamento** y **nombre de departamento**. Confirmar formato de cuenta y unicidad del código de empleado. No enviar contraseñas o tokens en ejemplos.

Si se conserva el contrato ASMX actual y solo se agregan columnas, basta configurar sus nombres exactos:

| Clave | Valor requerido |
|---|---|
| `Ad:Enabled` | `true` tras configurar el token |
| `Ad:Endpoint` | URL base ASMX HTTPS confirmada por TI |
| `Ad:Token` | Secreto institucional |
| `Ad:FullNameColumn` | Por defecto `NombreCompleto` |
| `Ad:EmployeeCodeColumn` | Por defecto `Codigo` |
| `Ad:RegionCodeColumn` | Nombre exacto de la columna regional |
| `Ad:DepartmentCodeColumn` | Nombre exacto de la columna código departamental |
| `Ad:DepartmentNameColumn` | Nombre exacto de la columna nombre departamental |
| `Ad:AutoProvisioningEnabled` | `true` solo después del mapeo y pruebas |

Se exige que esos campos pertenezcan al mismo perfil/código verificado. Si TI cambia métodos, parámetros o transporte, hay que ajustar el adaptador con el contrato real; no se ha supuesto un endpoint que todavía no existe.

Una cuenta administrativa PENDIENTE tampoco se activa automáticamente: su habilitación requiere una decisión explícita. La provisión automática de cuentas pendientes se limita a USER.

Con provisión activa, un nuevo perfil de regional mapeada y departamento activo se crea como USER y entra sin solicitud. Un departamento inexistente se crea a partir del perfil verificado. Si ya existe el nombre sin código mapeado, se exige vinculación administrativa para no duplicarlo. Regional desconocida/inactiva, departamento inactivo, perfil incompleto o conflictos de identidad impiden el acceso. Las cuentas INACTIVO nunca se reactivan por autenticarse. Roles administrativos se asignan manualmente. Cambiar de departamento retira ADMIN_DEP; cambiar de regional retira ADMIN_REG. Un departamento administrado por AD no se modifica desde un formulario del colaborador.

## 7. Habilitar SendGrid y probar correos

TI debe entregar una API key con permiso **Mail Send**, remitente/dominio autenticado y residencia Global o EU según su cuenta. Confirmar autorización de salida HTTPS/443 a `api.sendgrid.com` (o `api.eu.sendgrid.com`). No se necesita usuario/contraseña SMTP para este modo.

| Configuración privada | Ejemplo / función |
|---|---|
| `Mail:Mode` | `SendGrid` |
| `SendGrid:ApiKey` | API key privada de TI |
| `Mail:From` | Dirección verificada en SendGrid |
| `SendGrid:DataResidency` | `Global` por defecto; `EU` para la cuenta compatible |
| `Mail:AllowSendGridInDevelopment` | `true` únicamente para prueba externa desde Development |
| `Operations:Enabled` | `true` para procesar la cola |
| `Operations:ScheduledJobsEnabled` | `false` durante pruebas manuales |

Prueba controlada:

1. Iniciar con `Mail:Mode=Pickup`, `Operations:Enabled=true` y trabajos diarios desactivados. En CONFIG de la regional activar `TEST_EMAIL_MODE=TRUE` y establecer **un único buzón institucional de prueba** en `TEST_EMAIL_DEST`. Revisar también `APP_URL` HTTPS, nombre del remitente y horarios. Guardar configuración.
2. Pulsar **Enviar correos de prueba**, escoger fecha con pedidos sintéticos e inspeccionar los `.eml` en `Storage:Path\mail`, con Excel/PDF. Las pruebas de cierre no ejecutan respaldos ni modifican pedidos.
3. Configurar API key, remitente y `Mail:Mode=SendGrid`; en Development activar también `Mail:AllowSendGridInDevelopment=true`. Reiniciar para cargar los secretos. Mantener el modo de prueba y el destinatario único.
4. Repetir el envío manual. Esperar el ciclo de cola (hasta 30 segundos). Comprobar el mensaje y adjuntos en el buzón y su estado en SendGrid Email Activity. La app marca aceptación solo con HTTP 202, lo cual no confirma recepción ni ausencia de rebote.
5. Revisar logs sin secretos: HTTP 401/403 suele indicar clave/permisos o remitente; 429 requiere revisar cuota. La cola respeta Retry-After y usa espera creciente, hasta cinco intentos. Un mensaje agotado requiere revisión operativa; no hay panel de reenvío automático. No modificar la cola mientras el worker esté procesando.
6. En homologación y en **cada regional**, verificar un resumen consolidado y otro por departamento. Development obliga a redirigir todo al único buzón de prueba. Nunca probar con la audiencia real.
7. Solo con aprobación operativa de TI, cambiar a Production, desactivar `TEST_EMAIL_MODE` en las regionales autorizadas y activar `Operations:ScheduledJobsEnabled=true`. Revisar pendientes anteriores antes de activar entrega real: el enrutamiento usa el modo vigente al procesar, no el que existía al encolar.

Sin opt-in en Development se escribe Pickup; si la entrega externa está activada pero falta clave/remitente o destino válido, la cola queda retenida con aviso. Los mensajes se encolan junto a la transacción de negocio y se envían después del commit. Un fallo entre aceptación de SendGrid y confirmación en la base puede duplicar un envío. Monitorizar agotados, fallos, rebotes, cuota y almacenamiento de adjuntos. No hay webhooks de eventos de entrega en esta versión.

API oficial utilizada: [SendGrid Mail Send v3](https://www.twilio.com/docs/sendgrid/api-reference/mail-send/mail-send).

## 8. Publicar en IIS y configurar producción

1. Detener el sitio/Application Pool. Respaldar SQL y almacenamiento privado y desplegar el contenido de `artifacts\publish-v021` en la carpeta del sitio. No sobrescribir el almacén privado con un paquete vacío.
2. Configurar en el gestor de secretos o entorno de la cuenta de servicio las mismas claves, sustituyendo `:` por `__` en variables: `ConnectionStrings__DefaultConnection`, `Database__Provider=SqlServer`, `Storage__Path`, `Ad__Enabled`, `Ad__Endpoint`, `Ad__Token`, `Mail__Mode=SendGrid`, `Mail__From`, `SendGrid__ApiKey`, `SendGrid__DataResidency`, `Operations__Enabled`, `Operations__ScheduledJobsEnabled` y `AllowedHosts=dominio.institucional`.
3. Establecer `ASPNETCORE_ENVIRONMENT=Production`. Aplicar los valores AD nuevos solo al completar el punto 6. No guardar secretos en appsettings del repositorio, archivos publicados compartidos ni web.config versionado.
4. Configurar binding HTTPS y certificado válido. Si existe proxy, configurar de forma explícita sus hosts/redes de confianza y HTTPS antes de operar; la app no confía indiscriminadamente en cabeceras reenviadas.
5. En Windows las claves se cifran con DPAPI bajo la cuenta del proceso; conservar identidad, perfil y directorio privado `keys`. En otros sistemas configurar `DataProtection__CertificatePath` y `DataProtection__CertificatePassword` privados. Probar reinicio/login; una clave perdida exige renovar sesiones y no debe resolverse desactivando CSRF.
6. Iniciar y revisar migraciones, acceso AD, ámbito regional, cola y reportes. Los horarios usan America/Santo_Domingo. Los respaldos SQL no los ejecuta el worker web: TI debe programarlos y ensayar la restauración.

La API de menú existente acepta `GET /api/menu-day/yyyy-MM-dd?regionalId=ID` con `Authorization: Bearer <Integrations:MenuToken>`, nunca token en URL. Sin regionalId conserva el ámbito `central`. El token concede lectura institucional del menú regional indicado, no acceso a usuarios o pedidos; protegerlo como secreto.

## 9. Aceptación y mantenimiento

Antes de dar acceso nacional, verificar en homologación:

- Dos regionales con menú/proveedor/precio/valoraciones diferentes; USER/ADMIN_DEP/ADMIN_REG no pueden leer ni modificar otra regional, aunque manipulen IDs/cookies. ADMIN_GEN puede alternar contexto.
- ADMIN_REG no modifica identidades, roles, departamentos, categorías/reglas, configuración, correos, branding ni proveedor/precios. ADMIN_DEP consulta cuentas y recibe solo su reporte. Verificar también que una suspensión regional no puede retirar una deshabilitación general y que AD/solicitudes manipuladas no reactivan cuentas bloqueadas. Un departamento sin responsable se detecta y asigna explícitamente.
- Cambiar proveedor sin precio falla; con precio se guardan ambos y los históricos previos se conservan.
- AD actual funciona; al activar el nuevo contrato, USER nuevo entra en la organización verificada sin solicitud, y los casos negativos del punto 6 permanecen bloqueados.
- Reportes Excel/PDF, recordatorio y cierre usan la regional correcta; SendGrid entrega únicamente al buzón de prueba hasta autorizar producción.
- Bandeja Express funciona con flechas/A-D y botones táctiles, muestra dificultad y tiempo, oculta/restaura el pedido y aplica 15 minutos diarios aunque se recargue o abandone la partida. Abandonar consume la reserva de esa partida; el ranking nuevo comienza con su propia versión de puntuación.
- Escritorio/móvil: drawer a 1024 px, ajustes a 720 px, scroll de modales/tablas, sin desbordamiento de página. Preferencias, importación TSV multilínea, feriados, valoraciones y precios históricos deben contrastarse con la operación vigente.

Comprobación de snapshots para futuros cambios de entidades (migraciones separadas por proveedor):

```cmd
dotnet ef migrations has-pending-model-changes --project src\Almuerzo.Web --context AppDbContext --configuration Release
dotnet ef migrations has-pending-model-changes --project src\Almuerzo.Web --context SqlServerAppDbContext --configuration Release
```

Al crear nuevas migraciones, usar `--context AppDbContext --output-dir Data/Migrations` para SQLite y `--context SqlServerAppDbContext --output-dir Data/SqlServerMigrations` para SQL Server. Publicar de nuevo ASP.NET después de cualquier cambio web. Mantener seguimiento de dependencias, cuotas de correo, agotados de la cola, crecimiento de Reports, auditoría, respaldos y restauraciones.

## Verificación histórica de v0.2.0 · 16 de octubre de 2026

- Assets compilados y publicación Release generada sin errores; 74 pruebas correctas en SQLite y las mismas 74 en SQL Server LocalDB, sin pruebas omitidas.
- Snapshots de ambos contextos sin cambios de modelo pendientes. La copia local de la base existente se transfirió a SQL Server, reconciliando todas las tablas y preservando el SQLite original. Se comprobó el arranque del paquete con esa base.
- NuGet no reportó paquetes vulnerables. `npm audit` falló por conexión TLS del cliente; la consulta alternativa por HTTPS al mismo endpoint oficial revisó 80 paquetes del lockfile y devolvió cero paquetes con avisos, sin desactivar certificados.
- Navegador: selector regional, creación de regional sintética, precio obligatorio, final/guardado de partida, controles de teclado y táctiles, animación de la tarjeta y revisión a 390 y 1440 px. Sin desbordamiento horizontal, imágenes rotas ni errores de consola en las vistas comprobadas. Evidencias locales en `artifacts/qa-v020`, excluidas de Git.
- Paquete revisado sin bases, Excel, correos, almacenes privados ni secretos privados conocidos. Las pruebas automatizadas de SendGrid usan un handler simulado; no se enviaron correos externos.

Estos resultados no validan las credenciales de SendGrid, la recepción real de mensajes, el contrato AD ampliado aún no entregado ni el servidor SQL de TI. Esos pasos requieren homologación institucional antes de habilitar producción.

## Actualización v0.2.1

Las migraciones añaden `RegionalBlocked=false` y marcadores persistentes de autoridad AD en regionales/departamentos, en ambos proveedores; conservan los estados generales y todos los históricos. Se aplican automáticamente al arrancar. Los INACTIVO existentes no se reclasifican ni se retiran. Aplicar la matriz [ACCESS-CONTROL.md](ACCESS-CONTROL.md) al homologar esta actualización, incluyendo rechazo de payloads manipulados, revalidación de sesión, independencia de los dos bloqueos y responsables sin cambios implícitos de departamento.

La aplicación local del usuario permanece detenida después de este cambio; las comprobaciones visuales se realizan con datos sintéticos en un host temporal independiente. El contrato nuevo del AD y la entrega real de SendGrid siguen requiriendo homologación institucional.

Verificación del 19 de octubre de 2026: 85 pruebas correctas en SQLite y las mismas 85 en SQL Server LocalDB; ambos snapshots sin cambios pendientes, assets compilados y publicación Release generada. La demo aislada respondió HTTP 200. Se intentó la captura visual, pero el visor integrado no logró conectar su webview; no se afirma una nueva validación visual completa de esta versión. Se cerraron ambos hosts al finalizar. Las pruebas usan datos sintéticos y no envían correos externos.
