using AtestadoMedico.Data;
using AtestadoMedico.Models;
using AtestadoMedico.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using AtestadoMedico.Data;
using AtestadoMedico.Models;
using AtestadoMedico.ViewModels;
using Microsoft.Extensions.Configuration; // Necessário para ler as config do Azure

namespace AtestadoMedico.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AtestadoController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApplicationDbContext _context;

        private readonly IConfiguration _configuration; // Para ler a string de conexão do Blob
        
        // Construtor corrigido: Injeta IConfiguration e remove IWebHostEnvironment (não é mais necessário)
        public AtestadoController(ApplicationDbContext context, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            
            // O código 'InitializeStatusColumns' foi removido
            // pois era específico do SQLite (PRAGMA) e suas migrações do PostgreSQL
            // já criaram as colunas de Status.
        }

        // =================================================================
        // MÉTODO DE UPLOAD CORRIGIDO (USANDO AZURE BLOB STORAGE)
        // =================================================================
        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromForm] AtestadoViewModel viewModel)
        {
            if (viewModel.Arquivo == null || viewModel.Arquivo.Length == 0)
            {
                return BadRequest("Nenhuma imagem (arquivo) foi enviada.");
            }

            // Verificar se o usuário existe
            var usuario = await _context.Usuarios.FindAsync(viewModel.UsuarioId);
            if (usuario == null)
            {
                return BadRequest("Usuário não encontrado");
            }

            // --- 1. LÓGICA DE UPLOAD PARA O AZURE BLOB ---

            // Pega a connection string do appsettings.json
            string connectionString = _configuration.GetConnectionString("BlobStorage");
            if (string.IsNullOrEmpty(connectionString))
            {
                return StatusCode(500, "A string de conexão do Blob Storage não foi configurada.");
            }

            // Pega o nome do contêiner que você criou (ex: "atestados")
            string containerName = "atestados";
            
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            
            // Cria o contêiner se ele não existir (opcional, mas bom para garantir)
            await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);
            
            string uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(viewModel.Arquivo.FileName);
            BlobClient blobClient = containerClient.GetBlobClient(uniqueFileName);

            // Faz o upload da imagem
            using (var stream = viewModel.Arquivo.OpenReadStream())
            {
                await blobClient.UploadAsync(stream, true);
            }

            // Pega a URL pública do arquivo que acabamos de subir
            string fileUrl = blobClient.Uri.ToString();


            // --- 2. LÓGICA DE SALVAR NO BANCO DE DADOS (PostgreSQL) ---

            var atestado = new Atestado
            {
                UsuarioId = viewModel.UsuarioId,
                DataAtestado = viewModel.DataAtestado,
                NomeMedico = viewModel.NomeMedico,
                CRM = viewModel.CRM,
                Descricao = viewModel.Descricao,
                CID = viewModel.CID,
                DiasAfastamento = viewModel.DiasAfastamento,

                // Informações do arquivo que veio do bucket
                CaminhoArquivo = fileUrl, // A URL do Azure Blob
                NomeArquivo = viewModel.Arquivo.FileName, // Nome original
                TipoArquivo = viewModel.Arquivo.ContentType, // Ex: "image/jpeg"

                DataCadastro = DateTime.UtcNow,
                Status = "Pendente" // Status inicial para novos atestados
            };

            _context.Atestados.Add(atestado);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Atestado enviado com sucesso!" });
        }
        
        // =================================================================
        // SEUS MÉTODOS EXISTENTES (GET, PUT, DELETE, ETC.)
        // =================================================================

        // GET: api/Atestado
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AtestadoViewModel>>> GetAtestados([FromQuery] int? usuarioId = null)
        {
            Console.WriteLine($"Recebida solicitação para obter atestados. UsuarioId: {usuarioId}");
            
            if (usuarioId.HasValue)
            {
                var usuario = await _context.Usuarios.FindAsync(usuarioId.Value);
                if (usuario == null)
                {
                    Console.WriteLine($"Usuário {usuarioId} não encontrado");
                    return NotFound("Usuário não encontrado");
                }
                
                Console.WriteLine($"Usuário {usuario.Nome} (ID: {usuario.Id}) encontrado. É admin: {usuario.IsAdmin}");
                
                var atestadosQuery = usuario.IsAdmin 
                    ? _context.Atestados 
                    : _context.Atestados.Where(a => a.UsuarioId == usuarioId.Value);
                
                var realCount = await atestadosQuery.CountAsync();
                Console.WriteLine($"Número real de atestados para {(usuario.IsAdmin ? "admin" : "usuário")}: {realCount}");
                
                var atestados = await atestadosQuery
                    .OrderByDescending(a => a.DataCadastro)
                    .ToListAsync();
                
                Console.WriteLine($"Retornando {atestados.Count} atestados");
                
                var atestadosViewModel = atestados.Select(a => new AtestadoViewModel
                {
                    Id = a.Id,
                    UsuarioId = a.UsuarioId,
                    DataAtestado = a.DataAtestado,
                    NomeMedico = a.NomeMedico,
                    CRM = a.CRM,
                    Descricao = a.Descricao,
                    NomeArquivo = a.NomeArquivo,
                    TipoArquivo = a.TipoArquivo,
                    CaminhoArquivo = a.CaminhoArquivo,
                    DataCadastro = a.DataCadastro,
                    Status = GetSafeStatus(a),
                    MotivoRejeicao = a.MotivoRejeicao,
                    CID = a.CID,
                    DiasAfastamento = a.DiasAfastamento
                }).ToList();
                
                return atestadosViewModel;
            }
            else
            {
                Console.WriteLine("UsuarioId não fornecido. Retornando lista vazia.");
                return new List<AtestadoViewModel>();
            }
        }
        
        // Método auxiliar para obter o Status com segurança
        private string GetSafeStatus(Atestado atestado)
        {
            try
            {
                return atestado.Status ?? "Pendente";
            }
            catch
            {
                return "Pendente";
            }
        }

        // GET: api/Atestado/5?usuarioId=1
        [HttpGet("{id}")]
        public async Task<ActionResult<AtestadoViewModel>> GetAtestado(int id, [FromQuery] int usuarioId)
        {
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return NotFound("Usuário não encontrado");
            }
            
            var atestado = await _context.Atestados.FindAsync(id);
            if (atestado == null)
            {
                return NotFound("Atestado não encontrado");
            }

            if (!usuario.IsAdmin && atestado.UsuarioId != usuarioId)
            {
                Console.WriteLine($"Acesso negado: Usuário {usuarioId} tentando acessar atestado {id} do usuário {atestado.UsuarioId}");
                return Unauthorized("Você só pode visualizar seus próprios atestados");
            }
            
            Console.WriteLine($"Atestado {id} acessado por: {usuario.Nome} (ID: {usuarioId}, Admin: {usuario.IsAdmin})");

            var atestadoVM = new AtestadoViewModel
            {
                Id = atestado.Id,
                UsuarioId = atestado.UsuarioId,
                DataAtestado = atestado.DataAtestado,
                NomeMedico = atestado.NomeMedico,
                CRM = atestado.CRM,
                Descricao = atestado.Descricao,
                NomeArquivo = atestado.NomeArquivo,
                TipoArquivo = atestado.TipoArquivo,
                CaminhoArquivo = atestado.CaminhoArquivo,
                DataCadastro = atestado.DataCadastro,
                Status = GetSafeStatus(atestado),
                MotivoRejeicao = atestado.MotivoRejeicao,
                CID = atestado.CID,
                DiasAfastamento = atestado.DiasAfastamento
            };

            return atestadoVM;
        }

        // GET: api/Atestado/Usuario/5
        [HttpGet("Usuario/{usuarioId}")]
        public async Task<ActionResult<IEnumerable<AtestadoViewModel>>> GetAtestadosByUsuario(int usuarioId, [FromQuery] int requestingUserId)
        {
            var requestingUser = await _context.Usuarios.FindAsync(requestingUserId);
            if (requestingUser == null)
            {
                return NotFound("Usuário solicitante não encontrado");
            }
            
            if (!requestingUser.IsAdmin && requestingUser.Id != usuarioId)
            {
                Console.WriteLine($"Acesso negado: Usuário {requestingUserId} tentando acessar atestados do usuário {usuarioId}");
                return Unauthorized("Você só pode visualizar seus próprios atestados");
            }
            
            var atestados = await _context.Atestados
                .Where(a => a.UsuarioId == usuarioId)
                .OrderByDescending(a => a.DataCadastro)
                .ToListAsync();
            
            Console.WriteLine($"Retornando {atestados.Count} atestados para o usuário {usuarioId}, solicitado por {requestingUserId}");

            var atestadosVM = atestados.Select(a => new AtestadoViewModel
            {
                Id = a.Id,
                UsuarioId = a.UsuarioId,
                DataAtestado = a.DataAtestado,
                NomeMedico = a.NomeMedico,
                CRM = a.CRM,
                Descricao = a.Descricao,
                NomeArquivo = a.NomeArquivo,
                TipoArquivo = a.TipoArquivo,
                CaminhoArquivo = a.CaminhoArquivo,
                DataCadastro = a.DataCadastro,
                Status = GetSafeStatus(a),
                MotivoRejeicao = a.MotivoRejeicao,
                CID = a.CID,
                DiasAfastamento = a.DiasAfastamento
            }).ToList();

            return atestadosVM;
        }

        // GET: api/Atestado/MeusAtestados?usuarioId=5
        [HttpGet("MeusAtestados")]
        public async Task<ActionResult<IEnumerable<AtestadoViewModel>>> GetMeusAtestados([FromQuery] int usuarioId)
        {
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return NotFound("Usuário não encontrado");
            }
            
            Console.WriteLine($"Buscando atestados para o usuário: {usuario.Id}, {usuario.Nome}");
            
            var countExato = await _context.Atestados
                .Where(a => a.UsuarioId == usuarioId)
                .CountAsync();
                
            Console.WriteLine($"Contagem real do banco para usuário {usuarioId}: {countExato}");
            
            var atestados = await _context.Atestados
                .Where(a => a.UsuarioId == usuarioId)
                .OrderByDescending(a => a.DataCadastro)
                .ToListAsync();

            if (atestados.Count == 0)
            {
                Console.WriteLine($"Nenhum atestado encontrado para o usuário {usuario.Id}");
                return Ok(new { message = "Nenhum atestado encontrado", atestados = new List<AtestadoViewModel>() });
            }
            
            var atestadosVM = atestados.Select(a => new AtestadoViewModel
            {
                Id = a.Id,
                UsuarioId = a.UsuarioId,
                DataAtestado = a.DataAtestado,
                NomeMedico = a.NomeMedico,
                CRM = a.CRM,
                Descricao = a.Descricao,
                NomeArquivo = a.NomeArquivo,
                TipoArquivo = a.TipoArquivo,
                CaminhoArquivo = a.CaminhoArquivo,
                DataCadastro = a.DataCadastro,
                Status = GetSafeStatus(a),
                MotivoRejeicao = a.MotivoRejeicao,
                CID = a.CID,
                DiasAfastamento = a.DiasAfastamento
            }).ToList();

            return atestadosVM;
        }

        // GET: api/Atestado/MeuAtestado/5?usuarioId=1
        [HttpGet("MeuAtestado/{id}")]
        public async Task<ActionResult<AtestadoViewModel>> GetMeuAtestado(int id, [FromQuery] int usuarioId)
        {
            var atestado = await _context.Atestados.FindAsync(id);
            if (atestado == null)
            {
                return NotFound("Atestado não encontrado");
            }
            
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return NotFound("Usuário não encontrado");
            }
            
            if (!usuario.IsAdmin && atestado.UsuarioId != usuarioId)
            {
                Console.WriteLine($"Tentativa de acesso não autorizado: usuário {usuarioId} tentando acessar atestado {id} do usuário {atestado.UsuarioId}");
                return Unauthorized("Você só pode visualizar seus próprios atestados");
            }
            
            var atestadoVM = new AtestadoViewModel
            {
                Id = atestado.Id,
                UsuarioId = atestado.UsuarioId,
                DataAtestado = atestado.DataAtestado,
                NomeMedico = atestado.NomeMedico,
                CRM = atestado.CRM,
                Descricao = atestado.Descricao,
                NomeArquivo = atestado.NomeArquivo,
                TipoArquivo = atestado.TipoArquivo,
                CaminhoArquivo = atestado.CaminhoArquivo,
                DataCadastro = atestado.DataCadastro,
                Status = GetSafeStatus(atestado),
                MotivoRejeicao = atestado.MotivoRejeicao,
                CID = atestado.CID,
                DiasAfastamento = atestado.DiasAfastamento
            };

            return atestadoVM;
        }

        // Método para download do arquivo
        // ATENÇÃO: Este método de download só funciona para a versão LOCAL (wwwroot/uploads).
        // Se você está usando o Azure Blob, o download é feito de outra forma
        // (ou, mais fácil, o frontend apenas usa a URL do CaminhoArquivo para abrir o arquivo).
        [HttpGet("download/{id}")]
        public async Task<IActionResult> Download(int id, [FromQuery] int usuarioId)
        {
            var atestado = await _context.Atestados.FindAsync(id);
            if (atestado == null)
                return NotFound("Atestado não encontrado");
            
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
                return NotFound("Usuário não encontrado");
                
            if (!usuario.IsAdmin && usuario.Id != atestado.UsuarioId)
            {
                return Unauthorized("Você só pode baixar seus próprios atestados");
            }

            // A LÓGICA DE DOWNLOAD MUDA COM O BLOB STORAGE
            // Se CaminhoArquivo é uma URL (ex: https://...), redirecione para ela
            if (atestado.CaminhoArquivo.StartsWith("http"))
            {
                return Redirect(atestado.CaminhoArquivo);
            }
            
            // Se for o método antigo (salvando local), mantenha o código antigo
            // var filePath = Path.Combine(_environment.WebRootPath, "uploads", atestado.CaminhoArquivo);
            // if (!System.IO.File.Exists(filePath))
            // {
            //     return NotFound("Arquivo do atestado não encontrado");
            // }
            // var memory = new MemoryStream();
            // using (var stream = new FileStream(filePath, FileMode.Open))
            // {
            //     await stream.CopyToAsync(memory);
            // }
            // memory.Position = 0;
            // return File(memory, atestado.TipoArquivo, atestado.NomeArquivo);

            return NotFound("O método de download não está configurado para o armazenamento em nuvem ou o arquivo local não foi encontrado.");
        }

        // DELETE: api/Atestado/5?usuarioId=1
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAtestado(int id, [FromQuery] int usuarioId)
        {
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null || !usuario.IsAdmin)
            {
                return Unauthorized("Somente administradores podem excluir atestados");
            }
            
            var atestado = await _context.Atestados.FindAsync(id);
            if (atestado == null)
            {
                return NotFound("Atestado não encontrado");
            }

            // TODO: Excluir o arquivo do Azure Blob Storage
            // (Esta parte ainda não foi implementada, mas o registro será excluído)

            _context.Atestados.Remove(atestado);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Atestado excluído com sucesso" });
        }

        // DELETE: api/Atestado/ExcluirMeuAtestado/5?usuarioId=1
        [HttpDelete("ExcluirMeuAtestado/{id}")]
        public async Task<IActionResult> ExcluirMeuAtestado(int id, [FromQuery] int usuarioId)
        {
            var atestado = await _context.Atestados.FindAsync(id);
            if (atestado == null)
            {
                return NotFound("Atestado não encontrado");
            }
            
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return NotFound("Usuário não encontrado");
            }
            
            if (atestado.UsuarioId != usuarioId)
            {
                return Unauthorized("Você só pode excluir seus próprios atestados");
            }
            
            // TODO: Excluir o arquivo do Azure Blob Storage

            _context.Atestados.Remove(atestado);
            await _context.SaveChangesAsync();
            
            return Ok(new { message = "Seu atestado foi excluído com sucesso" });
        }

        // GET: api/Atestado/ContarAtestados?usuarioId=1
        [HttpGet("ContarAtestados")]
        public async Task<ActionResult<object>> ContarAtestados([FromQuery] int usuarioId)
        {            
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return NotFound("Usuário não encontrado");
            }
            
            if (usuario.IsAdmin)
            {
                var todosAtestados = await _context.Atestados.ToListAsync();
                var totalAtestados = todosAtestados.Count;
                var totalUsuarios = await _context.Usuarios.CountAsync();
                var mediaAtestadosPorUsuario = totalUsuarios > 0 ? (float)totalAtestados / totalUsuarios : 0;
                
                var hoje = DateTime.UtcNow.Date;
                var seisAtras = hoje.AddMonths(-6);
                
                var atestadosPorMes = todosAtestados
                    .Where(a => a.DataCadastro >= seisAtras)
                    .GroupBy(a => new { a.DataCadastro.Year, a.DataCadastro.Month })
                    .Select(g => new 
                    {
                        Ano = g.Key.Year,
                        Mes = g.Key.Month,
                        Quantidade = g.Count()
                    })
                    .OrderBy(x => x.Ano)
                    .ThenBy(x => x.Mes)
                    .ToList();
                
                var atestadosPorUsuario = todosAtestados
                    .GroupBy(a => a.UsuarioId)
                    .Select(g => new 
                    {
                        UsuarioId = g.Key,
                        Quantidade = g.Count()
                    })
                    .ToList();
                
                return Ok(new 
                { 
                    TotalAtestados = totalAtestados,
                    TotalUsuarios = totalUsuarios,
                    MediaPorUsuario = mediaAtestadosPorUsuario,
                    AtestadosPorMes = atestadosPorMes,
                    AtestadosPorUsuario = atestadosPorUsuario
                });
            }
            else
            {
                var atestadosDoUsuario = await _context.Atestados
                    .Where(a => a.UsuarioId == usuarioId)
                    .ToListAsync();
                
                var quantidadeUsuario = atestadosDoUsuario.Count;
                
                var hoje = DateTime.UtcNow.Date;
                var seisAtras = hoje.AddMonths(-6);
                
                var atestadosPorMes = atestadosDoUsuario
                    .Where(a => a.DataCadastro >= seisAtras)
                    .GroupBy(a => new { a.DataCadastro.Year, a.DataCadastro.Month })
                    .Select(g => new 
                    {
                        Ano = g.Key.Year,
                        Mes = g.Key.Month,
                        Quantidade = g.Count()
                    })
                    .OrderBy(x => x.Ano)
                    .ThenBy(x => x.Mes)
                    .ToList();
                
                var totalSistema = quantidadeUsuario;
                
                return Ok(new 
                { 
                    TotalAtestadosUsuario = quantidadeUsuario,
                    TotalAtestadosSistema = totalSistema,
                    AtestadosPorMes = atestadosPorMes
                });
            }
        }

        // GET: api/Atestado/Total
        [HttpGet("Total")]
        public async Task<ActionResult<object>> GetTotalAtestados([FromQuery] int usuarioId = 0)
        {
            if (usuarioId > 0)
            {
                var usuario = await _context.Usuarios.FindAsync(usuarioId);
                if (usuario == null)
                {
                    return NotFound("Usuário não encontrado");
                }
                
                var countExato = await _context.Atestados
                    .Where(a => a.UsuarioId == usuarioId)
                    .CountAsync();
                
                return Ok(new { 
                    totalAtestados = countExato,
                    TotalAtestados = countExato, 
                    quantidadeAtestados = countExato, 
                    mensagem = "Contagem real de atestados do usuário"
                });
            }
            
            var totalReal = await _context.Atestados.CountAsync();
            
            return Ok(new { 
                totalAtestados = totalReal,
                TotalAtestados = totalReal,
                quantidadeAtestados = totalReal,
                mensagem = "Contagem total real de atestados no sistema"
            });
        }
        
        // GET: api/Atestado/DashboardInfo?usuarioId=1
        [HttpGet("DashboardInfo")]
        public async Task<ActionResult<object>> GetDashboardInfo([FromQuery] int usuarioId)
        {
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return NotFound("Usuário não encontrado");
            }
            
            var historicoAtestados = await _context.Atestados
                .Where(a => a.UsuarioId == usuarioId)
                .OrderByDescending(a => a.DataCadastro)
                .ToListAsync();
                
            var quantidadeHistorico = historicoAtestados.Count;
            
            int totalSistema = 0;
            if (usuario.IsAdmin)
            {
                totalSistema = await _context.Atestados.CountAsync();
            }
            
            var dashboardData = new 
            {
                quantidadeAtestados = quantidadeHistorico,
                isAdmin = usuario.IsAdmin,
                totalAtestadosSistema = usuario.IsAdmin ? totalSistema : quantidadeHistorico,
                ultimosAtestados = historicoAtestados.Take(5).Select(a => new 
                {
                    id = a.Id,
                    dataAtestado = a.DataAtestado,
                    nomeMedico = a.NomeMedico,
                    dataCadastro = a.DataCadastro
                }).ToList()
            };
            
            return Ok(dashboardData);
        }

        // (Todos os outros métodos de Contador, DashboardReal, etc. continuam aqui...)

        // PUT: api/atestado/status/{id}
        [HttpPut("status/{id}")]
        public async Task<IActionResult> AtualizarStatus(int id, [FromBody] AtualizarStatusViewModel model)
        {
            if (model == null || model.UsuarioId <= 0)
            {
                return BadRequest("Dados inválidos para atualização de status");
            }

            var usuario = await _context.Usuarios.FindAsync(model.UsuarioId);
            if (usuario == null)
            {
                return NotFound("Usuário não encontrado");
            }

            if (!usuario.IsAdmin)
            {
                return Unauthorized("Apenas administradores podem mudar o status de atestados");
            }

            var atestado = await _context.Atestados.FindAsync(id);
            if (atestado == null)
            {
                return NotFound("Atestado não encontrado");
            }

            if (model.Status != "Aprovado" && model.Status != "Rejeitado" && model.Status != "Pendente")
            {
                return BadRequest("Status inválido. Os valores permitidos são: Aprovado, Rejeitado, Pendente");
            }

            if (model.Status == "Rejeitado" && string.IsNullOrWhiteSpace(model.MotivoRejeicao))
            {
                return BadRequest("Para rejeitar um atestado, é necessário informar o motivo da rejeição.");
            }

            atestado.Status = model.Status;
            atestado.AtualizadoPor = usuario.Id;
            atestado.DataAtualizacao = DateTime.UtcNow;
            
            if (model.Status == "Rejeitado")
            {
                atestado.MotivoRejeicao = model.MotivoRejeicao;
            }
            else
            {
                atestado.MotivoRejeicao = null;
            }

            try
            {
                await _context.SaveChangesAsync();
                return Ok(new { message = $"Status do atestado atualizado para: {model.Status}" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar status do atestado {id}: {ex.Message}");
                return StatusCode(500, "Erro ao salvar as alterações no banco de dados");
            }
        }

        // GET: api/Atestado/CorrigirStatus
        [HttpGet("CorrigirStatus")]
        public async Task<IActionResult> CorrigirStatus()
        {
            try
            {
                var atestados = await _context.Atestados.ToListAsync();
                int contadorAtualizados = 0;
                
                foreach (var atestado in atestados)
                {
                    if (string.IsNullOrEmpty(atestado.Status))
                    {
                        atestado.Status = "Pendente";
                        contadorAtualizados++;
                    }
                }
                
                if (contadorAtualizados > 0)
                {
                    await _context.SaveChangesAsync();
                }

                // O código 'PRAGMA' foi removido pois era de SQLite
                var registrosSemStatus = await _context.Atestados.CountAsync(a => a.Status == null || a.Status == "");
                
                return Ok(new {
                    atualizados = contadorAtualizados,
                    registrosSemStatus = registrosSemStatus,
                    totalRegistros = atestados.Count,
                    mensagem = $"Correção de status concluída: {contadorAtualizados} atestados atualizados."
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao corrigir status dos atestados: {ex.Message}");
                return StatusCode(500, $"Erro ao corrigir status: {ex.Message}");
            }
        }

        [HttpGet("validar")]
        public async Task<IActionResult> ValidarCid([FromQuery] string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo))
            {
                return BadRequest(new { valido = false, mensagem = "Código não fornecido." });
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();

                var apiUrl = $"https://clinicaltables.nlm.nih.gov/api/icd10cm/v3/search?sf=code&terms={codigo}";

                var response = await httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();

                    using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                    {
                        JsonElement root = doc.RootElement;

                        // O primeiro elemento (índice 0) é a contagem de resultados
                        if (root.GetArrayLength() > 0 && root[0].TryGetInt32(out int count) && count > 0)
                        {
                            var codesArray = root[1];
                            bool valido = false;
                            foreach (JsonElement codeElement in codesArray.EnumerateArray())
                            {
                                var codeRetornado = codeElement.GetString();
                                if (codeRetornado == null) continue;

                                // Aceita match exato (ex: A08.0) ou categoria pai (ex: A08 quando existem A08.0, A08.1...)
                                if (codeRetornado.Equals(codigo, StringComparison.OrdinalIgnoreCase) ||
                                    codeRetornado.StartsWith(codigo + ".", StringComparison.OrdinalIgnoreCase))
                                {
                                    valido = true;
                                    break;
                                }
                            }

                            if (valido)
                            {
                                return Ok(new { valido = true });
                            }
                        }
                    }

                    return Ok(new { valido = false, mensagem = "CID não encontrado." });
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new { valido = false, mensagem = "Erro ao consultar a API externa." });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao validar CID: {ex.GetType().Name} - {ex.Message}");
                return StatusCode(500, new { valido = false, mensagem = "Erro interno no servidor ao tentar validar o CID." });
            }
        }


    }
}