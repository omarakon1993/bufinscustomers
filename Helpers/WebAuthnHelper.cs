using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Verificación WebAuthn mínima y sin dependencias (solo System.Security.Cryptography + Newtonsoft):
    /// registro de passkeys (attestation "none", no se valida la marca del autenticador) e inicio de
    /// sesión con aserción firmada. Algoritmos: ES256 (-7) y RS256 (-257), que cubren Windows Hello,
    /// Touch ID / Face ID, Android y llaves USB. Exige presencia (UP) y verificación del usuario (UV:
    /// huella, rostro o PIN) para que sea un segundo factor real.
    /// </summary>
    public static class WebAuthnHelper
    {
        public const int AlgES256 = -7;
        public const int AlgRS256 = -257;

        public class ResultadoRegistro
        {
            public bool Ok; public string Error;
            public byte[] CredentialId; public byte[] ClavePublicaCose; public int Algoritmo; public long Contador;
        }

        public class ResultadoAutenticacion
        {
            public bool Ok; public string Error; public long Contador;
        }

        // ───────── Base64url ─────────
        public static string ABase64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static byte[] DeBase64Url(string s)
        {
            if (string.IsNullOrEmpty(s)) return new byte[0];
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        public static string NuevoReto()
        {
            var b = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return ABase64Url(b);
        }

        // ───────── Registro ─────────
        public static ResultadoRegistro VerificarRegistro(byte[] clientDataJson, byte[] attestationObject,
            string retoEsperado, string origenEsperado, string rpId)
        {
            try
            {
                string err = ValidarClientData(clientDataJson, "webauthn.create", retoEsperado, origenEsperado);
                if (err != null) return new ResultadoRegistro { Error = err };

                var att = new CborLector(attestationObject).Leer() as Dictionary<object, object>;
                if (att == null || !att.ContainsKey("authData")) return new ResultadoRegistro { Error = "attestationObject inválido." };
                var authData = att["authData"] as byte[];
                if (authData == null) return new ResultadoRegistro { Error = "authData inválido." };

                err = ValidarAuthData(authData, rpId);
                if (err != null) return new ResultadoRegistro { Error = err };
                if ((authData[32] & 0x40) == 0) return new ResultadoRegistro { Error = "Falta la credencial en authData." };
                if (authData.Length < 55) return new ResultadoRegistro { Error = "authData truncado." };

                int credLen = (authData[53] << 8) | authData[54];
                if (credLen < 1 || credLen > 512 || authData.Length < 55 + credLen)
                    return new ResultadoRegistro { Error = "Identificador de credencial inválido." };
                var credId = new byte[credLen];
                Buffer.BlockCopy(authData, 55, credId, 0, credLen);

                int inicioCose = 55 + credLen;
                var lector = new CborLector(authData, inicioCose);
                var cose = lector.Leer() as Dictionary<object, object>;
                if (cose == null) return new ResultadoRegistro { Error = "Clave pública inválida." };
                var coseBytes = new byte[lector.Posicion - inicioCose];
                Buffer.BlockCopy(authData, inicioCose, coseBytes, 0, coseBytes.Length);

                int alg = ValidarCose(cose);
                if (alg == 0) return new ResultadoRegistro { Error = "Algoritmo de passkey no soportado." };

                return new ResultadoRegistro
                {
                    Ok = true, CredentialId = credId, ClavePublicaCose = coseBytes,
                    Algoritmo = alg, Contador = Contador(authData)
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn("WebAuthn registro rechazado: " + ex.Message, "WebAuthn");
                return new ResultadoRegistro { Error = "Respuesta de registro inválida." };
            }
        }

        // ───────── Inicio de sesión ─────────
        public static ResultadoAutenticacion VerificarAutenticacion(byte[] clientDataJson, byte[] authData, byte[] firma,
            string retoEsperado, string origenEsperado, string rpId, byte[] clavePublicaCose, int algoritmo)
        {
            try
            {
                string err = ValidarClientData(clientDataJson, "webauthn.get", retoEsperado, origenEsperado);
                if (err != null) return new ResultadoAutenticacion { Error = err };
                err = ValidarAuthData(authData, rpId);
                if (err != null) return new ResultadoAutenticacion { Error = err };

                byte[] hashCd;
                using (var sha = SHA256.Create()) hashCd = sha.ComputeHash(clientDataJson);
                var firmado = new byte[authData.Length + hashCd.Length];
                Buffer.BlockCopy(authData, 0, firmado, 0, authData.Length);
                Buffer.BlockCopy(hashCd, 0, firmado, authData.Length, hashCd.Length);

                var cose = new CborLector(clavePublicaCose).Leer() as Dictionary<object, object>;
                if (cose == null) return new ResultadoAutenticacion { Error = "Clave guardada inválida." };

                bool ok;
                if (algoritmo == AlgES256)
                {
                    var p = new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        Q = new ECPoint { X = (byte[])cose[-2L], Y = (byte[])cose[-3L] }
                    };
                    using (var ec = ECDsa.Create(p))
                        ok = ec.VerifyData(firmado, DerARaw(firma, 32), HashAlgorithmName.SHA256);
                }
                else if (algoritmo == AlgRS256)
                {
                    using (var rsa = RSA.Create())
                    {
                        rsa.ImportParameters(new RSAParameters { Modulus = (byte[])cose[-1L], Exponent = (byte[])cose[-2L] });
                        ok = rsa.VerifyData(firmado, firma, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    }
                }
                else return new ResultadoAutenticacion { Error = "Algoritmo no soportado." };

                if (!ok) return new ResultadoAutenticacion { Error = "Firma inválida." };
                return new ResultadoAutenticacion { Ok = true, Contador = Contador(authData) };
            }
            catch (Exception ex)
            {
                AppLogger.Warn("WebAuthn aserción rechazada: " + ex.Message, "WebAuthn");
                return new ResultadoAutenticacion { Error = "Respuesta de autenticación inválida." };
            }
        }

        // ───────── Piezas comunes ─────────
        private static string ValidarClientData(byte[] clientDataJson, string tipoEsperado, string retoEsperado, string origenEsperado)
        {
            var cd = JObject.Parse(Encoding.UTF8.GetString(clientDataJson));
            if ((string)cd["type"] != tipoEsperado) return "Tipo de operación inesperado.";
            if (string.IsNullOrEmpty(retoEsperado) || (string)cd["challenge"] != retoEsperado) return "Reto inválido o vencido.";
            if (!string.Equals((string)cd["origin"], origenEsperado, StringComparison.Ordinal)) return "Origen no permitido.";
            if (cd["crossOrigin"] != null && (bool)cd["crossOrigin"]) return "Operación entre orígenes no permitida.";
            return null;
        }

        /// <summary>Valida rpIdHash y que el usuario estuvo presente (UP) y fue verificado (UV).</summary>
        private static string ValidarAuthData(byte[] authData, string rpId)
        {
            if (authData == null || authData.Length < 37) return "authData inválido.";
            byte[] esperado;
            using (var sha = SHA256.Create()) esperado = sha.ComputeHash(Encoding.UTF8.GetBytes(rpId));
            for (int i = 0; i < 32; i++)
                if (authData[i] != esperado[i]) return "Dominio (RP ID) no coincide.";
            byte flags = authData[32];
            if ((flags & 0x01) == 0) return "Falta la presencia del usuario.";
            if ((flags & 0x04) == 0) return "Falta la verificación del usuario (huella, rostro o PIN).";
            return null;
        }

        private static long Contador(byte[] authData)
        {
            return ((long)authData[33] << 24) | ((long)authData[34] << 16) | ((long)authData[35] << 8) | authData[36];
        }

        /// <summary>Devuelve el algoritmo COSE (-7 / -257) si la clave es válida y soportada; 0 si no.</summary>
        private static int ValidarCose(Dictionary<object, object> cose)
        {
            try
            {
                long kty = (long)cose[1L];
                long alg = (long)cose[3L];
                if (kty == 2 && alg == AlgES256 && (long)cose[-1L] == 1
                    && ((byte[])cose[-2L]).Length == 32 && ((byte[])cose[-3L]).Length == 32) return AlgES256;
                if (kty == 3 && alg == AlgRS256 && ((byte[])cose[-1L]).Length >= 256 && ((byte[])cose[-2L]).Length >= 1) return AlgRS256;
            }
            catch { }
            return 0;
        }

        /// <summary>Convierte una firma ECDSA en DER (SEQUENCE{INTEGER r, INTEGER s}) al formato r||s que espera .NET.</summary>
        private static byte[] DerARaw(byte[] der, int tamano)
        {
            int p = 0;
            if (der[p++] != 0x30) throw new FormatException("DER inválido.");
            LeerLargoDer(der, ref p);
            var r = LeerEnteroDer(der, ref p);
            var s = LeerEnteroDer(der, ref p);
            var raw = new byte[tamano * 2];
            CopiarDerecha(r, raw, 0, tamano);
            CopiarDerecha(s, raw, tamano, tamano);
            return raw;
        }

        private static int LeerLargoDer(byte[] d, ref int p)
        {
            int l = d[p++];
            if (l < 0x80) return l;
            int n = l & 0x7F; l = 0;
            for (int i = 0; i < n; i++) l = (l << 8) | d[p++];
            return l;
        }

        private static byte[] LeerEnteroDer(byte[] d, ref int p)
        {
            if (d[p++] != 0x02) throw new FormatException("DER inválido.");
            int l = LeerLargoDer(d, ref p);
            var v = new byte[l];
            Buffer.BlockCopy(d, p, v, 0, l);
            p += l;
            return v;
        }

        private static void CopiarDerecha(byte[] origen, byte[] destino, int desde, int tamano)
        {
            int i = 0;
            while (i < origen.Length - 1 && origen[i] == 0) i++;   // quita ceros iniciales
            int largo = origen.Length - i;
            if (largo > tamano) throw new FormatException("Entero DER demasiado largo.");
            Buffer.BlockCopy(origen, i, destino, desde + tamano - largo, largo);
        }
    }
}
