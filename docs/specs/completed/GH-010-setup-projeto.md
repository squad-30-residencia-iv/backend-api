# 📋 Especificação Técnica: GH-010 - Setup do Projeto (Solution, Camadas, Persistência e Migrações)

> Status: Completed
> História de Usuário Relacionada: GH-010 (task de engenharia, fora das US)
> Responsável: Iago V. R.
> Data: 2026-10-11

---

## 1. Contexto & Regras de Negócio

- **Objetivo:** inicializar a Solution `VoucherCampaigns` em .NET 10 (Web API), organizada em camadas, com persistência resiliente e migrações versionadas aplicadas automaticamente na inicialização.
- **Arquitetura em camadas** (dependências sempre apontam para dentro):

  | Projeto | Responsabilidade | Referencia |
  |---|---|---|
  | `VoucherCampaigns.Domain` | Entidades e regras de domínio (vazio até a primeira US) | — |
  | `VoucherCampaigns.Application` | Serviços de aplicação, portas (interfaces) e contratos (Records) | Domain |
  | `VoucherCampaigns.Infrastructure` | `AppDbContext`, repositórios, registro de DI | Application |
  | `VoucherCampaigns.Infrastructure.Migrations.Sqlite` | Migrações EF Core do SQLite | Infrastructure |
  | `VoucherCampaigns.Infrastructure.Migrations.Postgres` | Migrações EF Core do PostgreSQL | Infrastructure |
  | `VoucherCampaigns.Api` | Controllers, `Program.cs`, composição | Application, Infrastructure, Migrations.* |
  | `VoucherCampaigns.UnitTests` | Testes unitários | Application, Infrastructure |
  | `VoucherCampaigns.IntegrationTests` | Testes de integração (`WebApplicationFactory`) | Api |

- **Estratégia de conexão (resiliência):**
  - O provider e a connection string vêm de configuração: `appsettings.json` com sobrescrita por variáveis de ambiente (padrão ASP.NET Core). Um arquivo `.env` local (não versionado) é carregado via DotNetEnv antes da criação do host; `.env.example` é versionado.
  - Chaves: `Database__Provider` (`Sqlite` | `Postgres`) e `Database__ConnectionString`.
  - Default em `appsettings.json`: `Sqlite` com `Data Source=vouchercampaigns.db`, de modo que a aplicação sobe sem `.env`.
  - A troca para PostgreSQL é exclusivamente configuracional: apenas `AddInfrastructure` conhece o provider; serviços e repositórios não o referenciam.
- **Validações:**
  - `Database:Provider` deve ser `Sqlite` ou `Postgres` (case-insensitive).
  - `Database:ConnectionString` não pode ser vazia.
  - Configuração inválida impede a inicialização (fail-fast) com mensagem clara.
- **Migrações (DDL):**
  - EF Core Migrations, um conjunto por provider (o DDL é específico de cada banco).
  - Migrações pendentes são aplicadas no startup (`MigrateAsync`), de forma idempotente via `__EFMigrationsHistory`.
  - A migração inicial `InitialCreate` é uma **baseline sem tabelas de negócio**; entidades de domínio serão introduzidas pelas US.
  - `dotnet-ef` fixado como ferramenta local (`dotnet-tools.json`); `scripts/add-migration.sh <Nome>` gera a migração nos dois conjuntos.
- **Tratamento de exceções:** erros não tratados retornam `ProblemDetails` (RFC 7807) com status 500.
- **Qualidade:** `TreatWarningsAsErrors` habilitado em `Directory.Build.props`; versões de pacotes centralizadas em `Directory.Packages.props`.

### Pacotes NuGet autorizados por esta spec

| Pacote | Projeto | Justificativa |
|---|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | Infrastructure | Banco local/emulado |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Infrastructure | Banco relacional oficial |
| `Microsoft.EntityFrameworkCore.Design` | Api | Suporte ao `dotnet ef` (design-time) |
| `DotNetEnv` | Api | Carregar o arquivo `.env` |
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | Api | Health check do banco em `/health` |
| `Microsoft.AspNetCore.Mvc.Testing` | IntegrationTests | `WebApplicationFactory` |

Além destes, apenas os pacotes já trazidos pelos templates `webapi` e `xunit` do SDK.

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
// Application — resposta da rota de diagnóstico
public record SystemStatusResponse(
    string Provider,
    bool CanConnect,
    IReadOnlyList<string> AppliedMigrations);

// Application — porta implementada pela Infrastructure
public interface IDatabaseStatusReader
{
    Task<SystemStatusResponse> ReadAsync(CancellationToken cancellationToken);
}

// Application — serviço consumido pelo controller
public interface ISystemStatusService
{
    Task<SystemStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
}

// Infrastructure — opções de persistência (seção "Database")
public record DatabaseOptions
{
    public const string SectionName = "Database";
    public string Provider { get; init; } = string.Empty;
    public string ConnectionString { get; init; } = string.Empty;
}
```

---

## 3. Especificação do Endpoint / Serviço

### 3.1 `GET /api/v1/system/status`

- Autenticação / Autorização: Pública
- Respostas HTTP esperadas:
  - 200: `SystemStatusResponse` com o provider ativo, resultado do teste de conexão e migrações aplicadas
  - 500: Erro interno não tratado (`ProblemDetails`)

### 3.2 `GET /health`

- Autenticação / Autorização: Pública
- Respostas HTTP esperadas:
  - 200: `Healthy` (aplicação no ar e banco acessível)
  - 503: `Unhealthy` (banco inacessível)

---

## 4. Checklist de Implementação

- [x] 1. Criar a Solution `VoucherCampaigns.slnx`, `global.json`, `Directory.Build.props` e `Directory.Packages.props`
- [x] 2. Criar os projetos de `src/` e `tests/` com as referências da seção 1
- [x] 3. Criar contratos/Records e portas na camada Application (`SystemStatusResponse`, `IDatabaseStatusReader`, `ISystemStatusService`)
- [x] 4. Implementar `DatabaseOptions` com validação e fail-fast no startup
- [x] 5. Implementar `AppDbContext` e `AddInfrastructure` com seleção de provider por configuração
- [x] 6. Implementar `SystemStatusService` e `DatabaseStatusReader`
- [x] 7. Configurar carregamento do `.env` (DotNetEnv), `appsettings.json` e versionar `.env.example`
- [x] 8. Configurar `dotnet-ef` como ferramenta local e criar `scripts/add-migration.sh`
- [x] 9. Gerar a migração `InitialCreate` para SQLite e PostgreSQL
- [x] 10. Aplicar migrações automaticamente na inicialização
- [x] 11. Criar `SystemStatusController`, endpoint `/health` e tratamento de erros com `ProblemDetails`
- [x] 12. Escrever testes unitários e de integração
- [x] 13. Ajustar `scripts/hooks/pre-push` para detectar `*.slnx`
- [x] 14. Atualizar `.gitignore` (`.env`, `*.db`), `README.md` (execução local) e o catálogo de skills em `agents.md`
- [x] 15. Validar localmente via `dotnet test`

---

## 5. Casos de Teste Essenciais

- [x] Cenário Feliz (resiliência): com `Database__Provider=Sqlite`, a aplicação inicializa, aplica `InitialCreate` e `GET /api/v1/system/status` retorna 200 com `Provider = "Sqlite"`, `CanConnect = true` e a migração listada.
- [x] Cenário Feliz (health): `GET /health` retorna 200.
- [x] Cenário de Idempotência: reiniciar a aplicação sobre o mesmo banco não reaplica migrações nem falha.
- [x] Cenário de Validação: provider desconhecido ou connection string vazia impedem a inicialização.
- [x] Cenário Unitário: `SystemStatusService` devolve o resultado fornecido pela porta `IDatabaseStatusReader`.
- [x] Cenário PostgreSQL (offline): `dotnet ef migrations script --idempotent` gera o DDL do conjunto Postgres sem erros.

### Pendência conhecida

- A validação de ponta a ponta contra uma instância real de PostgreSQL depende do provisionamento do banco oficial (não há Docker/PostgreSQL no ambiente local). Deve ser registrada em `docs/PROJECT_STATE.md` no fechamento.
