using System;
using System.Data.SqlClient;
using System.Web.Mvc;
using bufinscustomers.Controllers;

namespace bufinscustomers.Controllers
{
    public class HealthController : BaseController
    {
        [HttpGet]
        public JsonResult Index()
        {
            string dbStatus = "ok";

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();
                    var cmd = new SqlCommand("SELECT 1", cn);
                    cmd.ExecuteScalar();
                }
            }
            catch (Exception)
            {
                dbStatus = "error";
                // A4: No exponer mensaje de excepción SQL al cliente
            }

            bool healthy = dbStatus == "ok";

            Response.StatusCode = healthy ? 200 : 503;

            return Json(new
            {
                status    = healthy ? "ok" : "degraded",
                db        = dbStatus,
                timestamp = DateTime.UtcNow.ToString("o")
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
