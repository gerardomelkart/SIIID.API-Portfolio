# SIIID · API de demostración

**C# · ASP.NET Core · .NET 10 · Validación · Aprobación · Conciliación**

Implementación independiente para portafolio del flujo mensual y su registro complementario BANCI. Usa exclusivamente identificadores y motivos ficticios. No contiene código de despliegue, credenciales, conexiones, catálogos, imágenes ni historial de los repositorios de trabajo.

## Tres componentes

| Repositorio | Responsabilidad |
|---|---|
| [SIIID2.FrontEnd-Portfolio](https://github.com/gerardomelkart/SIIID2.FrontEnd-Portfolio) | Interfaz Angular y ocho escenarios guiados |
| **SIIID.API-Portfolio** | Contratos HTTP y reglas ejecutables de esta demo |
| [SIIID2BD-Portfolio](https://github.com/gerardomelkart/SIIID2BD-Portfolio) | Modelo relacional y procedimiento transaccional de laboratorio |

El Front consume esta API. La API guarda el estado **en memoria**; reiniciarla inicia una demostración vacía. El modelo SQL se estudia y ejecuta por separado: esta edición no incluye un adaptador de persistencia SQL ni requiere una base de datos.

## Ejecutar

Requiere SDK .NET 10.

```bash
dotnet build src/Portfolio.Api.csproj -c Release
dotnet run --project tests/Portfolio.Checks.csproj -c Release
dotnet run --project src/Portfolio.Api.csproj
```

La API escucha en `http://127.0.0.1:5088`. En otra terminal inicia el Front siguiendo su README. Se permite CORS únicamente desde `localhost:4200` y `127.0.0.1:4200`. No hay autenticación ni autorización: la aprobación representa un paso funcional simulado. Usar únicamente para pruebas locales con datos ficticios, sin exponer el puerto mediante un proxy.

## Flujo que demuestra

1. Validar un mensual de dos víctimas que comparten carpeta. Se prepara una propuesta sin alterar el estado aprobado.
2. Aprobarlo y cargar los complementarios BANCI de ambas víctimas.
3. Reclasificar una víctima como `NO_DELITO`, con motivo obligatorio. Solo se aplica al aprobar; conserva el registro histórico y no altera a la otra víctima ni el mensual.
4. Intentar una actualización mensual contradictoria: la API la bloquea.
5. Retirar la reclasificada y agregar otra desaparición: se exige su complementario BANCI.
6. Aprobar la actualización con su complemento en una sola integración del estado.
7. Revertir la reclasificación: vuelve a exigirse esa desaparición en el siguiente mensual.

Se permite una propuesta pendiente global. Aprobar y rechazar requieren el identificador vigente; repetir una decisión se bloquea. Un bloqueo de proceso serializa las operaciones. Se revalida al aprobar y se conserva una bitácora de decisiones con hora UTC. Esto no sustituye una transacción distribuida ni una auditoría persistente.

## Contrato HTTP

| Método y ruta | Resultado |
|---|---|
| `GET /api/state` | Mensuales, histórico BANCI, propuesta pendiente y bitácora |
| `POST /api/proposals` | Valida y prepara `MONTHLY`, `REGISTRY`, `RECLASSIFY` o `REVERT` |
| `POST /api/proposals/{id}/approve` | Revalida e integra la propuesta pendiente |
| `POST /api/proposals/{id}/reject` | Descarta la propuesta sin modificar datos aprobados |

Periodos `AAAA-MM`; claves `DEMO-` alfanuméricas con guiones, de 6 a 40 caracteres. El mensual admite de 1 a 1000 filas completas por corte y delitos ficticios `DESAPARICION` u `OTRO`. BANCI también admite `NO_DELITO`; una víctima reclasificada así debe omitirse del mensual. Una reclasificada como `OTRO` puede omitirse o reportarse como `OTRO`. Los identificadores distinguen mayúsculas de minúsculas.

La carga inicial BANCI debe complementar exactamente todas las desapariciones del mensual aprobado. En las siguientes actualizaciones, los complementarios deben corresponder exclusivamente a desapariciones nuevas. Se protege la carpeta de las víctimas ya registradas. La demo no implementa captura de personas, CURP, catálogos oficiales, Excel, PDF, notificaciones, usuarios ni los módulos Federal y Semanal.

## Comprobaciones

El ejecutable de `tests` comprueba 20 condiciones: duplicados, claves y periodo inválidos, aislamiento del staging, pendiente único, correspondencia de complementarios, reclasificación individual, conservación del mensual e histórico, bloqueo de contradicciones, carpeta protegida, reversión, rechazo, decisión duplicada y aislamiento de los snapshots. No accede a servidores externos.

`tests/http_smoke.py` verifica el mismo recorrido por HTTP con la API recién iniciada:

```bash
python tests/http_smoke.py
```

## Alcance de portafolio

Demuestra separación entre validación y aplicación, trazabilidad de decisiones, consistencia entre módulos y manejo de errores. Es una versión reducida y autónoma; no es una copia funcional completa del sistema operativo ni un producto listo para producción. No se concede una licencia de reutilización por el solo hecho de ser público; las dependencias conservan sus licencias.
