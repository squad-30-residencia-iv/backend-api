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
- Para rodar a suíte de integração no PostgreSQL (com `docker compose up -d`), defina `TEST_POSTGRES_CONNECTION` sem o nome do banco. Cada `new ApiFactory()` cria um banco `vc_tests_*` próprio e o apaga no fim; sem a variável, os testes usam SQLite:
  ```bash
  TEST_POSTGRES_CONNECTION="Host=localhost;Port=5432;Username=postgres;Password=postgres" dotnet test
  ```
- Nos testes, não fixe o provider: compare com `factory.Provider` (ou `ApiFactory.DefaultProvider`), para o mesmo teste valer nos dois bancos.

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
- Com a API rodando (`bin/Debug` travado), compile e teste com `--artifacts-path <pasta temporária>`. Para a migração, rode os comandos do script com `--configuration Release`.
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
- Novas entidades: classe no `Domain`, `IEntityTypeConfiguration` em `Infrastructure/Persistence/Configurations` com `ToTable("nome_snake")`, `DbSet` no `AppDbContext` e migração via `scripts/add-migration.sh`. O teste `MigrationsModelTests` falha se a migração for esquecida. Filho com id gerado pela aplicação e adicionado pela coleção de um agregado rastreado (ex.: `Campaign.Items`) precisa de `ValueGeneratedNever()` na chave; sem isso o EF o toma como existente e tenta `UPDATE` (`DbUpdateConcurrencyException`).
- O seed de demonstração (`Persistence/Seed/DemoDataSeeder.cs`) só roda com `Database__SeedDemoData=true` (ligado em `appsettings.Development.json`) e quando não há nenhuma rede no banco. A carga de `users` (`UserAccountSeeder`) roda em toda subida em que a tabela está vazia, a partir de `Auth:MockUsers`, e não regrava uma linha já existente.

### [Skill] Autenticação, Papéis e Escopo

- O login lê a tabela `users` (`DatabaseUserCredentialStore`). A senha é hash PBKDF2; e-mail inexistente e senha errada respondem o mesmo `401`. `FindByCredentialsAsync` e `AuthenticateAsync` são assíncronos. O perfil do `200` inclui `name` (`users.name`, 1 a 150 caracteres). O JWT não leva o nome. `Auth:MockUsers` exige `Name`; o seed grava esse valor só na tabela vazia.
- Configuração na seção `Auth` (`Infrastructure/Authentication/AuthOptions.cs`), validada no startup. A chave de desenvolvimento e a carga inicial (`Auth:MockUsers`) ficam só em `appsettings.Development.json`; nunca escreva chave ou senha no código. `Auth:MockUsers` não autentica: só grava `users` quando a tabela está vazia.
- Papéis (`Application/Authentication/Roles.cs`): `analista` e `gestor` pertencem a uma rede; `administrador` é o administrador da plataforma (PR-15), global e sem `escopoId` no token. Em `Auth:MockUsers` e em `users`, o administrador fica sem `NetworkId` e os demais exigem um; o startup recusa o contrário na carga inicial. `users.network_id` não tem chave estrangeira para `networks`.
- Rotas protegidas: `[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]` (analista e gestor), `SubmitCampaigns` (só analista), `ApproveCampaigns` (só gestor), `ReadHierarchy` (administrador, analista e gestor), `ManageHierarchy` (só administrador), `ReadCatalog` (administrador, analista e gestor) ou `ManageCatalog` (só administrador). Para restringir a escrita numa controller de leitura, some o `[Authorize]` da ação ao da classe: as policies se combinam.
- Decisão da campanha: `POST /api/v1/campaigns/{id}/submit` (analista, só de `Draft`), `POST .../approve` e `POST .../reject` (gestor, só de `PendingApproval`). O mesmo `SaveChanges` grava a campanha e uma linha em `campaign_status_events` (id da aplicação, `ValueGeneratedNever`). Aprovar com início futuro grava `Scheduled`; com a vigência já começada, `Active`. `Rejected` não volta a rascunho. A tabela `AuditEntry` do motor não entra nesse fluxo.
- Rotas que operam sobre uma rede: adicione `[ValidateTenantScope]` e receba `Guid networkId` na rota/query ou um request que implemente `INetworkScopedRequest`. Sem rede identificável o filtro falha de propósito. O filtro e o `NetworkScope.Ensure` liberam o administrador. `Guid` vazio passa pelo filtro: o serviço deve validá-lo com `InputValues.RequiredId` (`400`) e chamar `NetworkScope.Ensure`.
- Para saber quem está chamando, injete `ICurrentUser` (`UserId`, `Role`, `IsPlatformAdmin`, `NetworkId`); não leia claims direto do `HttpContext`. `NetworkId` lança para o administrador: consulte `IsPlatformAdmin` antes.
- Erros: use `ApiProblems` e `ErrorCodes` (`Api/Errors/ApiProblems.cs`). Erro de contrato → `400 VALIDATION_ERROR` (automático pelo `[ApiController]`); regra de negócio → `422` com `code` próprio; recurso ausente → `ResourceNotFoundException` (`404 NOT_FOUND`); rede descoberta só no serviço (filho cujo pai está em outra rede) → `ScopeForbiddenException` (`403 SCOPE_FORBIDDEN`). O filtro `[ValidateTenantScope]` continua obrigatório quando a rede já está na rota, na query ou no request.
- Cadastros de hierarquia e catálogo: `DELETE` inativa (`is_active`) e a cascata para os descendentes ocorre na mesma `SaveChanges` (rede → regiões → lojas; categoria → produtos). O canal é atributo da loja (PR-11), sem rota própria: `Establishment.EnableChannels` liga e desliga as linhas de `channels`, reaproveitando a linha do mesmo tipo. Pai inexistente informado no corpo → `422` com `code` e a mensagem do backlog; `404` só para o id da URL ou o pai da query. Violação de índice único vira `BusinessRuleException` em `Persistence/ConstraintSave.cs`. Categoria e produto não têm rede. `bool?` e `Guid?` nos contratos obrigatórios, para que o campo omitido seja `400` e não um valor padrão.
- Testes: `tests/VoucherCampaigns.IntegrationTests/Authentication/TestAuth.cs` faz login (`LoginAsync`) ou monta tokens com papel, rede e validade variáveis (`CreateToken`; com `Roles.Administrador` e sem rede, o token sai sem `escopoId`).
- Aplicações satélites (US-07.5): esquema `Satellite` (audiência `Auth:Satellite:Audience`), separado do JWT de usuário. Rotas de satélite ficam em `/api/v1/satellite` e usam `[Authorize(Policy = AuthorizationPolicies.SatelliteApp)]` ou a policy do escopo (`SatelliteReadCampaigns`, `SatelliteIssueVouchers`, `SatelliteRedeemVouchers`). Para saber qual aplicação chama, injete `ICurrentSatelliteApp`; `ICurrentUser` não vale para satélite. `[ValidateTenantScope]` usa a rede da aplicação. Nos testes, `TestAuth.CreateSatelliteToken` varia escopos, rede, validade e chave; a aplicação `pdv-demo` vem da carga de Development.

### [Skill] CORS do portal

- O portal e a API ficam em origens diferentes. A allowlist é `Cors:AllowedOrigins` (`Api/Cors/PortalCorsOptions.cs`), validada no startup. Em Development: `http://localhost:5173`. Em produção: `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, etc.
- Origem válida é `http` ou `https` absoluta, sem caminho, consulta, fragmento ou barra final. Entrada inválida sobe com `Configuração inválida`. Lista vazia não registra a policy.
- `UseCors` fica antes de `UseAuthentication`. Métodos `GET`, `POST`, `PUT` e `DELETE`. Cabeçalhos `Authorization` e `Content-Type`. Não usar `AllowCredentials`: o JWT vai no cabeçalho, não em cookie.
- A lista é da origem do portal, não do usuário. O navegador em `http://127.0.0.1:5173` não é a mesma origem de `http://localhost:5173`.

### [Skill] CORS do portal

- O portal e a API ficam em origens diferentes. A allowlist é `Cors:AllowedOrigins` (`Api/Cors/PortalCorsOptions.cs`), validada no startup. Em Development: `http://localhost:5173`. Em produção: `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, etc.
- Origem válida é `http` ou `https` absoluta, sem caminho, consulta, fragmento ou barra final. Entrada inválida sobe com `Configuração inválida`. Lista vazia não registra a policy.
- `UseCors` fica antes de `UseAuthentication`. Métodos `GET`, `POST`, `PUT` e `DELETE`. Cabeçalhos `Authorization` e `Content-Type`. Não usar `AllowCredentials`: o JWT vai no cabeçalho, não em cookie.
- A lista é da origem do portal, não do usuário. O navegador em `http://127.0.0.1:5173` não é a mesma origem de `http://localhost:5173`.

### [Skill] Regras de Negócio, Datas e Fuso

- Regra de negócio violada: lance `BusinessRuleException(code, mensagem)` (Domain/Common) com código e mensagem exata do backlog; o `ApiExceptionHandler` responde `422`. Erro de contrato que só o serviço detecta: `RequestValidationException(campo, mensagem)` → `400 VALIDATION_ERROR`.
- Mensagens de validação de campo (`[Required]`, `[StringLength]`...) sempre com `ErrorMessage` em português.
- Datas de negócio digitadas pelo usuário chegam **sem fuso** (`[LocalDateTime]`) e são convertidas com `NetworkTime` usando `Network.TimeZone`; no banco ficam em UTC e voltam com o offset da rede.
- "Agora" sempre via `TimeProvider` injetado, nunca `DateTime.UtcNow` em regra de negócio. Nos testes de integração, fixe `factory.Clock.UtcNow`; como o JWT é validado com a hora real, use `TestAuth.CreateToken` (e não o login) nesses testes.
- Status da campanha para leitura ou decisão: `Campaign.EffectiveStatus(utcNow)` (PR-04), nunca `campaign.Status` direto. Agendada, ativa e pausada seguem a vigência pelo relógio, e o minuto do fim conta inteiro (D9: às 23:59:59 ainda vale); o banco guarda o status da decisão. Transição nova (como `Pause` e `End`, US-01.8) parte do status efetivo, devolve um `CampaignStatusEvent` e passa pelo `RecordStatusChange` do serviço, que grava o histórico e a auditoria. A concessão usa `CanIssueVouchers`, que já olha o efetivo.

### [Skill] Motor de Concessão (EP-04)

- Fluxo em `Application/Vouchers/ConcessionService.cs` (spec `GH-045-motor-de-concessao.md`, seção 1.2): loja e canal → idempotência por rede + `transactionId` → liberação das reservas vencidas → candidatas aprovadas no escopo → `ConcessionEvaluator` (domínio, sem I/O: condições, status efetivo, limite por cliente, alvo e desempate D1) → reserva atômica → voucher provisório, `VoucherEvent` e auditoria no mesmo `SaveChanges`.
- Saldo e status do voucher mudam só por comandos condicionais do `IConcessionRepository` (`UPDATE ... WHERE`, via `ExecuteUpdateAsync`): `TryReserveAsync` é o **primeiro comando da transação** e trava a linha da campanha; `TryConfirmVoucherAsync`, `TryDiscardVoucherAsync`, `ConsumeReservationAsync` e `ReleaseReservationAsync` nunca leem o contador para gravar. Não some nem subtraia `IssuedVouchers`/`ReservedVouchers` na mão. No SQLite, uma transação que lê antes de escrever recebe `database is locked` sob concorrência: por isso a escrita vem primeiro.
- Esgotada é derivada: `Campaign.EffectiveStatus` devolve `SoldOut` quando `issued + reserved >= total`; a reserva vencida devolve o saldo (`IReservationReleaser`, chamado na concessão, na confirmação/desfazimento e pelo `ReservationSweepService` a cada `Vouchers:ReservationSweepSeconds`; `0` desliga, e o `ApiFactory` desliga).
- Status do voucher e da concessão na leitura são os efetivos (`Voucher.EffectiveStatus`, `Concession.EffectiveStatus`): provisório vencido = `Discarded`/`Expired`, disponível vencido = `Expired`. A validade é inclusiva até o último segundo (`BusinessTime`, D9); o mesmo vale para o fim da vigência da campanha.
- Rotas em `Api/Controllers/SatelliteConcessionsController.cs` (policy `SatelliteIssueVouchers`); mensagens em `ConcessionContract`, regras em `Domain/Vouchers/ConcessionErrors.cs`. Nos testes de integração, `Vouchers/MotorScenario.cs` cria uma loja por cenário para as campanhas dos outros testes não alcançarem a compra.

### [Skill] Trilha de Auditoria

- Escopo da campanha (US-02.3): `campaign_scopes` tem uma linha por local e canal; região e loja nulas = rede inteira, e a campanha vale na união dos locais. Uma coleção da campanha substituída por inteiro (itens, condições, escopo) usa `ValueGeneratedNever` na chave e ids v7 em sequência para manter a ordem informada.
- Ação relevante (submeter, aprovar, reprovar, pausar, encerrar, duplicar, emitir, confirmar e desfazer a concessão; depois resgatar): monte um `AuditEntry` (`Domain/Audit`) e chame `IAuditTrail.Append` antes do `SaveChanges` da operação, que grava os dois juntos. Tipo do evento em `AuditEventTypes`; autor `AuditActorType.User` (`sub` do token) ou `Application` (id da aplicação satélite); payload JSON curto.
- `AuditEntry`, `CampaignStatusEvent` e `VoucherEvent` implementam `IAppendOnly`: o `AppDbContext` recusa alterar ou apagar esses registros. Não crie rota de alteração para eles.

### [Skill] Dependências entre Camadas

- `Domain` não referencia nada; `Application` → `Domain`; `Infrastructure` → `Application`; `Api` compõe tudo.
- Versões de pacotes NuGet ficam somente em `Directory.Packages.props`; warnings quebram o build (`Directory.Build.props`).
