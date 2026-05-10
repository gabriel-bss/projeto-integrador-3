using System.ComponentModel.DataAnnotations;

namespace AtestadoMedico.Models
{
    public class Usuario
    {
        [Key]
        [StringLength(20)]
        public string Id { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Senha { get; set; } = string.Empty;

        [Required]
        public bool IsAdmin { get; set; } = false;

        public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

        public ICollection<Atestado> Atestados { get; set; } = new List<Atestado>();
    }
}