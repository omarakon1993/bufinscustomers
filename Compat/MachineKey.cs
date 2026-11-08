using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace bufinscustomers.Compat
{
    /// <summary>
    /// System.Web.Security.MachineKey.Protect / Unprotect de .NET Framework 4.5+, reimplementado para leer y escribir EXACTAMENTE el mismo
    /// formato (compatible en ambos sentidos con la aplicacion antigua mientras exista la posibilidad de volver atras).
    ///
    /// Formato:  IV || AES-CBC-PKCS7(datos) || HMAC(IV || cifrado)
    /// Claves:   se derivan de las claves maestras de la machineKey (validationKey y decryptionKey) con SP800-108 en modo contador
    ///           (HMAC-SHA512), con label = "User.MachineKey.Protect" y context = los "propositos" serializados con BinaryWriter.
    /// Las claves maestras salen de IConfiguration (secretsSettings.json o variables de entorno): MachineKey:ValidationKey,
    /// MachineKey:DecryptionKey, MachineKey:Validation (SHA1, HMACSHA256...) y MachineKey:Decryption (AES).
    /// </summary>
    public static class MachineKey
    {
        public const string PropositoPrimario = "User.MachineKey.Protect";
        private static IConfiguration _config;
        public static void Iniciar(IConfiguration config) { _config = config; }

        public static byte[] Protect(byte[] datos, params string[] propositos)
        {
            var k = Claves(propositos);
            using (var aes = Aes.Create())
            {
                aes.Key = k.Cifrado; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();
                byte[] cifrado = aes.EncryptCbc(datos, aes.IV, PaddingMode.PKCS7);
                byte[] cuerpo = aes.IV.Concat(cifrado).ToArray();
                using (var hmac = k.CrearHmac()) return cuerpo.Concat(hmac.ComputeHash(cuerpo)).ToArray();
            }
        }

        /// <summary>Devuelve los datos originales, o null si la firma no coincide o el formato no es valido (como MachineKey.Unprotect).</summary>
        public static byte[] Unprotect(byte[] protegido, params string[] propositos)
        {
            if (protegido == null) return null;
            var k = Claves(propositos);
            using (var hmac = k.CrearHmac())
            {
                int tamFirma = hmac.HashSize / 8;
                const int tamIv = 16;
                if (protegido.Length < tamIv + 16 + tamFirma) return null;
                int largoCuerpo = protegido.Length - tamFirma;
                var firmaReal = hmac.ComputeHash(protegido, 0, largoCuerpo);
                var firma = protegido.Skip(largoCuerpo).ToArray();
                if (!CryptographicOperations.FixedTimeEquals(firmaReal, firma)) return null;
                try
                {
                    using (var aes = Aes.Create())
                    {
                        aes.Key = k.Cifrado;
                        var iv = protegido.Take(tamIv).ToArray();
                        var cifrado = protegido.Skip(tamIv).Take(largoCuerpo - tamIv).ToArray();
                        return aes.DecryptCbc(cifrado, iv, PaddingMode.PKCS7);
                    }
                }
                catch (CryptographicException) { return null; }
            }
        }

        // ------------------------------------------------------------------ derivacion de claves

        private sealed class ClavesDerivadas
        {
            public byte[] Cifrado;
            public byte[] Validacion;
            public string Algoritmo;
            public HMAC CrearHmac()
            {
                switch ((Algoritmo ?? "SHA1").ToUpperInvariant())
                {
                    case "HMACSHA256": return new HMACSHA256(Validacion);
                    case "HMACSHA384": return new HMACSHA384(Validacion);
                    case "HMACSHA512": return new HMACSHA512(Validacion);
                    case "MD5": return new HMACMD5(Validacion);
                    default: return new HMACSHA1(Validacion);   // "SHA1" (valor de la machineKey de este sitio)
                }
            }
        }

        private static ClavesDerivadas Claves(string[] propositos)
        {
            if (_config == null) throw new InvalidOperationException("MachineKey no esta configurada (MachineKey.Iniciar).");
            string vk = _config["MachineKey:ValidationKey"], dk = _config["MachineKey:DecryptionKey"];
            if (string.IsNullOrWhiteSpace(vk) || string.IsNullOrWhiteSpace(dk))
                throw new InvalidOperationException("Falta MachineKey:ValidationKey / MachineKey:DecryptionKey (secretsSettings.json o variables de entorno).");

            byte[] maestraValidacion = Hex(vk), maestraCifrado = Hex(dk);
            byte[] etiqueta = Encoding.UTF8.GetBytes(PropositoPrimario);
            byte[] contexto;
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, new UTF8Encoding(false, true), true))
                    foreach (var p in propositos ?? new string[0]) w.Write(p);   // cadena con prefijo de longitud, como BinaryWriter.Write(string)
                contexto = ms.ToArray();
            }
            return new ClavesDerivadas
            {
                Cifrado = Derivar(maestraCifrado, etiqueta, contexto, maestraCifrado.Length * 8),
                Validacion = Derivar(maestraValidacion, etiqueta, contexto, maestraValidacion.Length * 8),
                Algoritmo = _config["MachineKey:Validation"]
            };
        }

        /// <summary>NIST SP800-108, modo contador, PRF = HMAC-SHA512: K(i) = HMAC(KI, [i]32 || etiqueta || 0x00 || contexto || [L]32).</summary>
        private static byte[] Derivar(byte[] claveBase, byte[] etiqueta, byte[] contexto, int bitsClave)
        {
            var buffer = new byte[4 + etiqueta.Length + 1 + contexto.Length + 4];
            Buffer.BlockCopy(etiqueta, 0, buffer, 4, etiqueta.Length);
            Buffer.BlockCopy(contexto, 0, buffer, 5 + etiqueta.Length, contexto.Length);
            EscribirBigEndian((uint)bitsClave, buffer, 5 + etiqueta.Length + contexto.Length);

            var salida = new byte[bitsClave / 8];
            int escritos = 0;
            using (var hmac = new HMACSHA512(claveBase))
            {
                for (uint i = 1; escritos < salida.Length; i++)
                {
                    EscribirBigEndian(i, buffer, 0);
                    var ki = hmac.ComputeHash(buffer);
                    int n = Math.Min(salida.Length - escritos, ki.Length);
                    Buffer.BlockCopy(ki, 0, salida, escritos, n);
                    escritos += n;
                }
            }
            return salida;
        }

        private static void EscribirBigEndian(uint valor, byte[] destino, int posicion)
        {
            destino[posicion] = (byte)(valor >> 24); destino[posicion + 1] = (byte)(valor >> 16);
            destino[posicion + 2] = (byte)(valor >> 8); destino[posicion + 3] = (byte)valor;
        }

        private static byte[] Hex(string s)
        {
            s = s.Trim();
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
