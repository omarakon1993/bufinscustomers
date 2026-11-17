using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace bufinscustomers.Services
{
    // Api: parte de CargueOrquestadorService (misma clase; solo se separa el codigo por responsabilidad).
    public partial class CargueOrquestadorService
    {
        // ═════════════════════════════════════════════════════════════════════════
        // 5. Envío por tandas (API en JSON): abrir lote → agregar hojas → validar
        //    El resto del flujo (revisión, confirmar, descartar) es el de siempre.
        // ═════════════════════════════════════════════════════════════════════════
        private const int MaxFilasPorLlamada = 50000;

        private static readonly HashSet<string> ColumnasDeSistemaApi = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Llave", "IdEmpresa_Log", "IdEscenario", "IdUsuarioCargue_Log", "FechaCargue_Log", "Historico_Log",
            "IdUsuarioEjecucion_Log", "FechaEjecucion_Log", "Observacion_Log", "IdLote", "NumeroFilaExcel"
        };

        /// <summary>Abre un lote vacío en staging (sin archivo) para llenarlo por hojas. Misma llave y mismas reglas que un cargue de Excel.</summary>
        public ResultadoProcesoCargue IniciarLoteSinArchivo(SolicitudCargue s, Action<long> alCrearLote = null)
        {
            bool modoEjecucion = string.Equals(s.Modo, "ejecucion", StringComparison.OrdinalIgnoreCase);
            byte modo = modoEjecucion ? (byte)0 : (byte)1;
            int idUsuario = s.Usuario?.Id ?? 0;
            string referencia = string.IsNullOrWhiteSpace(s.NombreArchivo) ? "API" : s.NombreArchivo.Trim();
            if (referencia.Length > 200) referencia = referencia.Substring(0, 200);

            _stagingService.PurgarLotesVencidosSiToca();
            try { _stagingService.DescartarLotesPendientesDeUsuario(idUsuario, s.IdEmpresa); }
            catch (Exception exDesc) { AppLogger.Error(exDesc, "IniciarLoteSinArchivo.DescartarLotesPendientesDeUsuario"); }

            string nombreEmpresa = _empresaService.ObtenerEmpresas().Find(e => e.Id == s.IdEmpresa)?.Nombre ?? "Desconocida";
            string nombreUsuario = ((s.Usuario?.Nombre ?? "") + " " + (s.Usuario?.Apellidos ?? "")).Trim();

            var (creado, idLote, mensaje) = _stagingService.CrearLote(s.IdEmpresa, nombreEmpresa, s.Anio, modo, s.IdEscenario, idUsuario, nombreUsuario, referencia);
            if (!creado)
                return new ResultadoProcesoCargue { Estado = ProcesoCargueEstado.LoteActivoExistente, IdLote = idLote, Mensaje = mensaje, NombreArchivo = referencia };

            alCrearLote?.Invoke(idLote);
            return new ResultadoProcesoCargue { Estado = ProcesoCargueEstado.EnRevision, IdLote = idLote, NombreArchivo = referencia };
        }

        /// <summary>
        /// Agrega (o reemplaza) las filas de una hoja en el staging del lote. Las filas llegan ya como valores simples
        /// (número → double, texto → string, Sí/No → bool, vacío → null). Todo o nada: si una fila no se puede guardar, no queda ninguna.
        /// El lote vuelve a «EnValidacion»: hay que volver a validarlo antes de poder confirmarlo.
        /// </summary>
        public ResultadoHojaApi AgregarFilasHoja(Usuarios usuario, long idLote, string hoja, IReadOnlyList<IDictionary<string, object>> filas, bool reemplazar)
        {
            var res = new ResultadoHojaApi { Hoja = hoja, FilasRecibidas = filas?.Count ?? 0 };
            var lote = _stagingService.ObtenerLote(idLote);
            if (lote == null || !EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa)
                || lote.Estado == CargueLoteEstado.Confirmado || lote.Estado == CargueLoteEstado.Descartado)
            { res.Estado = HojaApiEstado.LoteNoDisponible; return res; }

            var mapeo = TablasCargueHelper.MapeoParaEmpresa(lote.IdEmpresa);
            string clave = NormalizarNombre(hoja ?? "");
            if (!mapeo.TryGetValue(clave, out string tablaIni))
            { res.Estado = HojaApiEstado.HojaNoSoportada; res.Mensaje = hoja; return res; }
            res.Hoja = clave; res.Tabla = tablaIni;

            if (res.FilasRecibidas > MaxFilasPorLlamada)
            { res.Estado = HojaApiEstado.DemasiadasFilas; res.Mensaje = MaxFilasPorLlamada.ToString(); return res; }

            string nombreStaging = TablasCargueHelper.NombreStaging(tablaIni);

            // Columnas de negocio de la hoja (esquema real de la tabla de staging); los nombres que llegan se resuelven contra él.
            var columnas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Bd.Leer(CadenaConexion, "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t AND COLUMNPROPERTY(OBJECT_ID(QUOTENAME(TABLE_SCHEMA) + '.' + QUOTENAME(TABLE_NAME)), COLUMN_NAME, 'IsIdentity') = 0",
                new { t = nombreStaging },
                r =>
                {
                    string nombre = r.GetString(0);
                    if (!ColumnasDeSistemaApi.Contains(nombre)) columnas[nombre] = nombre;
                });
            if (columnas.Count == 0)
            { res.Estado = HojaApiEstado.DatosInvalidos; res.Mensaje = "La hoja no tiene una tabla de destino disponible."; return res; }
            if (columnas.ContainsKey("Año")) columnas["Anio"] = "Año";   // alias sin tilde

            // Construye el DataTable con los mismos tipos que entrega la lectura de Excel (double / string).
            var usadas = new List<string>();
            foreach (var fila in filas)
                foreach (var k in fila.Keys)
                {
                    if (!columnas.TryGetValue((k ?? "").Trim(), out string canon))
                    { res.Estado = HojaApiEstado.ColumnaDesconocida; res.Mensaje = k; return res; }
                    if (!usadas.Contains(canon)) usadas.Add(canon);
                }

            var dt = new DataTable(clave);
            foreach (var c in usadas) dt.Columns.Add(c, typeof(object));
            string anioTexto = lote.Anio.ToString();
            int omitidas = 0;
            foreach (var fila in filas)
            {
                var dr = dt.NewRow();
                foreach (DataColumn col in dt.Columns) dr[col] = "";
                bool vacia = true;
                foreach (var kv in fila)
                {
                    string canon = columnas[(kv.Key ?? "").Trim()];
                    object v = kv.Value;
                    object celda;
                    if (v is double d) { celda = d; vacia = false; }
                    else
                    {
                        string txt = v == null ? "" : (v is bool b ? (b ? "true" : "false") : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture).Trim());
                        if (txt.Length > 0) vacia = false;
                        celda = txt;
                    }
                    dr[canon] = celda;
                }
                if (vacia) continue;

                // Igual que el Excel: una fila de otro año se omite en silencio (y se cuenta para avisar).
                if (usadas.Contains("Año"))
                {
                    string a = dr["Año"] is double da ? ((long)da).ToString() : Convert.ToString(dr["Año"]).Trim();
                    if (a.Length > 0 && a != anioTexto) { omitidas++; continue; }
                }
                dt.Rows.Add(dr);
            }
            res.FilasOmitidasPorAnio = omitidas;

            using (var conn = new SqlConnection(CadenaConexion))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        int existentes = 0;
                        if (!reemplazar)
                            using (var cmdC = new SqlCommand($"SELECT COUNT(1) FROM dbo.[{nombreStaging}] WHERE IdLote = @L", conn, tx))
                            {
                                cmdC.Parameters.AddWithValue("@L", idLote);
                                existentes = Convert.ToInt32(cmdC.ExecuteScalar());
                            }
                        else
                            using (var cmdD = new SqlCommand($"DELETE FROM dbo.[{nombreStaging}] WHERE IdLote = @L", conn, tx))
                            {
                                cmdD.Parameters.AddWithValue("@L", idLote);
                                cmdD.ExecuteNonQuery();
                            }

                        if (dt.Rows.Count > 0)
                        {
                            UltimoMensajeHoja = null;
                            if (!GuardarEnIni(conn, dt, nombreStaging, lote.IdEmpresa, usuario?.Id ?? 0, lote.IdEscenario, historicoLog: lote.Modo, tx: tx, idLote: idLote, numeroFilaBase: existentes + 2))
                            {
                                tx.Rollback();
                                res.Estado = HojaApiEstado.DatosInvalidos;
                                res.Mensaje = (UltimoMensajeHoja ?? "").Replace("Staging_Ini_", "Ini_");
                                return res;
                            }
                        }

                        int total = existentes + dt.Rows.Count;
                        using (var cmdH = new SqlCommand(@"
                            UPDATE dbo.CarguesLotesHojas SET Filas = @F, Declarada = GETDATE() WHERE IdLote = @L AND Hoja = @H;
                            IF @@ROWCOUNT = 0 INSERT INTO dbo.CarguesLotesHojas (IdLote, Hoja, Filas) VALUES (@L, @H, @F);", conn, tx))
                        {
                            cmdH.Parameters.AddWithValue("@L", idLote);
                            cmdH.Parameters.AddWithValue("@H", clave);
                            cmdH.Parameters.AddWithValue("@F", total);
                            cmdH.ExecuteNonQuery();
                        }

                        _stagingService.ReabrirParaEdicion(conn, tx, idLote);
                        tx.Commit();

                        res.Estado = HojaApiEstado.Ok;
                        res.FilasGuardadas = dt.Rows.Count;
                        res.FilasEnHoja = total;
                        return res;
                    }
                    catch (Exception ex)
                    {
                        Helpers.TransaccionHelper.RollbackSeguro(tx);
                        AppLogger.Error(ex, "CargueOrquestador.AgregarFilasHoja");
                        res.Estado = HojaApiEstado.DatosInvalidos;
                        res.Mensaje = "No se pudo guardar la hoja. Inténtelo de nuevo o contacte a soporte.";
                        return res;
                    }
                }
            }
        }

        /// <summary>
        /// Cierra el envío por hojas y lo valida: exige que se haya declarado CADA hoja de la empresa (aunque sea vacía; una hoja olvidada
        /// borraría sus datos al confirmar), que el archivo traiga al menos una fila del año, y corre las mismas reglas del Excel.
        /// </summary>
        public ResultadoValidacionApi ValidarLoteApi(Usuarios usuario, long idLote)
        {
            var lote = _stagingService.ObtenerLote(idLote);
            if (lote == null || !EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa)
                || lote.Estado == CargueLoteEstado.Confirmado || lote.Estado == CargueLoteEstado.Descartado)
                return new ResultadoValidacionApi { Estado = ValidacionApiEstado.LoteNoDisponible };

            var declaradas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Bd.Leer(CadenaConexion, "SELECT Hoja, Filas FROM dbo.CarguesLotesHojas WHERE IdLote = @L",
                new { L = idLote },
                r => declaradas[r.GetString(0)] = r.GetInt32(1));

            var faltantes = TablasCargueHelper.MapeoParaEmpresa(lote.IdEmpresa).Keys
                .Where(k => !declaradas.ContainsKey(NormalizarNombre(k))).ToList();
            if (faltantes.Count > 0)
                return new ResultadoValidacionApi
                {
                    Estado = ValidacionApiEstado.HojasFaltantes, HojasFaltantes = faltantes, Lote = lote,
                    Mensaje = "Faltan hojas por enviar (envíelas aunque vayan sin filas): " + string.Join(", ", faltantes) + "."
                };

            int totalFilas = declaradas.Values.Sum();
            if (totalFilas == 0)
                return new ResultadoValidacionApi
                {
                    Estado = ValidacionApiEstado.SinFilasDelAnio, Lote = lote,
                    Mensaje = string.Format(T("Datos_CargueSinFilasDelAnio"), lote.Anio)
                };

            _stagingService.ActualizarTotalFilas(idLote, totalFilas);
            var (ok, mensaje, _, _) = _stagingService.ValidarLote(idLote);
            if (!ok)
            {
                AppLogger.Error($"sp_ValidarCargueStaging falló para el lote {idLote}: {mensaje}", contexto: "ValidarLoteApi");
                return new ResultadoValidacionApi { Estado = ValidacionApiEstado.ErrorValidacion, Mensaje = mensaje, Lote = lote };
            }
            return new ResultadoValidacionApi { Estado = ValidacionApiEstado.Ok, Lote = _stagingService.ObtenerLote(idLote) };
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 6. Aviso a los revisores de la empresa cuando la API deja un cargue esperando
        // ═════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// Avisa (campana) a los administradores de la empresa que un cargue enviado por la API espera revisión, o que llegó con errores.
        /// Va en español: los destinatarios son usuarios de la empresa, no el cliente que llama a la API. Nunca lanza.
        /// </summary>
        public void NotificarRevisores(CargueLote lote, string nombreIntegracion, int idUsuarioTecnico)
        {
            try
            {
                if (lote == null) return;
                bool conErrores = lote.Estado == CargueLoteEstado.ConErrores;
                if (!conErrores && !lote.PuedeConfirmar) return;

                var es = new System.Globalization.CultureInfo("es-CO");
                string Txt(string k) => bufinscustomers.App_GlobalResources.Strings.ResourceManager.GetString(k, es) ?? k;
                string modo = lote.Modo == 0 ? Txt("RevisarCargue_ModoEjecucion") : Txt("RevisarCargue_ModoHistorico");
                string titulo = Txt(conErrores ? "Notif_CargueApiConErrores" : "Notif_CargueApiPendiente");
                string mensaje = string.Format(Txt(conErrores ? "Notif_CargueApiConErroresMsg" : "Notif_CargueApiPendienteMsg"),
                    nombreIntegracion, lote.NombreEmpresa, lote.Anio, modo, lote.TotalFilas.ToString("N0"), lote.TotalErrores, lote.TotalAdvertencias);
                string url = "/Datos/RevisarCargue?idLote=" + lote.IdLote;

                var destinatarios = new List<int>();
                Bd.Leer(CadenaConexion, "SELECT Id FROM dbo.Usuarios WHERE IdEmpresa = @E AND Admin = 1 AND Id <> @T",
                    new { E = lote.IdEmpresa, T = idUsuarioTecnico },
                    r => destinatarios.Add(r.GetInt32(0)));
                var notif = new NotificacionesService();
                foreach (var id in destinatarios)
                    notif.Crear(id, titulo, mensaje, conErrores ? "warning" : "info", url);
            }
            catch (Exception ex) { AppLogger.Warn("CargueOrquestador.NotificarRevisores: " + ex.Message); }
        }
    }
}
