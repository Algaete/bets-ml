using System;

namespace CornersMLData.Models
{
    /// <summary>
    /// Modelo JSON usado por <c>POST /api/partidos/sincronizar</c> para insertar o actualizar partidos proximos manualmente.
    /// </summary>
    public sealed class PartidoProximoSyncRequest
    {
        /// <summary>
        /// Fecha y hora programada del partido.
        /// </summary>
        public DateTime FechaPartido { get; set; }

        /// <summary>
        /// Nombre del equipo local.
        /// </summary>
        public string EquipoLocal { get; set; } = "";

        /// <summary>
        /// Nombre del equipo visitante.
        /// </summary>
        public string EquipoVisita { get; set; } = "";

        /// <summary>
        /// Nombre de la liga o torneo.
        /// </summary>
        public string Liga { get; set; } = "";

        /// <summary>
        /// Genero del partido, por ejemplo <c>M</c> o <c>F</c>.
        /// </summary>
        public string Genero { get; set; } = "";

        /// <summary>
        /// Indica si el partido pertenece a una fase eliminatoria.
        /// </summary>
        public bool EsKnockout { get; set; }

        /// <summary>
        /// Cantidad total de equipos en la tabla cuando aplica.
        /// </summary>
        public int? TotalTeams { get; set; }

        /// <summary>
        /// Posicion actual del equipo local en la tabla cuando este dato existe.
        /// </summary>
        public int? HomeTeamPosition { get; set; }

        /// <summary>
        /// Posicion actual del equipo visitante en la tabla cuando este dato existe.
        /// </summary>
        public int? AwayTeamPosition { get; set; }
    }

    public sealed class PartidoProximoUpsertDto
    {
        public DateTime FechaPartido { get; set; }
        public string EquipoLocal { get; set; } = "";
        public string EquipoVisita { get; set; } = "";
        public string Liga { get; set; } = "";
        public string Genero { get; set; } = "";
        public bool EsKnockout { get; set; }
        public int? TotalTeams { get; set; }
        public int? HomeTeamPosition { get; set; }
        public int? AwayTeamPosition { get; set; }
    }

    /// <summary>
    /// Resumen de la sincronizacion manual de partidos proximos.
    /// </summary>
    public sealed class PartidosProximosSyncResponse
    {
        /// <summary>
        /// Mensaje de resultado de la operacion.
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Cantidad total de registros insertados o actualizados.
        /// </summary>
        public int TotalProcesados { get; set; }
    }

    /// <summary>
    /// Resumen de una sincronizacion automatica de partidos proximos obtenidos desde ESPN.
    /// </summary>
    public sealed class PartidosProximosAutoSyncResponse
    {
        /// <summary>
        /// Mensaje de resultado de la operacion.
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Fecha inicial consultada.
        /// </summary>
        public DateTime FechaDesde { get; set; }

        /// <summary>
        /// Fecha final consultada.
        /// </summary>
        public DateTime FechaHasta { get; set; }

        /// <summary>
        /// Cantidad de dias cubiertos por la corrida.
        /// </summary>
        public int Dias { get; set; }

        /// <summary>
        /// Cantidad de partidos descubiertos antes de persistir.
        /// </summary>
        public int TotalDescubiertos { get; set; }

        /// <summary>
        /// Cantidad de registros insertados o actualizados.
        /// </summary>
        public int TotalProcesados { get; set; }

        /// <summary>
        /// Desglose diario de partidos encontrados.
        /// </summary>
        public List<PartidoProximoResumenDiario> Diario { get; set; } = new();
    }

    /// <summary>
    /// Resumen diario de partidos descubiertos en una corrida automatica.
    /// </summary>
    public sealed class PartidoProximoResumenDiario
    {
        /// <summary>
        /// Fecha del grupo diario.
        /// </summary>
        public DateTime Fecha { get; set; }

        /// <summary>
        /// Cantidad de partidos encontrados para esa fecha.
        /// </summary>
        public int TotalPartidos { get; set; }
    }
}
