using bufinscustomers.Models;
using bufinscustomers.Services;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Text;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    public class EmpresaController : Controller
    {
        private EmpresaService _empresaService = new EmpresaService();
        // GET: Empresa
        public ActionResult Empresas()
        {
            List<Empresas> empresas = _empresaService.ObtenerEmpresas();
            return View(empresas);
        }
    }
}
