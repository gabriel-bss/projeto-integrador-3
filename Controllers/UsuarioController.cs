using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AtestadoMedico.Data;
using AtestadoMedico.Models;

namespace AtestadoMedico.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsuarioController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public UsuarioController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/Usuario
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetUsuarios()
        {
            var usuarios = await _context.Usuarios
                .Select(u => new { u.Id, u.Email, u.IsAdmin, u.DataCadastro })
                .ToListAsync();
            return Ok(usuarios);
        }

        private static string NormalizeId(string id) =>
            string.IsNullOrWhiteSpace(id) ? id : char.ToUpper(id[0]) + id.Substring(1);

        // GET: api/Usuario/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetUsuario(string id)
        {
            id = NormalizeId(id);
            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null) return NotFound("Usuário não encontrado");
            return Ok(new { usuario.Id, usuario.Email, usuario.IsAdmin, usuario.DataCadastro });
        }

        // POST: api/Usuario/login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel login)
        {
            Console.WriteLine($"Tentativa de login: Email={login.Email}");

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Email == login.Email && u.Senha == login.Senha);

            if (usuario == null)
                return Unauthorized("Credenciais inválidas");

            return Ok(new { id = usuario.Id, email = usuario.Email, isAdmin = usuario.IsAdmin });
        }

        // POST: api/Usuario/cadastrar?adminId=Brasil001
        [HttpPost("cadastrar")]
        public async Task<IActionResult> CadastrarUsuario([FromBody] CadastrarUsuarioModel model, [FromQuery] string adminId)
        {
            adminId = NormalizeId(adminId);
            var admin = await _context.Usuarios.FindAsync(adminId);
            if (admin == null || !admin.IsAdmin)
                return StatusCode(403, "Somente administradores podem cadastrar novos usuários");

            if (string.IsNullOrWhiteSpace(model.Matricula) || !model.Matricula.All(char.IsDigit))
                return BadRequest("A matrícula deve conter apenas números.");

            var novoId = $"Brasil{model.Matricula.Trim()}";

            if (await _context.Usuarios.AnyAsync(u => u.Id == novoId))
                return BadRequest($"Já existe um usuário com a matrícula {model.Matricula} (ID: {novoId}).");

            if (await _context.Usuarios.AnyAsync(u => u.Email == model.Email))
                return BadRequest("Este e-mail já está em uso.");

            var usuario = new Usuario
            {
                Id = novoId,
                Email = model.Email,
                Senha = model.Senha,
                IsAdmin = model.IsAdmin,
                DataCadastro = DateTime.UtcNow
            };

            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Usuário cadastrado com sucesso! ID: {novoId}", id = novoId });
        }

        // PUT: api/Usuario/AlterarSenha
        [HttpPut("AlterarSenha")]
        public async Task<IActionResult> AlterarSenha([FromBody] AlterarSenhaModel model)
        {
            model.AdminId = NormalizeId(model.AdminId);
            model.UsuarioId = NormalizeId(model.UsuarioId);
            var admin = await _context.Usuarios.FindAsync(model.AdminId);
            if (admin == null || !admin.IsAdmin)
                return StatusCode(403, "Somente administradores podem alterar senhas");

            var usuario = await _context.Usuarios.FindAsync(model.UsuarioId);
            if (usuario == null) return NotFound("Usuário não encontrado");

            usuario.Senha = model.NovaSenha;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Senha alterada com sucesso!" });
        }

        // DELETE: api/Usuario/ExcluirUsuario
        [HttpDelete("ExcluirUsuario")]
        public async Task<IActionResult> ExcluirUsuario([FromBody] ExcluirUsuarioModel model)
        {
            model.AdminId = NormalizeId(model.AdminId);
            model.UsuarioId = NormalizeId(model.UsuarioId);
            var admin = await _context.Usuarios.FindAsync(model.AdminId);
            if (admin == null || !admin.IsAdmin)
                return StatusCode(403, "Somente administradores podem excluir usuários");

            var usuario = await _context.Usuarios.FindAsync(model.UsuarioId);
            if (usuario == null) return NotFound("Usuário não encontrado");

            if (usuario.Id == model.AdminId)
                return BadRequest("Não é possível excluir seu próprio usuário");

            if (usuario.Email == "admin@admin.com")
                return BadRequest("Não é possível excluir o administrador principal do sistema");

            var possuiAtestados = await _context.Atestados.AnyAsync(a => a.UsuarioId == model.UsuarioId);
            if (possuiAtestados && !model.ForcarExclusao)
            {
                return BadRequest(new
                {
                    message = "Este usuário possui atestados registrados. Deseja excluir mesmo assim?",
                    requiresForce = true
                });
            }

            try
            {
                if (model.ForcarExclusao)
                {
                    var atestados = await _context.Atestados
                        .Where(a => a.UsuarioId == model.UsuarioId)
                        .ToListAsync();
                    _context.Atestados.RemoveRange(atestados);
                }

                _context.Usuarios.Remove(usuario);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Usuário excluído com sucesso!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erro ao excluir usuário: {ex.Message}");
            }
        }

        // =====================================================================
        // IMPORTAÇÃO EM MASSA VIA EXCEL
        // =====================================================================

        [HttpGet("ModeloPlanilha")]
        public IActionResult ModeloPlanilha()
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Usuarios");

            ws.Cell(1, 1).Value = "Email";
            ws.Cell(1, 2).Value = "Senha";
            ws.Cell(1, 3).Value = "Matricula";
            ws.Cell(1, 4).Value = "IsAdmin";

            var header = ws.Range("A1:D1");
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#2c7da0");
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = "joao.silva@empresa.com";
            ws.Cell(2, 2).Value = "Senha@123";
            ws.Cell(2, 3).Value = "475";
            ws.Cell(2, 4).Value = "false";

            ws.Cell(3, 1).Value = "maria.admin@empresa.com";
            ws.Cell(3, 2).Value = "Admin@456";
            ws.Cell(3, 3).Value = "100";
            ws.Cell(3, 4).Value = "true";

            ws.Cell(5, 1).Value = "INSTRUÇÕES:";
            ws.Cell(5, 1).Style.Font.Bold = true;
            ws.Cell(6, 1).Value = "- Não altere os cabeçalhos da linha 1";
            ws.Cell(7, 1).Value = "- Matricula: somente números (ex: 475). O ID ficará Brasil475";
            ws.Cell(8, 1).Value = "- IsAdmin: use 'true' para administrador ou 'false' para funcionário";
            ws.Cell(9, 1).Value = "- Preencha a partir da linha 2 (remova as linhas de exemplo se preferir)";
            ws.Range("A5:D9").Style.Font.Italic = true;

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "modelo_usuarios.xlsx"
            );
        }

        [HttpPost("ImportarPlanilha")]
        public async Task<IActionResult> ImportarPlanilha(IFormFile arquivo, [FromQuery] string adminId)
        {
            adminId = NormalizeId(adminId);
            var admin = await _context.Usuarios.FindAsync(adminId);
            if (admin == null || !admin.IsAdmin)
                return StatusCode(403, "Somente administradores podem importar usuários");

            if (arquivo == null || arquivo.Length == 0)
                return BadRequest("Nenhum arquivo enviado.");

            if (Path.GetExtension(arquivo.FileName).ToLower() != ".xlsx")
                return BadRequest("Apenas arquivos .xlsx são aceitos. Utilize a planilha modelo para evitar erros.");

            var importados = new List<string>();
            var erros = new List<string>();

            try
            {
                using var stream = arquivo.OpenReadStream();
                using var workbook = new XLWorkbook(stream);
                var ws = workbook.Worksheets.First();

                var cabecalhos = new[] { "Email", "Senha", "Matricula", "IsAdmin" };
                for (int col = 1; col <= 4; col++)
                {
                    var valor = ws.Cell(1, col).GetString().Trim();
                    if (!string.Equals(valor, cabecalhos[col - 1], StringComparison.OrdinalIgnoreCase))
                        return BadRequest($"Formato inválido. Coluna {col} deve ser '{cabecalhos[col - 1]}'. Utilize a planilha modelo.");
                }

                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

                for (int row = 2; row <= lastRow; row++)
                {
                    var email = ws.Cell(row, 1).GetString().Trim();
                    var senha = ws.Cell(row, 2).GetString().Trim();
                    var matricula = ws.Cell(row, 3).GetString().Trim();
                    var isAdminStr = ws.Cell(row, 4).GetString().Trim().ToLower();

                    if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(matricula)) continue;

                    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(senha) || string.IsNullOrEmpty(matricula))
                    {
                        erros.Add($"Linha {row}: campos obrigatórios ausentes (Email, Senha, Matricula).");
                        continue;
                    }

                    if (!matricula.All(char.IsDigit))
                    {
                        erros.Add($"Linha {row}: matrícula '{matricula}' inválida — use somente números.");
                        continue;
                    }

                    var novoId = $"Brasil{matricula}";

                    if (await _context.Usuarios.AnyAsync(u => u.Id == novoId))
                    {
                        erros.Add($"Linha {row}: matrícula {matricula} (ID {novoId}) já cadastrada.");
                        continue;
                    }

                    if (await _context.Usuarios.AnyAsync(u => u.Email == email))
                    {
                        erros.Add($"Linha {row}: e-mail '{email}' já está em uso.");
                        continue;
                    }

                    bool isAdmin = isAdminStr == "true" || isAdminStr == "sim" || isAdminStr == "1" || isAdminStr == "verdadeiro";

                    _context.Usuarios.Add(new Usuario
                    {
                        Id = novoId,
                        Email = email,
                        Senha = senha,
                        IsAdmin = isAdmin,
                        DataCadastro = DateTime.UtcNow
                    });

                    importados.Add($"{novoId} ({email})");
                }

                if (importados.Count > 0)
                    await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"Importação concluída: {importados.Count} usuário(s) cadastrado(s), {erros.Count} erro(s).",
                    importados,
                    erros
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erro ao processar planilha: {ex.Message}. Certifique-se de usar a planilha modelo.");
            }
        }
    }

    public class LoginModel
    {
        public string Email { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
    }

    public class CadastrarUsuarioModel
    {
        public string Email { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
        public string Matricula { get; set; } = string.Empty;
        public bool IsAdmin { get; set; } = false;
    }

    public class AlterarSenhaModel
    {
        public string AdminId { get; set; } = string.Empty;
        public string UsuarioId { get; set; } = string.Empty;
        public string NovaSenha { get; set; } = string.Empty;
    }

    public class ExcluirUsuarioModel
    {
        public string AdminId { get; set; } = string.Empty;
        public string UsuarioId { get; set; } = string.Empty;
        public bool ForcarExclusao { get; set; } = false;
    }
}
