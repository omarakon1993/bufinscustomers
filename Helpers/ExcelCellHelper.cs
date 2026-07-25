using System;
using ClosedXML.Excel;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Escribe un valor de tipo desconocido (proveniente de DataTable, SqlDataReader, etc.)
    /// en una celda de ClosedXML: numéricos como número real, el resto como texto.
    /// </summary>
    internal static class ExcelCellHelper
    {
        public static void SetValue(IXLCell cell, object value)
        {
            switch (value)
            {
                case null:
                case DBNull _:
                    cell.Clear(XLClearOptions.Contents);
                    break;
                case string s:
                    cell.Value = s;
                    break;
                case bool b:
                    cell.Value = b;
                    break;
                case DateTime dt:
                    cell.Value = dt;
                    break;
                case double d:
                    cell.Value = d;
                    break;
                case float f:
                    cell.Value = (double)f;
                    break;
                case decimal m:
                    cell.Value = (double)m;
                    break;
                case int i:
                    cell.Value = (double)i;
                    break;
                case long l:
                    cell.Value = (double)l;
                    break;
                case short sh:
                    cell.Value = (double)sh;
                    break;
                case byte by:
                    cell.Value = (double)by;
                    break;
                default:
                    cell.Value = value.ToString();
                    break;
            }
        }
    }
}
