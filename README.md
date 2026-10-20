# App Almuerzo Web · 0.2.3

ASP.NET Core 10 MVC, EF Core y **SQL Server**, con la interfaz de App_Almuerzo v7.36 adaptada a operación nacional. Cada regional tiene departamentos, menú, proveedor, precio, pedidos, valoraciones, configuración y reportes. La autorización se verifica en el servidor en cada petición.

Guía vigente para TI: [Despliegue nacional, SQL Server, AD y SendGrid](docs/DEPLOYMENT-NATIONAL.md), con comandos **CMD**, migración, configuración privada y pruebas. La BD real y el Excel se transfieren por un canal privado: **solicitar la copia actualizada inmediatamente antes del despliegue**.

## Cambios de esta versión

La [matriz de autoridad y permisos](docs/ACCESS-CONTROL.md) define quién puede modificar cada dato y las reglas de transición del AD. Nombre/código y organización verificada son de solo lectura incluso para ADMIN_GEN; no se crean identidades manuales. El bloqueo regional es independiente de la deshabilitación general y se comprueba en cada petición.

- `ADMIN_GEN`: administración nacional con selector de regional en el encabezado; administra alias y estados locales de organización y asigna `ADMIN_REG`.
- `ADMIN_REG`: gestiona menú, calendario, pedidos, reportes, avisos y suspensiones regionales de USER/ADMIN_DEP. Consulta identidades y departamentos. Roles, responsables, organización, categorías/reglas, configuración, correos, branding y proveedor/precios quedan reservados a ADMIN_GEN.
- Cambiar proveedor requiere escribir un precio válido, aunque coincida con el anterior. Ambos valores y la vigencia se guardan juntos, preservando los históricos.
- Nuevo AD NamesV1: lee CodigoEmpleado, Cargo, Departamento y Regional del DataTable/diffgram. Con opt-in crea organizaciones nuevas y USER; conserva nombres AD y permite alias solo a ADMIN_GEN. Catálogos importados requieren mapeos explícitos privados antes del primer vínculo. Los ADMIN_DEP se asignan expresamente desde Departamentos; reciben el resumen de su departamento y coordinan el retiro.
- SendGrid por API HTTPS, con adjuntos Excel/PDF, cola transaccional y reintentos. Pickup sigue siendo el modo inicial seguro; SMTP se conserva por compatibilidad.
- Migraciones específicas de SQL Server y transferencia offline desde SQLite, transaccional y reconciliada. SQLite sigue disponible para demo y pruebas.
- **Bandeja Express**: cuatro carriles, ingredientes y fuego, flechas/A-D y controles táctiles. Siete niveles de dificultad progresiva, partidas de hasta 45 segundos y máximo de 15 minutos diarios reservado en el servidor. El servidor reconstruye la puntuación; el ranking mensual usa la versión nueva del juego. La tarjeta del pedido se oculta con animación durante la partida y regresa al terminar.

## Ejecutar una demostración

Requisitos: .NET SDK 10 y Node.js 24. La demo no necesita AD, SendGrid ni SQL Server. Detener con `Ctrl+C` cualquier instancia que use la misma carpeta bin/Release antes de compilar, para evitar el bloqueo MSB3027 del ejecutable.

```cmd
cd /d C:\Dev\AppsWeb\App_Almuerzo
npm ci
npm run build
dotnet restore Almuerzo.sln
dotnet build Almuerzo.sln -c Release --no-restore
dotnet test Almuerzo.sln -c Release --no-build
set ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src\Almuerzo.Web -c Release --no-build --no-launch-profile --urls http://localhost:5062 -- --demo
```

Abrir `http://localhost:5062`. La demo usa SQLite independiente en `src/Almuerzo.Web/App_Data/demo`, cuenta sintética `demo.almuerzo@infotep.gob.do` y contraseña aleatoria disponible únicamente en `App_Data/demo/access.txt`. Nunca habilita cuentas de la base importada; `--demo` se rechaza en producción.

Editar `frontend/app.js`, `frontend/lunch-rush.js`, CSS y `src/Almuerzo.Web/Templates/app.html`, y ejecutar `npm run build`; los recursos en `wwwroot/assets` son generados. No ejecutar el portador inicial de Apps Script para sobrescribir la implementación actual.

## Datos existentes y SQL Server

El proveedor predeterminado es `Database:Provider=SqlServer`. Configurar `ConnectionStrings:DefaultConnection` por User Secrets en Development y por gestor de secretos/variables de entorno en producción. La conexión requiere cifrado y certificado válido; producción falla si falta la conexión. El arranque aplica migraciones y semillas seguras por regional, sin cuentas con contraseñas conocidas.

Para conservar la base SQLite existente, detener la app, configurar una **base SQL vacía** y ejecutar:

```cmd
scripts\Migrate-Sqlite.cmd C:\AlmuerzoPrivate\almuerzo.db
```

El proceso crea un snapshot temporal consistente, aplica migraciones al snapshot, copia todas las tablas dentro de una transacción y reconcilia conteos. No modifica el origen ni mezcla datos con un destino poblado. Los datos existentes quedan en `Sede inicial` (`central`); TI debe mapearla con su nombre AD conservando los IDs, y ADMIN_GEN puede definir su alias visible.

Para importar un Excel actualizado directamente en una base nueva:

```cmd
scripts\Import-Legacy.cmd C:\AlmuerzoPrivate\DB_Almuerzo.xlsx C:\AlmuerzoPrivate\storage
```

La importación reconoce el mismo libro por SHA-256, conserva históricos y preferencias y redacta secretos de Config. Un libro diferente requiere destino vacío. No borrar una base operativa para importar una nueva: usar otra base y programar el cambio con TI. Ver comandos del binario publicado en [la guía](docs/DEPLOYMENT-NATIONAL.md).

## Identidad, roles y correo

AD utiliza ASMX HTTPS POST: `Autenticacion` y `DatosUsuarioAD`, con `Token`, `NombreUsuario` y `Password` según el método. Se verifica el perfil del mismo usuario, sin XML DTD/entidades externas, con límites de tamaño y plazo. El código institucional único vincula la cuenta AD conservando correo e históricos importados; solo ADMIN_GEN puede restablecer un vínculo, con auditoría y revocación de sesión.

El endpoint de pruebas HTTP solo puede habilitarse de forma explícita en Development mediante `scripts\\Configure.cmd ad-http-test`, limitado a `ona-dis-2.infotep.gov.do`. Envía credenciales sin cifrar y requiere un token/cuenta de pruebas revocable; la aplicación rechaza esa opción fuera de Development y producción exige HTTPS. Ejecutar `scripts\\Configure.cmd ad` para volver al endpoint seguro.

Con el contrato antiguo, las cuentas nuevas solicitan acceso y un administrador asigna departamento/rol. Con `Ad:AutoProvisioningEnabled=true`, se exige organización verificada activa; con `Ad:OrganizationContract=NamesV1` y `Ad:AutoCreateOrganizationEnabled=true` se crean regionales/departamentos nuevos sin roles administrativos. Los catálogos importados se vinculan explícitamente antes de mover cuentas. Se crea/sincroniza USER automáticamente; nunca se reactivan cuentas INACTIVO ni se conceden privilegios administrativos. Los cambios organizativos retiran ADMIN_DEP al cambiar departamento y ADMIN_REG al cambiar regional. Un traslado verificado retira únicamente la suspensión de la regional anterior, con auditoría; una deshabilitación general se conserva. No se infiere el departamento por texto escrito por el usuario.

En Development el envío externo exige opt-in, `TEST_EMAIL_MODE=TRUE` y un único destinatario de prueba. Los trabajos y la cola comienzan desactivados. Cada regional tiene horario, calendario y destinatarios; ADMIN_GEN recibe consolidados regionales y ADMIN_REG el de su ámbito. HTTP 202 de SendGrid significa aceptación por el proveedor; entrega y rebotes se comprueban en SendGrid.

CONFIG permite favicon y sello PNG validados, sin metadata y almacenados de forma privada, y texto configurable. El crédito predeterminado es `INFOTEP - DIAET © 2026`; la firma incluida en el repo se usa por defecto. Los valores ya personalizados se conservan.

## Verificación y operación

La suite usa datos sintéticos: autenticación/CSRF, roles y departamentos, aislamiento regional, precios e históricos, AD/XXE/conflictos, menú/importación, reportes, SendGrid simulado y límites/puntuación del juego. También se ejecuta con SQL Server LocalDB:

```cmd
sqllocaldb start MSSQLLocalDB
set ALMUERZO_TEST_SQLSERVER=1
dotnet test Almuerzo.sln -c Release --no-build
set ALMUERZO_TEST_SQLSERVER=
```

Cada prueba SQL usa una BD temporal `AlmuerzoTest_<GUID>` y la elimina al terminar. Auditorías: `npm audit` y `dotnet list Almuerzo.sln package --vulnerable --include-transitive`. Mantener snapshots EF de ambos proveedores; ver comandos en la guía. La revisión visual incluye escritorio/móvil, selector regional, precio y juego sintéticos.

Producción requiere HTTPS, dominio en AllowedHosts, cuenta de servicio y almacenamiento privado persistente. Las claves de Data Protection se cifran con DPAPI en Windows o certificado en otros sistemas. TI administra respaldos SQL y pruebas de restauración. Operar **una instancia y un worker**: la cola no tiene bloqueo distribuido y un fallo después de aceptar un correo puede causar reintento duplicado. No se promete entrega exactamente una vez.

El contrato nuevo está implementado contra el XML entregado; faltan token, confianza TLS y homologación AD real, credenciales/conexión SQL institucional y recepción real de SendGrid. Seguir [AD nuevo, alias, CMD y configuración separada para TI](docs/AD-NAMES-AND-TI.md): `scripts\Configure.cmd map`, `scripts\Configure.cmd ad` y `scripts\Configure.cmd ti`. El asistente no cambia la conexión SQL local al configurar AD ni conecta al servidor para preparar el archivo de TI. Reglas de mantenimiento: [AGENTS.md](AGENTS.md). Historial de la migración inicial: [docs/MIGRATION.md](docs/MIGRATION.md).
