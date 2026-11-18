using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("Portal");
builder.Services.AddDbContext<PortalContext>(o => o.UseSqlServer(cs));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Logs estruturados em JSON, com escopos (CorrelationId)
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

// Observabilidade: métrica de negócio, métrica técnica e span próprio
var fonte = new ActivitySource("PortalTarefas");
var medidor = new Meter("PortalTarefas");
var tarefasCriadas = medidor.CreateCounter<long>("portal.tarefas.criadas");
var duracao = medidor.CreateHistogram<double>("portal.http.duracao_ms");
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("PortalTarefas"))
    .WithTracing(t => t.AddSource("PortalTarefas")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(m => m.AddMeter("PortalTarefas")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter((_, leitor) => leitor
            .PeriodicExportingMetricReaderOptions
            .ExportIntervalMilliseconds = 10000));

// Health Checks: liveness (processo) e readiness (banco + dependência)
var urlDependencia = builder.Configuration["Dependencia:Url"]!;
builder.Services.AddHttpClient("saude",
    c => { c.BaseAddress = new Uri(urlDependencia);
           c.Timeout = TimeSpan.FromSeconds(2); });
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PortalContext>("banco", tags: ["ready"])
    .AddCheck<DependenciaCheck>("dependencia", tags: ["ready"]);

// HttpClient resiliente: timeout, retry finito e circuit breaker
builder.Services.AddHttpClient("dependencia",
        c => c.BaseAddress = new Uri(urlDependencia))
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
        o.Retry.MaxRetryAttempts = 2;
        o.Retry.Delay = TimeSpan.FromMilliseconds(200);
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.MinimumThroughput = 4;
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    });

// Rate limiting: 20 requisições por 10 s, excesso responde 429
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("api", l =>
    {
        l.PermitLimit = 20;
        l.Window = TimeSpan.FromSeconds(10);
        l.QueueLimit = 0;
    });
    o.OnRejected = (ctx, _) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "10";
        return ValueTask.CompletedTask;
    };
});

// Output caching: só leitura pública, 30 s, invalidada por tag
builder.Services.AddOutputCache(o => o.AddPolicy("Resumo",
    p => p.Expire(TimeSpan.FromSeconds(30)).Tag("tarefas")));

// Identity: usuários, senha com hash, roles e bloqueio por tentativas
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 8;
    o.Password.RequireNonAlphanumeric = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    o.User.RequireUniqueEmail = true;
}).AddEntityFrameworkStores<PortalContext>();

// Cookie de sessão: HttpOnly, SameSite Lax, vida curta, sem redirect
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c =>
    { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c =>
    { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
// Logout troca o security stamp; validar a cada requisição
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = TimeSpan.Zero);

// Políticas: por role e por claim
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CriarTarefa",
        p => p.RequireRole("Usuario", "Administrador"))
    .AddPolicy("EditarOutraEquipe",
        p => p.RequireClaim("permissao", "editar-outra-equipe"))
    .AddPolicy("ExcluirTarefa", p => p.RequireRole("Administrador"));

var app = builder.Build();

// Cria o banco (migrations), tarefas de exemplo, roles e usuários de teste
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortalContext>();
    db.Database.Migrate();
    if (!db.Tarefas.Any())
    {
        db.Tarefas.AddRange(Enumerable.Range(1, 12).Select(i => new Tarefa
        {
            Titulo = i % 4 == 0 ? $"Revisar API {i}" : $"Tarefa de exemplo {i}",
            Descricao = "Dado fictício para teste.",
            Concluida = i % 3 == 0
        }));
        db.SaveChanges();
    }
    var senha = builder.Configuration["Seed:Senha"];
    if (string.IsNullOrEmpty(senha))
        app.Logger.LogWarning("Seed:Senha ausente: usuários de teste não criados.");
    else
        await SemearAsync(scope.ServiceProvider, senha);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();

// Correlação, log de início/resultado/duração e métrica técnica.
// Não registra corpo, cabeçalhos, cookies nem query string.
app.Use(async (ctx, next) =>
{
    var id = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
    ctx.Response.Headers["X-Correlation-Id"] = id;
    var sw = Stopwatch.StartNew();
    using var escopo = app.Logger.BeginScope(
        new Dictionary<string, object> { ["CorrelationId"] = id });
    try { await next(); }
    finally
    {
        var rota = (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
            ?? "sem-rota";
        duracao.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("rota", rota));
        app.Logger.LogInformation("HTTP {Metodo} {Rota} => {Status} em {Ms} ms",
            ctx.Request.Method, rota, ctx.Response.StatusCode,
            sw.ElapsedMilliseconds);
        // Escrita bem-sucedida invalida o cache da leitura pública
        if (ctx.Request.Method != "GET" && ctx.Response.StatusCode < 300 &&
            ctx.Request.Path.StartsWithSegments("/api/tarefas"))
            await ctx.RequestServices.GetRequiredService<IOutputCacheStore>()
                .EvictByTagAsync("tarefas", default);
    }
});
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();             // /openapi/v1.json
    app.MapScalarApiReference();  // /scalar/v1
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

// Login, logout e sessão atual
var auth = app.MapGroup("/api/auth").WithTags("Auth");

auth.MapPost("/login", async (LoginDto dto, SignInManager<IdentityUser> sm) =>
{
    // Mesma resposta para e-mail inexistente, senha errada ou bloqueio
    var invalido = Results.Problem("Credenciais inválidas.", statusCode: 401);
    if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrEmpty(dto.Senha))
        return invalido;
    var r = await sm.PasswordSignInAsync(dto.Email, dto.Senha, false, true);
    return r.Succeeded ? Results.NoContent() : invalido;
}).WithName("Entrar");

auth.MapPost("/logout", async (ClaimsPrincipal u,
    SignInManager<IdentityUser> sm, UserManager<IdentityUser> um) =>
{
    if (await um.GetUserAsync(u) is { } usuario)
        await um.UpdateSecurityStampAsync(usuario);  // invalida cookies antigos
    await sm.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization().WithName("Sair");

auth.MapGet("/me", (ClaimsPrincipal u) => Results.Ok(new SessaoDto(
        u.Identity!.Name!,
        u.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
        u.FindAll("permissao").Select(c => c.Value).ToArray())))
    .RequireAuthorization().WithName("ObterSessao");

// Health Checks: liveness só indica que o processo responde;
// readiness consulta banco e dependência (resposta pública sem detalhes)
app.MapHealthChecks("/health/live",
    new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready",
    new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

// Leitura pública (sem dados pessoais): candidata a cache
app.MapGet("/api/publico/resumo", async (PortalContext db) =>
    Results.Ok(new ResumoDto(
        await db.Tarefas.CountAsync(t => !t.Concluida),
        await db.Tarefas.CountAsync(t => t.Concluida),
        DateTime.UtcNow)))
   .CacheOutput("Resumo").RequireRateLimiting("api");

// Chamada à dependência pelo HttpClient resiliente
app.MapGet("/api/diagnostico/dependencia",
    async (IHttpClientFactory f, ILogger<Program> log) =>
{
    try
    {
        var r = await f.CreateClient("dependencia").GetAsync("/sim/dependencia");
        return r.IsSuccessStatusCode ? Results.Ok(new { dependencia = "ok" })
            : Results.Problem("Dependência com erro.", statusCode: 502);
    }
    catch (BrokenCircuitException)
    {
        log.LogWarning("Circuito aberto: chamada bloqueada sem tentar");
        return Results.Problem("Dependência indisponível.", statusCode: 503);
    }
    catch (Exception e) when (e is HttpRequestException or TimeoutRejectedException)
    {
        log.LogWarning("Falha na dependência: {Tipo}", e.GetType().Name);
        return Results.Problem("Falha ao chamar a dependência.", statusCode: 502);
    }
}).RequireAuthorization();

// Simulador da dependência externa (somente Development)
var dependenciaOk = true;
if (app.Environment.IsDevelopment())
{
    app.MapGet("/sim/dependencia", () => dependenciaOk
        ? Results.Ok("ok") : Results.StatusCode(503));
    app.MapPost("/sim/dependencia/{estado}", (string estado) =>
    { dependenciaOk = estado == "ligar"; return Results.NoContent(); });
}

// Lista permitida de campos de ordenação
var ordenaveis = new Dictionary<string,
    Func<IQueryable<Tarefa>, bool, IOrderedQueryable<Tarefa>>>(
    StringComparer.OrdinalIgnoreCase)
{
    ["id"] = (q, d) => d ? q.OrderByDescending(t => t.Id)
                         : q.OrderBy(t => t.Id),
    ["titulo"] = (q, d) => d ? q.OrderByDescending(t => t.Titulo)
                             : q.OrderBy(t => t.Titulo),
    ["criadaEm"] = (q, d) => d ? q.OrderByDescending(t => t.CriadaEm)
                               : q.OrderBy(t => t.CriadaEm)
};

// Leitura privada: qualquer usuário autenticado
var api = app.MapGroup("/api/tarefas").WithTags("Tarefas")
    .RequireAuthorization().RequireRateLimiting("api");

api.MapGet("/", async (PortalContext db, int pagina = 1,
    int tamanho = 10, string ordenarPor = "criadaEm",
    string direcao = "asc", bool? concluida = null,
    string? busca = null) =>
{
    var erros = new Dictionary<string, string[]>();
    if (pagina < 1) erros["pagina"] = ["Deve ser maior ou igual a 1."];
    if (tamanho is < 1 or > 50)
        erros["tamanho"] = ["Deve estar entre 1 e 50."];
    if (!ordenaveis.TryGetValue(ordenarPor, out var ordenar))
        erros["ordenarPor"] = ["Use id, titulo ou criadaEm."];
    if (direcao is not ("asc" or "desc"))
        erros["direcao"] = ["Use asc ou desc."];
    if (erros.Count > 0) return Results.ValidationProblem(erros);

    var q = db.Tarefas.AsNoTracking();
    if (concluida is not null)
        q = q.Where(t => t.Concluida == concluida);
    if (!string.IsNullOrWhiteSpace(busca))
        q = q.Where(t => t.Titulo.Contains(busca));
    var total = await q.CountAsync();
    var itens = await ordenar!(q, direcao == "desc")
        .Skip((pagina - 1) * tamanho).Take(tamanho)
        .Select(t => new TarefaResumoDto(t.Id, t.Titulo, t.Concluida))
        .ToListAsync();
    return Results.Ok(
        new PaginaDto<TarefaResumoDto>(itens, pagina, tamanho, total));
}).WithName("ListarTarefas").Produces<PaginaDto<TarefaResumoDto>>()
  .ProducesValidationProblem();

api.MapGet("/{id:int}", async (int id, PortalContext db) =>
    await db.Tarefas.AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == id) is { } t
        ? Results.Ok(Detalhe(t)) : NaoEncontrada(id))
   .WithName("ObterTarefa").Produces<TarefaDetalheDto>()
   .ProducesProblem(404);

api.MapPost("/", async (TarefaCriarDto dto, PortalContext db) =>
{
    if (!Validar(dto, out var erros))
        return Results.ValidationProblem(erros);
    if (await db.Tarefas.AnyAsync(t => t.Titulo == dto.Titulo))
        return Results.Problem("Título já cadastrado.",
            statusCode: 409);
    using var span = fonte.StartActivity("CriarTarefa");
    var tarefa = new Tarefa
    { Titulo = dto.Titulo.Trim(), Descricao = dto.Descricao };
    db.Tarefas.Add(tarefa);
    await db.SaveChangesAsync();
    tarefasCriadas.Add(1);
    span?.SetTag("tarefa.id", tarefa.Id);
    app.Logger.LogInformation("Tarefa {TarefaId} criada", tarefa.Id);
    return Results.Created($"/api/tarefas/{tarefa.Id}",
        Detalhe(tarefa));
}).RequireAuthorization("CriarTarefa").WithName("CriarTarefa").Produces<TarefaDetalheDto>(201)
  .ProducesValidationProblem().ProducesProblem(409);

api.MapPut("/{id:int}", async (int id, TarefaAtualizarDto dto,
    PortalContext db) =>
{
    if (!Validar(dto, out var erros))
        return Results.ValidationProblem(erros);
    if (LerVersao(dto.RowVersion) is not { } versao)
    {
        erros = new() { ["rowVersion"] = ["Valor inválido."] };
        return Results.ValidationProblem(erros);
    }
    var tarefa = await db.Tarefas.FindAsync(id);
    if (tarefa is null) return NaoEncontrada(id);
    db.Entry(tarefa).Property(t => t.RowVersion)
        .OriginalValue = versao;
    tarefa.Titulo = dto.Titulo.Trim();
    tarefa.Descricao = dto.Descricao;
    tarefa.Concluida = dto.Concluida;
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Problem("Registro alterado por outra requisição.",
            statusCode: 409);
    }
    return Results.Ok(Detalhe(tarefa));
}).RequireAuthorization("EditarOutraEquipe").WithName("AtualizarTarefa").Produces<TarefaDetalheDto>()
  .ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);

api.MapDelete("/{id:int}", async (int id, PortalContext db) =>
    await db.Tarefas.Where(t => t.Id == id).ExecuteDeleteAsync() == 0
        ? NaoEncontrada(id) : Results.NoContent())
   .RequireAuthorization("ExcluirTarefa").WithName("ExcluirTarefa").Produces(204).ProducesProblem(404);

app.MapFallbackToFile("index.html");
app.Run();

static IResult NaoEncontrada(int id) =>
    Results.Problem($"Tarefa {id} não encontrada.", statusCode: 404);

static TarefaDetalheDto Detalhe(Tarefa t) =>
    new(t.Id, t.Titulo, t.Descricao, t.Concluida, t.CriadaEm,
        Convert.ToBase64String(t.RowVersion));

static byte[]? LerVersao(string s)
{
    try { return Convert.FromBase64String(s); }
    catch (FormatException) { return null; }
}

static bool Validar<T>(T dto, out Dictionary<string, string[]> erros)
{
    var r = new List<ValidationResult>();
    Validator.TryValidateObject(dto!, new(dto!), r, true);
    erros = r.GroupBy(x => x.MemberNames.FirstOrDefault() ?? "")
        .ToDictionary(g => g.Key,
            g => g.Select(x => x.ErrorMessage ?? "Inválido").ToArray());
    return erros.Count == 0;
}

// Seed controlado: a senha vem de user-secrets, nunca do repositório
static async Task SemearAsync(IServiceProvider sp, string senha)
{
    var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
    var users = sp.GetRequiredService<UserManager<IdentityUser>>();
    foreach (var r in new[] { "Usuario", "Administrador" })
        if (!await roles.RoleExistsAsync(r))
            await roles.CreateAsync(new IdentityRole(r));
    await CriarAsync(users, "usuario@portal.local", senha, "Usuario", null);
    await CriarAsync(users, "admin@portal.local", senha, "Administrador",
        "editar-outra-equipe");
}

static async Task CriarAsync(UserManager<IdentityUser> m, string email,
    string senha, string role, string? permissao)
{
    if (await m.FindByEmailAsync(email) is not null) return;
    var u = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
    if (!(await m.CreateAsync(u, senha)).Succeeded) return;
    await m.AddToRoleAsync(u, role);
    if (permissao is not null)
        await m.AddClaimAsync(u, new Claim("permissao", permissao));
}

public record LoginDto(string Email, string Senha);
public record SessaoDto(string Email, string[] Papeis, string[] Permissoes);
public record ResumoDto(int Abertas, int Concluidas, DateTime GeradoEm);

public class DependenciaCheck(IHttpClientFactory f) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext c, CancellationToken ct = default)
    {
        try
        {
            var r = await f.CreateClient("saude").GetAsync("/sim/dependencia", ct);
            return r.IsSuccessStatusCode ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("dependência indisponível");
        }
        catch { return HealthCheckResult.Unhealthy("dependência indisponível"); }
    }
}
