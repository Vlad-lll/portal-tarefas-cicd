using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

// Entidade com token de concorrência (RowVersion)
public class Tarefa
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descricao { get; set; }
    public bool Concluida { get; set; }
    public DateTime CriadaEm { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

// Dtos.cs: a entidade nunca sai da API
public class TarefaCriarDto
{
    [Required, StringLength(100, MinimumLength = 3)]
    public string Titulo { get; init; } = "";
    [StringLength(500)] public string? Descricao { get; init; }
}
public class TarefaAtualizarDto : TarefaCriarDto
{
    public bool Concluida { get; init; }
    [Required] public string RowVersion { get; init; } = "";
}
public record TarefaResumoDto(int Id, string Titulo, bool Concluida);
public record TarefaDetalheDto(int Id, string Titulo, string? Descricao,
    bool Concluida, DateTime CriadaEm, string RowVersion);
public record PaginaDto<T>(IReadOnlyList<T> Itens,
    int Pagina, int Tamanho, int Total);

// Contexto do EF Core
public class PortalContext(DbContextOptions<PortalContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Tarefa> Tarefas => Set<Tarefa>();
}
