# 🤖 Project Agent Guide (agents.md)

Este documento define as instruções universais para assistentes de Inteligência Artificial (Cursor, GitHub Copilot, Claude, ChatGPT, etc.) que atuarem neste repositório.

---

## 🏛️ 1. Contexto e Diretrizes Arquiteturais

- **Tecnologia Principal:** C# / .NET (Web API).
- **Metodologia:** Spec-Driven Development (SDD).
- **Premissa Central:** Nenhum código de produção deve ser gerado sem uma especificação prévia aprovada em `docs/specs/in-progress/`.
- **Determinismo Técnico:** Todo código submetido precisa compilar sem warnings e passar na suíte de testes (`dotnet test`).
- **Padrões de Código:**
  - Uso de Records para DTOs e contratos de entrada/saída.
  - Injeção de dependência via interfaces.
  - Validações centralizadas e respostas de erro estruturadas.

---

## 🔄 2. Ciclo Operacional SDD

Qualquer assistente que iniciar uma sessão de desenvolvimento deve seguir estritamente este fluxo:

1. **Início da Sessão (`resume-session`):**
   - Ler `docs/PROJECT_STATE.md` para entender as pendências e o foco atual.
   - Criar a especificação técnica em `docs/specs/in-progress/US-[ID]-[nome].md` usando o modelo em `docs/specs/templates/spec-template.md`.
   - **Proibição:** Não implementar código de produção nesta etapa; foque exclusivamente em contratos, regras e checklist.

2. **Implementação da Spec (`implement-spec`):**
   - Seguir rigorosamente o checklist da spec ativa.
   - Ordem de implementação: Contratos/DTOs $\rightarrow$ Regras de Domínio $\rightarrow$ Endpoints/Controllers $\rightarrow$ Testes unitários/integração.
   - Rodar/validar o comando `dotnet test`.

3. **Fechamento e Memória (`save-session`):**
   - Mover o arquivo de especificação para `docs/specs/completed/`.
   - Atualizar o status interno para `Status: Completed`.
   - Atualizar `docs/PROJECT_STATE.md` (concluídos e pendências).
   - Propor mensagem semântica de commit (ex.: `feat(api): ...`).

---

## 🧰 3. Catálogo de Skills (Área Extensível para o Squad 2)

> **Nota para o Squad 2:** Adicionem abaixo instruções pontuais e receitas técnicas para que qualquer IA saiba executar comandos e rotinas do projeto com precisão.

### [Skill] Execução de Testes e Validação

- Para verificar integridade antes de qualquer push:
  ```bash
  dotnet test --verbosity normal
  ```

### [Skill] Execução Local da API

- A Solution é `VoucherCampaigns.slnx`; o projeto de entrada é `src/VoucherCampaigns.Api`:
  ```bash
  dotnet tool restore
  dotnet run --project src/VoucherCampaigns.Api
  ```
- Sem `.env`, a API usa SQLite e aplica as migrações na inicialização. Verifique com `GET /health` e `GET /api/v1/system/status`.
- O banco é escolhido por `Database__Provider` (`Sqlite` | `Postgres`) e `Database__ConnectionString` (variáveis de ambiente, `.env` ou `appsettings.json`). Nunca referencie o provider fora de `VoucherCampaigns.Infrastructure/DependencyInjection.cs`.

### [Skill] Criação de Migrações (EF Core)

- Toda migração deve existir nos dois conjuntos (SQLite e PostgreSQL). Não use `dotnet ef migrations add` diretamente; use:
  ```bash
  sh scripts/add-migration.sh NomeDaMigracao
  ```
- Para inspecionar o DDL do PostgreSQL sem um banco disponível:
  ```bash
  Database__Provider=Postgres Database__ConnectionString="Host=localhost;Database=x" \
    dotnet ef migrations script --idempotent \
    --project src/VoucherCampaigns.Infrastructure.Migrations.Postgres \
    --startup-project src/VoucherCampaigns.Api
  ```

### [Skill] Banco Local e Dados de Demonstração

- PostgreSQL local: `docker compose up -d` (arquivo `docker-compose.yml` na raiz) e as linhas de PostgreSQL do `.env.example` no `.env`.
- Tabelas, colunas, chaves, FKs e índices ficam em `snake_case` (convenção em `Persistence/SnakeCaseNaming.cs`); os `CHECK`s nas configurações usam esses nomes e precisam funcionar em SQLite e PostgreSQL.
- Novas entidades: classe no `Domain`, `IEntityTypeConfiguration` em `Infrastructure/Persistence/Configurations` com `ToTable("nome_snake")`, `DbSet` no `AppDbContext` e migração via `scripts/add-migration.sh`. O teste `MigrationsModelTests` falha se a migração for esquecida.
- O seed (`Persistence/Seed/DemoDataSeeder.cs`) só roda com `Database__SeedDemoData=true` (ligado em `appsettings.Development.json`) e quando não há nenhuma rede no banco.

### [Skill] Autenticação, Papéis e Escopo

- Configuração na seção `Auth` (`Infrastructure/Authentication/AuthOptions.cs`), validada no startup. A chave de desenvolvimento e os usuários provisórios (`Auth:MockUsers`) ficam só em `appsettings.Development.json`; nunca escreva chave ou senha no código.
- Rotas protegidas: `[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]` (analista e gestor) ou `ApproveCampaigns` (só gestor).
- Rotas que operam sobre uma rede: adicione `[ValidateTenantScope]` e receba `Guid networkId` na rota/query ou um request que implemente `INetworkScopedRequest`. Sem rede identificável o filtro falha de propósito.
- Para saber quem está chamando, injete `ICurrentUser` (`UserId`, `Role`, `NetworkId`); não leia claims direto do `HttpContext`.
- Erros: use `ApiProblems` e `ErrorCodes` (`Api/Errors/ApiProblems.cs`). Erro de contrato → `400 VALIDATION_ERROR` (automático pelo `[ApiController]`); regra de negócio → `422` com `code` próprio.
- Testes: `tests/VoucherCampaigns.IntegrationTests/Authentication/TestAuth.cs` faz login (`LoginAsync`) ou monta tokens com papel, rede e validade variáveis (`CreateToken`).

### [Skill] Regras de Negócio, Datas e Fuso

- Regra de negócio violada: lance `BusinessRuleException(code, mensagem)` (Domain/Common) com código e mensagem exata do backlog; o `ApiExceptionHandler` responde `422`. Erro de contrato que só o serviço detecta: `RequestValidationException(campo, mensagem)` → `400 VALIDATION_ERROR`.
- Mensagens de validação de campo (`[Required]`, `[StringLength]`...) sempre com `ErrorMessage` em português.
- Datas de negócio digitadas pelo usuário chegam **sem fuso** (`[LocalDateTime]`) e são convertidas com `NetworkTime` usando `Network.TimeZone`; no banco ficam em UTC e voltam com o offset da rede.
- "Agora" sempre via `TimeProvider` injetado, nunca `DateTime.UtcNow` em regra de negócio. Nos testes de integração, fixe `factory.Clock.UtcNow`; como o JWT é validado com a hora real, use `TestAuth.CreateToken` (e não o login) nesses testes.

### [Skill] Dependências entre Camadas

- `Domain` não referencia nada; `Application` → `Domain`; `Infrastructure` → `Application`; `Api` compõe tudo.
- Versões de pacotes NuGet ficam somente em `Directory.Packages.props`; warnings quebram o build (`Directory.Build.props`).
