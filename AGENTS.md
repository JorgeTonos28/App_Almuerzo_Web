# Guía operativa — App Almuerzo Web

## Seguridad obligatoria en cada cambio
Toda implementación debe evaluar autenticación, autorización, aislamiento por departamento, validación del servidor, integridad, CSRF, XSS, sesiones, rate limiting, dependencias, errores y tratamiento de datos personales. Denegar por defecto. La interfaz nunca es una barrera de autorización. La identidad procede exclusivamente del proveedor AD; roles, habilitación y departamentos proceden de esta base de datos y se verifican en cada petición.

Nunca versionar datos reales, Excel de origen, bases de datos, respaldos, tokens, contraseñas, correos generados ni configuraciones privadas. Usar User Secrets en desarrollo y variables de entorno/gestor de secretos en producción. No imprimir credenciales ni respuestas completas del proveedor de identidad. No habilitar cuentas ni asignar roles automáticamente por pertenecer al AD.

## Paridad y versión
La referencia funcional y visual es App_Almuerzo v7.36. Conservar módulos, diseño responsive, reglas de menú, importación TSV multilínea, preferencias, avisos, precios históricos y roles USER/ADMIN_DEP/ADMIN_GEN. Incrementar la versión del proyecto antes de cambios funcionales y mencionarla en la entrega. Las reglas de Apps Script (clasp, manifest, triggers) se reemplazan por los mecanismos ASP.NET equivalentes; no modificar el repositorio de referencia.

## Datos y migraciones
Los cambios de entidades requieren migraciones EF Core, snapshot y ajustes de SeedData. El arranque ejecuta MigrateAsync y semillas idempotentes sin pasos manuales. Las semillas solo incluyen catálogos y valores seguros, nunca cuentas con contraseñas conocidas. La importación debe ser transaccional, trazable, repetible y reconciliada, sin borrar históricos ni resolver conflictos silenciosamente.

## Documentación y verificación
Actualizar README en la misma entrega si cambia comportamiento, setup o despliegue. Probar compilación, autorización por rol/departamento, casos negativos, integridad y cambios de negocio. Validar escritorio y móvil (drawer <=1024px, ajustes <=720px), overflow de tablas y scroll. Intentar capturas y comunicar limitaciones reales. Toda operación asíncrona visible debe mostrar estado de carga; las secundarias no deben bloquear toda la página.

## Preferencias y rendimiento
Fusionar preferencias JSON sin destruir claves existentes; persistir descartes con límites. Normalizar texto de menú sin ALL CAPS. Evitar roundtrips redundantes, consultas dentro de ciclos y recargas globales para cambios locales. Consultar históricos con filtros e índices. Los correos se encolan junto a la operación y se procesan después del commit con reintentos.

## Entrega
No afirmar paridad o producción lista cuando haya funcionalidades o integraciones pendientes. Informar pruebas ejecutadas, límites de validación y requisitos reales de configuración. Los cambios web requieren un nuevo despliegue ASP.NET; no publicar ni subir datos privados.
