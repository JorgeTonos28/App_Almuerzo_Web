# App Almuerzo Web · 0.5.0

ASP.NET Core 10 MVC, EF Core y **SQL Server**, con la interfaz de App_Almuerzo v7.36 adaptada a operación nacional. Cada regional tiene grupos de almuerzo, catálogo institucional AD, menú, proveedor, precio, pedidos, valoraciones, configuración y reportes. La autorización se verifica en el servidor en cada petición.

Guía vigente para TI: [Despliegue nacional, SQL Server, AD y SendGrid](docs/DEPLOYMENT-NATIONAL.md), con comandos **CMD**, migración, configuración privada y pruebas. La BD real y el Excel se transfieren por un canal privado: **solicitar la copia actualizada inmediatamente antes del despliegue**.

## Cambios de esta versión

La [matriz de autoridad y permisos](docs/ACCESS-CONTROL.md) define quién puede modificar cada dato y las reglas de transición del AD. Nombre/código y organización verificada son de solo lectura incluso para ADMIN_GEN; no se crean identidades manuales. El bloqueo regional es independiente de la deshabilitación general y se comprueba en cada petición.

- `ADMIN_GEN`: administración nacional con selector de regional en el encabezado; administra alias y estados locales de organización y asigna `ADMIN_REG`.
- `ADMIN_REG`: gestiona menú, calendario, pedidos, reportes, avisos y suspensiones regionales de USER/ADMIN_DEP. Consulta identidades y departamentos. Roles, responsables, organización, categorías/reglas, configuración, correos, branding y proveedor/precios quedan reservados a ADMIN_GEN.
- Cambiar proveedor requiere escribir un precio válido, aunque coincida con el anterior. Ambos valores y la vigencia se guardan juntos, preservando los históricos.
- Nuevo AD NamesV1: lee CodigoEmpleado, Cargo, Departamento y Regional del DataTable/diffgram. Con opt-in crea organizaciones nuevas y USER; conserva nombres AD y permite alias solo a ADMIN_GEN. Catálogos importados requieren mapeos explícitos privados antes del primer vínculo. Los grupos operativos de Legacy se conservan; varios departamentos AD pueden compartir el mismo grupo. Los nuevos usuarios quedan PENDIENTE hasta que ADMIN_GEN asigne grupo y acceso. ADMIN_DEP gestiona el grupo y recibe su resumen conjunto. Un ADMIN_GEN/ADMIN_REG puede recibir también el resumen de su grupo sin cambiar de rol.
- SendGrid por API HTTPS, con adjuntos Excel/PDF, cola transaccional y reintentos. Pickup sigue siendo el modo inicial seguro; SMTP se conserva por compatibilidad.
- Migraciones específicas de SQL Server y transferencia offline desde SQLite, transaccional y reconciliada. SQLite sigue disponible para demo y pruebas.
- **Calendario automático dominicano**: feriados y traslados calculados en el servidor, sin depender de Google ni de acceso a Internet. Se combinan con cierres institucionales regionales y gobiernan menú, importación, pedidos y resúmenes. Los nacionales aparecen identificados y no pueden eliminarse desde una regional. Ver [fuentes, revisión anual y operación](docs/CALENDAR-AND-GAME.md).
- **Config por secciones**: Operación, Correos, Proveedor y precio, Apariencia y Avisos, con formularios, vista previa del cierre, historial de precios y guardado independiente. Solo ADMIN_GEN modifica Config; ADMIN_REG mantiene su editor de avisos.
- **Olla Express**: sesiones abiertas, detención voluntaria, 15 minutos diarios de tiempo jugado y guardado automático cada 10 s. Cuatro carriles conectados por los extremos, contacto con el borde de la olla, ocho niveles dinámicos, fuego y especiales raros (festín ×3, protección y ralentización). El servidor reproduce los controles y calcula los resultados. Se conserva el puntaje mensual anterior. Ranking nacional paginado y filtro de la regional del colaborador. La tarjeta del pedido se oculta durante la partida.

En v0.4.1, el juego interpola el movimiento en cada cuadro y usa transformaciones gráficas, con simulación del servidor en pasos de 20 ms. El nivel máximo cae aproximadamente un 34% más rápido. 🔥 💣 🦠 🪨 son variantes del mismo negativo: −12 puntos; cada tercer impacto diario sin protección añade −30/−60/−90… puntos y 3/6/9… segundos de pausa, consumiendo tiempo diario. El contador y la espera sobreviven a detener, recargar o iniciar sesión nuevamente. El escudo evita cualquier efecto del negativo, incluido romper racha o sumar impactos. Perder un ingrediente sigue rompiendo la racha.

En v0.4.2, Olla Express conserva la velocidad inicial y extiende la progresión mensual: niveles 1–8 desde 0, 500, 1,000, 1,500, 2,000, 2,500, 3,000 y 4,000 puntos. El último tramo abarca 3,000–3,999; el máximo reduce el recorrido de 1,280 a 1,040 ms (aproximadamente 23% más velocidad). Se conservan puntos, tiempo y penalizaciones anteriores. Recarga la página tras desplegar: los clientes con reglas antiguas deben actualizarse antes de iniciar una partida. El panel regional integra selector, catálogo y formulario en una tarjeta con estados de carga/guardado y diseño adaptable. Cambiar la ficha a editar no cambia la regional de trabajo; la identidad AD y los permisos mantienen sus reglas.

En v0.5.0, el menú semanal también se carga desde Excel `.xls`/`.xlsx`, con vista previa y confirmación, conservando el pegado de texto. En Administración → Menú → Importar menú semanal, **Descargar plantilla** genera el formato para la regional y semana seleccionadas; sus categorías tienen listas desplegables por día. **Cargar Excel** lee la plantilla completada o el formato de cinco columnas del proveedor. Ver [formato, fechas, validación y límites](docs/MENU-WORKBOOK.md). La lectura no guarda el archivo ni modifica menús; confirmar usa la operación transaccional existente, con validación por regional y avisos en cola. La carga valida categorías y menús en lote para evitar una consulta por plato.

Olla Express mantiene el nivel 8 desde 4,000 puntos. Sus recorridos por nivel son 3135/2810/2485/2160/1835/1510/1185/860 ms: cada nivel aumenta la velocidad y el máximo es aproximadamente 21% más rápido que v0.4.2. Al subir aparece «↑ Nivel N» durante 950 ms, sin bloquear controles, y el HUD indica «Nivel de velocidad: N». La simulación pasa a `pot-v5`, conserva estadísticas, tiempo y penalizaciones anteriores y exige recargar clientes antiguos.

## Ejecutar una demostración

Requisitos: .NET SDK 10 y Node.js 24. La demo no necesita AD, SendGrid ni SQL Server. Detener con `Ctrl+C` cualquier instancia que use la misma carpeta bin/Release antes de compilar, para evitar el bloqueo MSB3027 del ejecutable.

```cmd
cd /d C:\Dev\AppsWeb\App_Almuerzo
npm ci
npm run build
dotnet restore Almuerzo.sln
dotnet build Almuerzo.sln -c Release --no-restore
dotnet test Almuerzo.sln -c Release --no-build
npm run test:game
set ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src\Almuerzo.Web -c Release --no-build --no-launch-profile --urls http://localhost:5062 -- --demo
```

Abrir `http://localhost:5062`. La demo usa SQLite independiente en `src/Almuerzo.Web/App_Data/demo`, cuenta sintética `demo.almuerzo@infotep.gob.do` y contraseña aleatoria disponible únicamente en `App_Data/demo/access.txt`. Nunca habilita cuentas de la base importada; `--demo` se rechaza en producción.

El login se mantiene en `wwwroot/assets/auth.js`. Editar `frontend/app.js`, `frontend/lunch-rush.js`, CSS y `src/Almuerzo.Web/Templates/app.html`, y ejecutar `npm run build`; los recursos en `wwwroot/assets` son generados. No ejecutar el portador inicial de Apps Script para sobrescribir la implementación actual.

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

Con NamesV1, el AD determina identidad, cargo, regional y departamento institucional. Estos datos son de solo lectura. Los grupos de almuerzo son una agrupación local independiente: ADMIN_GEN puede nombrarlos, reunir personas de varios departamentos, asignar responsables y aprobar altas. Las cuentas importadas conservan su grupo, rol e históricos al verificar AD en la misma regional. Las nuevas quedan PENDIENTE y se encola un aviso a ADMIN_GEN una sola vez. Un cambio de departamento AD dentro de la misma regional conserva el grupo; un traslado regional retira grupo y ADMIN_DEP/ADMIN_REG y requiere revisión. INACTIVO y las suspensiones siguen bloqueando el acceso. Véase [modelo y flujo de grupos](docs/MEAL-GROUPS.md). El contrato Codes anterior conserva su comportamiento de compatibilidad.

El login valida con una petición en la misma página y navega a una ruta local de la misma pestaña. Conserva CSRF, rate limiting, cookies HttpOnly y un formulario de respaldo sin JavaScript.

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

La versión 0.4.0 no cambia entidades ni necesita una migración adicional. Al arrancar reconcilia pedidos activos presentes/futuros que coincidan con un feriado nacional: cancela cada uno una sola vez, registra auditoría y encola la notificación en la misma transacción. Conserva el menú y todos los históricos. TI debe revisar la cola y los pedidos afectados tras actualizar. Los correos se procesan según la configuración de envío vigente.

Producción requiere HTTPS, dominio en AllowedHosts, cuenta de servicio y almacenamiento privado persistente. Las claves de Data Protection se cifran con DPAPI en Windows o certificado en otros sistemas. TI administra respaldos SQL y pruebas de restauración. Operar **una instancia y un worker**: la cola no tiene bloqueo distribuido y un fallo después de aceptar un correo puede causar reintento duplicado. No se promete entrega exactamente una vez.

El contrato nuevo está implementado contra el XML entregado; faltan token, confianza TLS y homologación AD real, credenciales/conexión SQL institucional y recepción real de SendGrid. Seguir [AD nuevo, alias, CMD y configuración separada para TI](docs/AD-NAMES-AND-TI.md): `scripts\Configure.cmd map`, `scripts\Configure.cmd ad` y `scripts\Configure.cmd ti`. El asistente no cambia la conexión SQL local al configurar AD ni conecta al servidor para preparar el archivo de TI. Reglas de mantenimiento: [AGENTS.md](AGENTS.md). Historial de la migración inicial: [docs/MIGRATION.md](docs/MIGRATION.md).

Si un mapeo existente apunta a un ID que ya no está en el catálogo local, `scripts\Configure.cmd map` permite corregirlo solo después de escribir `RECONCILE_MAPPING`. Usa el ID real de `Regions`; el nombre/código recibido del AD nunca sustituye a ese ID.
