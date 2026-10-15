using System;
using System.Collections.Generic;
using System.Text;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Lector CBOR mínimo (RFC 8949) para WebAuthn: enteros, cadenas de bytes/texto, arreglos y mapas.
    /// Los mapas se devuelven como Dictionary&lt;object, object&gt; (claves long o string); los
    /// enteros como long; los bytes como byte[]. Sin dependencias.
    /// </summary>
    public class CborLector
    {
        private readonly byte[] _d;
        private int _p;

        public CborLector(byte[] datos, int inicio = 0) { _d = datos; _p = inicio; }

        /// <summary>Posición actual (bytes consumidos desde el inicio del arreglo).</summary>
        public int Posicion { get { return _p; } }

        public object Leer() { return Leer(0); }

        private object Leer(int profundidad)
        {
            if (profundidad > 8) throw new FormatException("CBOR demasiado anidado.");
            if (_p >= _d.Length) throw new FormatException("CBOR truncado.");
            byte ib = _d[_p++];
            int mayor = ib >> 5;
            int info = ib & 0x1F;
            ulong arg = LeerArgumento(info);

            switch (mayor)
            {
                case 0: return checked((long)arg);                       // entero sin signo
                case 1: return -1L - checked((long)arg);                 // entero negativo
                case 2: return Tomar(arg);                               // bytes
                case 3: return Encoding.UTF8.GetString(Tomar(arg));      // texto
                case 4:                                                  // arreglo
                {
                    var lista = new List<object>();
                    for (ulong i = 0; i < arg; i++) lista.Add(Leer(profundidad + 1));
                    return lista;
                }
                case 5:                                                  // mapa
                {
                    var mapa = new Dictionary<object, object>();
                    for (ulong i = 0; i < arg; i++)
                    {
                        object k = Leer(profundidad + 1);
                        mapa[k] = Leer(profundidad + 1);
                    }
                    return mapa;
                }
                case 7:                                                  // simples: false/true/null
                    if (info == 20) return false;
                    if (info == 21) return true;
                    if (info == 22 || info == 23) return null;
                    throw new FormatException("Valor CBOR simple no soportado.");
                default:
                    throw new FormatException("Tipo CBOR no soportado.");
            }
        }

        private ulong LeerArgumento(int info)
        {
            if (info < 24) return (ulong)info;
            int n;
            switch (info)
            {
                case 24: n = 1; break;
                case 25: n = 2; break;
                case 26: n = 4; break;
                case 27: n = 8; break;
                default: throw new FormatException("Longitud CBOR indefinida no soportada.");
            }
            if (_p + n > _d.Length) throw new FormatException("CBOR truncado.");
            ulong v = 0;
            for (int i = 0; i < n; i++) v = (v << 8) | _d[_p++];
            return v;
        }

        private byte[] Tomar(ulong largo)
        {
            if (largo > (ulong)(_d.Length - _p)) throw new FormatException("CBOR truncado.");
            var r = new byte[(int)largo];
            Buffer.BlockCopy(_d, _p, r, 0, r.Length);
            _p += r.Length;
            return r;
        }
    }
}
