using System.ComponentModel.DataAnnotations;

namespace AtestadoMedico.ViewModels
{
    public class AtualizarStatusViewModel
    {
        [Required]
        public string UsuarioId { get; set; } = string.Empty;
        
        [Required]
        public string Status { get; set; }
        
        public string? MotivoRejeicao { get; set; }
    }
} 