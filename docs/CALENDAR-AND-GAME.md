# Calendario, configuración y juego · v0.5.0

## Feriados y cierres

Legacy v7.36 combinaba `DiasLibres` con el calendario Google `es.do#holiday@group.v.calendar.google.com`, con caché de seis horas. La primera migración solo consultaba los días importados/manuales: faltaba la fuente automática. Desde v0.4.0 el calendario nacional se calcula localmente; no requiere Google, token ni conectividad. No incluye efemérides que sean laborables.

Fuente de validación para 2026: [comunicado del Ministerio de Trabajo reproducido por CERTV](https://centralnoticias.gob.do/ministerio-de-trabajo-informa-dias-feriados-correspondientes-al-ano-2026/). Reglas de traslado: [Ley 139-97, Consultoría Jurídica](https://www.consultoria.gov.do/Consulta/Home/FileManagement?documentId=3312522&managementType=1). Reyes, Duarte, Trabajo y Constitución se trasladan cuando corresponde: martes/miércoles al lunes anterior; jueves/viernes al siguiente. Año Nuevo, Altagracia, Independencia, Restauración, Mercedes y Navidad permanecen en su fecha. Viernes Santo y Corpus Christi se calculan respecto a Pascua.

El módulo Calendario muestra el año anterior, actual y siguiente, con el origen de cada fecha. Los días nacionales aplican a todas las regionales y no se borran desde el panel. ADMIN_GEN/ADMIN_REG pueden agregar y retirar cierres adicionales en su regional; agregar uno cancela sus pedidos activos y encola los avisos. No borra históricos. Un cierre manual importado en una fecha nacional no convierte ese feriado en editable.

La misma regla del servidor excluye esas fechas al guardar/importar menú, presentar fechas para pedir y calcular el siguiente día hábil, cierres y trabajos de correo. Un viernes previo a un lunes feriado cierra el pedido del martes. Los fines de semana continúan excluidos. Al actualizar, se cancelan transaccionalmente pedidos activos presentes/futuros en feriados nacionales, con auditoría y notificación idempotente. No se modifican pedidos históricos ni se borra el menú importado.

TI/administración debe contrastar el calendario con el comunicado anual del Ministerio de Trabajo y registrar cierres excepcionales institucionales. Si cambia una regla legal nacional, requiere actualizar el código y desplegar. El cálculo automático no sustituye esa revisión anual.

## Configuración

Solo ADMIN_GEN ve los formularios Operación, Correos, Proveedor y precio, Apariencia y Avisos. Los valores operativos corresponden a la regional seleccionada. Identidad visual conserva el comportamiento y permisos existentes. ADMIN_REG conserva únicamente la edición de avisos de su regional.

Cada sección guarda solo sus campos; no sobrescribe otras secciones ni claves heredadas. Los horarios usan Santo Domingo; el cierre se muestra como hora de envío menos anticipación. El servidor rechaza recordatorios posteriores al cierre y cierres que retrocedan al día anterior. Precio conserva vigencia e historial; cambiar proveedor exige precio explícito en una operación atómica.

Correos distingue destinatarios del consolidado, copias, responsables de grupo y destinatario de prueba. La configuración de SendGrid/SMTP y sus secretos permanece en el servidor. No se envían pruebas automáticamente. Desarrollo conserva opt-in y destinatario único; una aceptación de SendGrid no confirma recepción.

## Olla Express

El jugador puede repartir 900 segundos diarios entre las sesiones que prefiera. Detener y guardar, Escape o cambiar de pestaña finaliza la sesión. Se descuentan los segundos jugados, redondeados al siguiente segundo. El guardado periódico es un punto de control, no una ronda: no interrumpe el juego. Las sesiones se cierran al agotar el tiempo o al cambiar el día en Santo Domingo.

El servidor mantiene una reserva renovable de 20 segundos y verifica cada 10 segundos. Una segunda pestaña no puede iniciar otra sesión durante la reserva. Al perder conectividad, la interfaz detiene el juego y permite reintentar el guardado congelado. Si se cierra abruptamente el navegador, se conserva el último puntaje confirmado y se consume como máximo la reserva de 20 segundos posterior al último control. El cliente intenta guardar al salir; esto no se garantiza al cerrar un proceso o apagar el equipo.

Cuatro carriles, controles ←/→ o A/D y botones táctiles. Los extremos se conectan. Un ingrediente se recoge desde el primer contacto con el borde superior de la olla, con una ventana tolerante. Desde v0.4.1 la simulación usa pasos de 20 ms, controles separados por al menos 100 ms y velocidad limitada. La presentación interpola cada cuadro con transformaciones `translate3d`, sin modificar la física ni los resultados. La altura se mide al iniciar o redimensionar, sin lecturas de geometría por cuadro. La caída máxima pasa de 1720 a 1280 ms de recorrido: aproximadamente 34% más velocidad. La frecuencia máxima también aumenta.

- Ingrediente: 10 puntos más 1 por cada cinco aciertos consecutivos, hasta 5 adicionales. Perder comida rompe la racha.
- Negativos 🔥 💣 🦠 🪨: son variantes visuales de la misma mecánica: −12 puntos y rompe la racha. No reduce el total por debajo de cero. Se fuerza presencia regular y se añade una probabilidad moderada; no aparecen durante Festín.
- Cada tres negativos sin protección en el día, aunque haya aciertos entre ellos: penalización adicional de 30 puntos y pausa de 3 segundos la primera vez; 60 puntos/6 segundos la segunda; 90/9 la tercera, y así sucesivamente. Durante la pausa se despeja el tablero, no se generan ingredientes y se bloquean los controles; no se penalizan ingredientes omitidos durante esa espera. El reloj y el guardado automático continúan. Se conserva una breve entrada de ingredientes al reanudar. La espera queda limitada por el tiempo diario disponible y el fin del día.
- ⭐ Festín: durante 10 segundos, puntos ×3; elimina los negativos presentes e impide negativos nuevos.
- 🛡️ Protección: ignora los negativos durante 10 segundos; conserva puntos, racha y contador de impactos. Los ingredientes que se dejen caer siguen rompiendo la racha y tienen un mensaje explícito.
- 🧊 Frío: ralentiza durante 8 segundos. Solo aparece desde nivel 3.

Los especiales no están garantizados: aproximadamente 3% en simulaciones sintéticas, con mínimo 18 segundos entre apariciones. Los efectos diferentes pueden coexistir. Un efecto termina por tiempo, no por detener el juego; una sesión nueva comienza sin efectos ni racha. Los puntos mensuales y mejor racha se conservan.

Desde v0.4.2 los umbrales mensuales de los niveles 1–8 son 0, 500, 1000, 1500, 2000, 2500, 3000 y 4000 puntos. El nivel 7 abarca 3000–3999 para que el máximo se active exactamente en 4000. La dificultad cambia inmediatamente al ganar o perder puntos y se conserva al iniciar otra sesión. La caída inicial mantiene 3135 ms por recorrido; el máximo baja de 1280 a 1040 ms (23% más velocidad). Se interpola por nivel mediante aritmética entera idéntica en navegador/servidor; la aparición conserva sus topes de 1070–510 ms. La ventana de captura máxima sigue cubriendo varios pasos de simulación; no se aceleran los controles mínimos de 100 ms ni se altera la mecánica de protección o penalizaciones.

En v0.5.0 los recorridos se ajustan a 3135/2810/2485/2160/1835/1510/1185/860 ms (niveles 1–8). El descenso disminuye 325 ms por nivel: también se aceleran los niveles intermedios, conservando el inicio. El máximo aumenta aproximadamente 21% frente a v0.4.2. El aviso «↑ Nivel N» aparece únicamente al subir durante una sesión, dura 950 ms y no captura clics ni controles. Se oculta al bajar o detener; se adapta a movimiento reducido. Iniciar con puntaje acumulado alto no simula una subida. El HUD muestra «Nivel de velocidad: N». `pot-v5` conserva la información de `pot-v4` y anteriores; las partidas/clientes con reglas antiguas requieren recargar.

El contador de negativos y la escalada son diarios, persisten en GameJson y se verifican en el servidor. Detener durante una pausa guarda el resultado y permite cerrar la partida, pero no comenzar otra hasta que transcurra la espera; el presupuesto diario sigue disminuyendo. Al volver a cargar o autenticar se recupera la espera restante. El servidor calcula la deuda temporal sin descontarla dos veces, incluso si la pausa supera la reserva de 20 segundos. Los negativos protegidos no cuentan. Se reinician al cambiar el día en Santo Domingo; el puntaje continúa siendo mensual. La versión `pot-v5` conserva puntajes, tiempo diario, impactos y esperas de `pot-v3` y `pot-v4`. Tras desplegar hay que recargar el navegador: iniciar con reglas anteriores se rechaza sin consumir tiempo, y una partida de la versión anterior no se reproduce con las reglas nuevas. Su último resultado confirmado y reserva temporal se conservan.

Navegador y servidor comparten reglas equivalentes deterministas. El servidor ignora puntuaciones enviadas por el cliente, reproduce movimientos/capturas/efectos, rechaza controles imposibles, tiempos futuros y reescritura de un tramo confirmado. El reintento final es idempotente. Esto verifica integridad de resultados; no pretende distinguir una persona de controles automatizados.

El ranking mensual es nacional por defecto y paginado de 50 en 50. “Mi regional” usa la regional real del colaborador, incluso cuando ADMIN_GEN consulta otra regional administrativa. Solo participan cuentas habilitadas, no suspendidas, de regional y grupo activos. Se exponen nombre, regional, grupo y estadísticas del juego; no códigos, correos, datos AD ni pedidos. Se conserva el puntaje `rush-v1` durante el mes de transición.

## Verificar

`dotnet test Almuerzo.sln -c Release` en SQLite y con `ALMUERZO_TEST_SQLSERVER=1` en SQL Server. `npm run test:game` valida contacto, efectos, distribución y vectores que también comprueba la simulación C#. Probar manualmente controles, envoltura de extremos, Detener/reanudar, cambio de pestaña, pérdida de conexión y vistas de escritorio/móvil. Las pruebas usan datos sintéticos y no envían correos externos.
