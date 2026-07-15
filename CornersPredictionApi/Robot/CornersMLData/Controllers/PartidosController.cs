using CornersMLData.Data;
using CornersMLData.Models;
using CornersMLData.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Controllers
{
    /// <summary>
    /// Endpoints para sincronizar y consultar partidos proximos en la tabla operativa de la API.
    /// </summary>
    [ApiController]
    [Route("api/partidos")]
    public class PartidosController : ControllerBase
    {
        private readonly PartidosProximosRepository _partidosProximosRepository;
        private readonly EspnPartidosProximosScraper _espnPartidosProximosScraper;
        private readonly ILogger<PartidosController> _logger;

        public PartidosController(
            PartidosProximosRepository partidosProximosRepository,
            EspnPartidosProximosScraper espnPartidosProximosScraper,
            ILogger<PartidosController> logger)
        {
            _partidosProximosRepository = partidosProximosRepository;
            _espnPartidosProximosScraper = espnPartidosProximosScraper;
            _logger = logger;
        }

        /// <summary>
        /// Verifica que la API este levantada sin requerir acceso a base de datos.
        /// </summary>
        /// <remarks>
        /// Endpoint de diagnostico sin parametros. Sirve para confirmar que el proceso web responde aunque SQL Server no este disponible.
        /// </remarks>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "API is running",
                timestamp = DateTime.UtcNow,
                message = "Use /api/partidos/test para probar sin base de datos"
            });
        }

        /// <summary>
        /// Devuelve una respuesta de prueba con datos de ejemplo para validar conectividad basica.
        /// </summary>
        /// <remarks>
        /// Endpoint de prueba sin parametros. Entrega un payload fijo para validar respuesta HTTP y formato JSON.
        /// </remarks>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                message = "Test endpoint - working without database",
                timestamp = DateTime.UtcNow,
                sampleData = new
                {
                    partidos = new[]
                    {
                        new { id = 1, local = "Colo-Colo", visitante = "Universidad de Chile", fecha = "2026-06-15 20:00" },
                        new { id = 2, local = "Bayer Leverkusen", visitante = "FC Bayern Munich", fecha = "2026-06-15 19:30" }
                    }
                }
            });
        }

        /// <summary>
        /// Inserta o actualiza una lista enviada manualmente de partidos proximos.
        /// </summary>
        /// <remarks>
        /// Recibe un arreglo JSON en el <c>body</c> con objetos <c>PartidoProximoSyncRequest</c>. Swagger muestra el esquema
        /// del modelo con los tipos esperados para fecha, texto, booleanos y posiciones opcionales.
        /// </remarks>
        /// <param name="partidos">Coleccion de partidos a sincronizar. Cada elemento debe incluir equipos, liga, fecha y genero.</param>
        /// <param name="cancellationToken">Token para cancelar la operacion HTTP.</param>
        [HttpPost("sincronizar")]
        [ProducesResponseType(typeof(PartidosProximosSyncResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Sincronizar(
            [FromBody] List<PartidoProximoSyncRequest>? partidos,
            CancellationToken cancellationToken)
        {
            if (partidos == null || partidos.Count == 0)
            {
                return BadRequest(new
                {
                    message = "Debes enviar al menos un partido para sincronizar."
                });
            }

            try
            {
                var normalized = partidos
                    .Select(NormalizeAndValidate)
                    .ToList();

                var totalProcesados = await _partidosProximosRepository.SincronizarAsync(normalized, cancellationToken);

                return StatusCode(StatusCodes.Status201Created, new PartidosProximosSyncResponse
                {
                    Message = "Partidos proximos sincronizados correctamente.",
                    TotalProcesados = totalProcesados
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validacion invalida en sincronizacion de partidos proximos.");
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL al sincronizar partidos proximos.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Ocurrio un error SQL al sincronizar los partidos.",
                    sqlNumber = ex.Number,
                    sqlMessage = ex.Message,
                    tip = "Verifica que SQL Server esté corriendo. Usa /api/partidos/health para verificar el estado."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al sincronizar partidos proximos.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Ocurrio un error interno al sincronizar los partidos."
                });
            }
        }

        /// <summary>
        /// Descubre partidos proximos desde ESPN dentro de un rango de fechas y los guarda en base de datos.
        /// </summary>
        /// <remarks>
        /// Recibe <c>fromDate</c> y <c>toDate</c> por <c>query string</c> como valores <c>date-time</c>. El rango se normaliza,
        /// admite hasta 31 dias y excluye partidos femeninos antes de persistirlos.
        /// </remarks>
        /// <param name="fromDate">Fecha inicial del rango a consultar.</param>
        /// <param name="toDate">Fecha final del rango a consultar.</param>
        /// <param name="cancellationToken">Token para cancelar la operacion HTTP.</param>
        [HttpPost("sincronizar/rango-fechas")]
        [ProducesResponseType(typeof(PartidosProximosAutoSyncResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SincronizarProximosPorFecha(
            [FromQuery] DateTime fromDate,
            [FromQuery] DateTime toDate,
            CancellationToken cancellationToken = default)
        {
            if (fromDate == default || toDate == default)
            {
                return BadRequest(new
                {
                    message = "Debes indicar fromDate y toDate."
                });
            }

            var start = fromDate.Date;
            var end = toDate.Date;
            if (end < start)
                (start, end) = (end, start);

            var totalDays = (end - start).Days + 1;
            if (totalDays > 31)
            {
                return BadRequest(new
                {
                    message = "El rango maximo permitido es de 31 dias."
                });
            }

            try
            {
                await _partidosProximosRepository.EnsureDatabaseObjectsAsync(cancellationToken);

                var partidos = await _espnPartidosProximosScraper.FetchUpcomingMatchesAsync(start, end, cancellationToken);
                var filtered = partidos
                    .Where(x => !IsWomenMatch(x.Liga, x.Genero))
                    .ToList();

                var totalProcesados = await _partidosProximosRepository.SincronizarAsync(filtered, cancellationToken);

                return StatusCode(StatusCodes.Status201Created, new PartidosProximosAutoSyncResponse
                {
                    Message = "Proximos partidos sincronizados desde ESPN correctamente.",
                    FechaDesde = start,
                    FechaHasta = end,
                    Dias = totalDays,
                    TotalDescubiertos = filtered.Count,
                    TotalProcesados = totalProcesados,
                    Diario = filtered
                        .GroupBy(x => x.FechaPartido.Date)
                        .OrderBy(x => x.Key)
                        .Select(x => new PartidoProximoResumenDiario
                        {
                            Fecha = x.Key,
                            TotalPartidos = x.Count()
                        })
                        .ToList()
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL al sincronizar proximos partidos por rango de fechas desde ESPN.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Ocurrio un error SQL al sincronizar los proximos partidos.",
                    sqlNumber = ex.Number,
                    sqlMessage = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al sincronizar proximos partidos por rango de fechas desde ESPN.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Ocurrio un error interno al sincronizar los proximos partidos."
                });
            }
        }

        /// <summary>
        /// Descubre partidos proximos desde hoy hasta la cantidad de dias indicada y los guarda en base de datos.
        /// </summary>
        /// <remarks>
        /// Recibe <c>days</c> por <c>query string</c> como entero. Si el valor es invalido se ajusta automaticamente al rango operativo permitido.
        /// </remarks>
        /// <param name="days">Cantidad de dias a incluir contando desde hoy.</param>
        /// <param name="cancellationToken">Token para cancelar la operacion HTTP.</param>
        [HttpPost("sincronizar/proximos")]
        [ProducesResponseType(typeof(PartidosProximosAutoSyncResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SincronizarProximos(
            [FromQuery] int days = 7,
            CancellationToken cancellationToken = default)
        {
            if (days <= 0)
                days = 7;

            if (days > 30)
                days = 30;

            try
            {
                var today = DateTime.Today;
                var fromDate = today;
                var toDate = today.AddDays(days - 1);

                await _partidosProximosRepository.EnsureDatabaseObjectsAsync(cancellationToken);

                var partidos = await _espnPartidosProximosScraper.FetchUpcomingMatchesAsync(fromDate, toDate, cancellationToken);
                var totalProcesados = await _partidosProximosRepository.SincronizarAsync(partidos, cancellationToken);

                return StatusCode(StatusCodes.Status201Created, new PartidosProximosAutoSyncResponse
                {
                    Message = "Proximos partidos sincronizados desde ESPN correctamente.",
                    FechaDesde = fromDate,
                    FechaHasta = toDate,
                    Dias = days,
                    TotalDescubiertos = partidos.Count,
                    TotalProcesados = totalProcesados,
                    Diario = partidos
                        .GroupBy(x => x.FechaPartido.Date)
                        .OrderBy(x => x.Key)
                        .Select(x => new PartidoProximoResumenDiario
                        {
                            Fecha = x.Key,
                            TotalPartidos = x.Count()
                        })
                        .ToList()
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL al sincronizar proximos partidos desde ESPN.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Ocurrio un error SQL al sincronizar los proximos partidos.",
                    sqlNumber = ex.Number,
                    sqlMessage = ex.Message,
                    tip = ex.Number == 10061 
                        ? "Error 10061: No se puede conectar a SQL Server. Verifica que SQL Server esté corriendo y accesible."
                        : "Verifica la configuración de la base de datos."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al sincronizar proximos partidos desde ESPN.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Ocurrio un error interno al sincronizar los proximos partidos."
                });
            }
        }

        private static PartidoProximoUpsertDto NormalizeAndValidate(PartidoProximoSyncRequest request)
        {
            var dto = new PartidoProximoUpsertDto
            {
                FechaPartido = request.FechaPartido,
                EquipoLocal = NormalizeRequired(request.EquipoLocal, nameof(request.EquipoLocal)),
                EquipoVisita = NormalizeRequired(request.EquipoVisita, nameof(request.EquipoVisita)),
                Liga = NormalizeRequired(request.Liga, nameof(request.Liga)),
                Genero = NormalizeRequired(request.Genero, nameof(request.Genero)),
                EsKnockout = request.EsKnockout
            };

            if (dto.FechaPartido == default)
                throw new ArgumentException("FechaPartido es obligatorio.");

            if (dto.EquipoLocal.Equals(dto.EquipoVisita, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("EquipoLocal y EquipoVisita no pueden ser iguales.");

            return dto;
        }

        private static string NormalizeRequired(string? value, string fieldName)
        {
            var normalized = NormalizeNullable(value);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new ArgumentException($"{fieldName} es obligatorio.");

            return normalized;
        }

        private static string? NormalizeNullable(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = Regex.Replace(value, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static bool IsWomenMatch(string? league, string? genero)
        {
            if (string.Equals(genero, "Femenino", StringComparison.OrdinalIgnoreCase))
                return true;

            var normalizedLeague = NormalizeNullable(league)?.ToLowerInvariant() ?? string.Empty;
            return normalizedLeague.Contains("women")
                || normalizedLeague.Contains("femen")
                || normalizedLeague.Contains("liga f")
                || normalizedLeague.Contains("premiere ligue")
                || normalizedLeague.Contains("nwsl")
                || normalizedLeague.Contains("northern super league");
        }
    }
}
