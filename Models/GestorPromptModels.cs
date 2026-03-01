namespace bufinscustomers.Models
{
    public class PromptIA
    {
        public int Id { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public string TextoPrompt { get; set; }
        public bool Activo { get; set; }
    }
}
