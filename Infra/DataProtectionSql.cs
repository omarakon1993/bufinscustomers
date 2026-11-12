using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Xml.Linq;
using bufinscustomers.Helpers;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace bufinscustomers.Infra
{
    /// <summary>
    /// Llaves de ASP.NET Core Data Protection (antiforgery, TempData) guardadas en SQL Server (<c>dbo.DataProtectionKeys</c>,
    /// script manual <c>Sql/045</c>) para que un reinicio del sitio no invalide los formularios abiertos. En Conexcol la identidad
    /// del grupo de aplicaciones no puede escribir en <c>App_Data\dpkeys</c> y el sitio se recicla con frecuencia.
    /// Cada llave se guarda cifrada con la MachineKey (<see cref="MachineKeyXmlEncryptor"/>): quien tenga solo la base no puede usarlas.
    /// </summary>
    public sealed class DataProtectionSqlRepository : IXmlRepository
    {
        private readonly string _cadena;
        public DataProtectionSqlRepository(string cadena) { _cadena = cadena; }

        /// <summary>true si la tabla existe y la base responde. Se consulta una sola vez al arrancar.</summary>
        public static bool Disponible(string cadena)
        {
            try
            {
                using (var cn = new SqlConnection(cadena))
                using (var cmd = new SqlCommand("SELECT OBJECT_ID('dbo.DataProtectionKeys', 'U')", cn))
                {
                    cmd.CommandTimeout = 15;
                    cn.Open();
                    var r = cmd.ExecuteScalar();
                    return r != null && r != DBNull.Value;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DataProtection: no se pudo comprobar dbo.DataProtectionKeys (" + ex.GetType().Name + ").");
                return false;
            }
        }

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            var lista = new List<XElement>();
            try
            {
                using (var cn = new SqlConnection(_cadena))
                using (var cmd = new SqlCommand("SELECT Xml FROM dbo.DataProtectionKeys ORDER BY Id", cn))
                {
                    cn.Open();
                    using (var rd = cmd.ExecuteReader())
                        while (rd.Read()) lista.Add(XElement.Parse(rd.GetString(0)));
                }
            }
            catch (Exception ex)
            {
                // Se relanza: Data Protection conserva el juego de llaves que ya tenía en memoria y reintenta más tarde.
                AppLogger.Error(ex, "DataProtection.Leer");
                throw;
            }
            return lista;
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            try
            {
                using (var cn = new SqlConnection(_cadena))
                using (var cmd = new SqlCommand("INSERT INTO dbo.DataProtectionKeys (FriendlyName, Xml) VALUES (@Nombre, @Xml)", cn))
                {
                    cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = (object)friendlyName ?? DBNull.Value;
                    cmd.Parameters.Add("@Xml", SqlDbType.NVarChar, -1).Value = element.ToString(SaveOptions.DisableFormatting);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                AppLogger.Info("DataProtection: llave nueva guardada en dbo.DataProtectionKeys.");
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "DataProtection.Guardar");
                throw;
            }
        }
    }

    /// <summary>Cifra cada llave con <see cref="Compat.MachineKey"/> (AES + HMAC con las claves de secretsSettings.json).</summary>
    public sealed class MachineKeyXmlEncryptor : IXmlEncryptor
    {
        internal const string Proposito = "bufinscustomers.DataProtectionKeys.v1";

        public EncryptedXmlInfo Encrypt(XElement plaintextElement)
        {
            byte[] datos = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
            byte[] cifrado = Compat.MachineKey.Protect(datos, Proposito);
            var elemento = new XElement("encryptedKey",
                new XComment(" Cifrada con la MachineKey del sitio (Infra/DataProtectionSql.cs). "),
                new XElement("value", Convert.ToBase64String(cifrado)));
            return new EncryptedXmlInfo(elemento, typeof(MachineKeyXmlDecryptor));
        }
    }

    /// <summary>Descifra lo que produjo <see cref="MachineKeyXmlEncryptor"/>. Data Protection lo instancia por su nombre de tipo.</summary>
    public sealed class MachineKeyXmlDecryptor : IXmlDecryptor
    {
        public MachineKeyXmlDecryptor() { }
        public MachineKeyXmlDecryptor(IServiceProvider services) { }

        public XElement Decrypt(XElement encryptedElement)
        {
            var valor = encryptedElement.Element("value")?.Value;
            byte[] datos = string.IsNullOrEmpty(valor) ? null
                : Compat.MachineKey.Unprotect(Convert.FromBase64String(valor), MachineKeyXmlEncryptor.Proposito);
            if (datos == null)
                throw new InvalidOperationException("DataProtection: no se pudo descifrar una llave (¿cambió la MachineKey?).");
            return XElement.Parse(Encoding.UTF8.GetString(datos));
        }
    }
}
