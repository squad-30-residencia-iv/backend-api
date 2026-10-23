# 🧠 Estado Atual do Backend (Project State)

> Atualizado ao final de cada sessão de desenvolvimento.

---

## 📌 Contexto Geral

- **Sprint:** Sprint 1
- **Foco:** Hierarquia e catálogo prontos (US-02.1 / US-02.4). Próximos passos continuam a consulta de campanhas (US-01.9), a configuração da campanha (US-01.2 a 01.5) e a submissão para aprovação (US-01.6)

---

## 🚧 Tarefas em Andamento

- [ ] [SQUAD-2] US-01.9 Consultar campanhas (o `Location` do `POST /api/v1/campaigns` já aponta para `GET /api/v1/campaigns/{id}`), com isolamento por rede (US-02.5, cenário 3).
- [ ] [SQUAD-2] US-01.2 a US-01.5: alvos e benefícios, quantidade, condições de geração e público elegível da campanha em rascunho.
- [ ] [SQUAD-2] US-01.6 Submeter para aprovação (`Draft` → `PendingApproval`).
- [ ] [SQUAD-2] Confirmar com o cliente se o fim da vigência às 23:59 inclui o minuto inteiro (hoje termina em 23:59:00).
- [ ] [SQUAD-2] Avisar o autor do commit `56839cd` e o frontend: `escopoId` agora é o Guid da rede, novos usuários de teste e erros em `ProblemDetails` com `code`.
- [ ] [SQUAD-2] Confirmar com o cliente a origem das contas de analista e gestor (importação do sistema do cliente, projeção local ou SSO).
- [ ] [SQUAD-2] Apontar `Database__ConnectionString` para o banco oficial quando ele for provisionado (apenas configuração; Supabase usa o mesmo conjunto de migrações `Postgres`).
- [ ] [SQUAD-2] Conferir as tabelas do GH-008 com o DER da issue #4 e ajustar via nova migração, se houver divergência.
- [ ] [SQUAD-2] US-02.3 Escopo da campanha por região, loja e canal (o CRUD desses cadastros já existe).
- [ ] [SQUAD-2] US-02.6 Herança de fuso por região/loja e moeda. O fuso já é obrigatório na rede.
- [ ] [SQUAD-2] Decisão D7: efeito de uma loja única inativada sobre campanha vigente (US-02.2).

---

## ✅ Últimas Entregas Concluídas

- **US-02.1 / US-02.4 — Hierarquia organizacional e catálogo (#12, com a inativação da US-02.2):** CRUD de `/api/v1/networks`, `/regions`, `/establishments`, `/channels`, `/categories` e `/products` para analista e gestor. Hierarquia presa à rede do token (`403 SCOPE_FORBIDDEN`); catálogo autenticado e compartilhado, porque as tabelas não têm rede. Contrato inválido, `Guid` vazio ou `isActive` omitido → `400 VALIDATION_ERROR`; duplicidade e pai inativo → `422` com `code`; ausente → `404 NOT_FOUND`. `DELETE` inativa (`is_active`) e propaga aos descendentes na mesma gravação; reativar exige pai ativo. `NetworkResponse` exige e devolve `timeZone` (IANA). `CreatedAt` via `TimeProvider`. `Location` relativo e em minúsculas. 25 testes de integração novos; `dotnet test` com 61 unitários e 86 de integração, em SQLite.
- **US-01.1 — Cadastrar campanha e definir vigência:** `POST /api/v1/campaigns` (analista e gestor, TenantGuard) cria a campanha sempre em `Draft`, com Guid v7. RN02 no domínio (`Campaign.CreateDraft`): início posterior ou igual ao fim → `422 CAMPAIGN_INVALID_PERIOD`; início antes do minuto atual → `422 CAMPAIGN_START_IN_PAST`; rede inexistente/inativa → `422 NETWORK_UNAVAILABLE`. `Campaign.CanIssueVouchers` (só `Active` na vigência) pronto para o EP-04. Novo `Network.TimeZone` (migração `NetworkTimeZone`, padrão `America/Sao_Paulo`): datas chegam sem fuso, valem no fuso da rede, ficam em UTC e voltam com offset. `CampaignStatus` ganhou `PendingApproval` (PR-02). Padrão `BusinessRuleException` → `422` via `ApiExceptionHandler`. 40 testes novos; validado em PostgreSQL 17 e Postman.
- **US-02.5 — Validar o escopo de atuação do usuário:** refaz a autenticação do commit `56839cd` (spec antiga `US-07.5-autenticacao-e-escopo.md` removida: estava truncada e com o ID errado). Login provisório `POST /api/v1/auth/login` com usuários de `Auth:MockUsers` (analista e gestor da "Supermercados Norte"); JWT com `sub`, `role` e `escopoId` (Guid da rede); seção `Auth` validada no startup, sem segredo no código; ordem `UseAuthentication()` → `UseAuthorization()` corrigida; policies `ManageCampaigns` e `ApproveCampaigns`; `ICurrentUser`; TenantGuard (`ValidateTenantScopeAttribute`) por rede via rota ou `INetworkScopedRequest`; erros `ProblemDetails` com `code` (padrão da US-07.5). 46 testes novos; validado no Postman.
- **GH-008 — Banco de dados (núcleo inicial):** entidades e mapeamento EF Core de Hierarquia Organizacional (`networks`, `regions`, `establishments`, `channels`), Catálogo (`categories`, `products`) e Campanhas (`campaigns`, `campaign_items`, `campaign_scopes`), com PKs, FKs, índices únicos e `CHECK`s (RN02, RN05); nomes em `snake_case`; migração `CoreTables` nos dois conjuntos; `docker-compose.yml` com PostgreSQL 17; seed de demonstração em Development; validado em PostgreSQL 17 real.
- **GH-010 — Setup do projeto:** Solution `VoucherCampaigns.slnx` (.NET 10) em camadas (Api, Application, Domain, Infrastructure), persistência com EF Core selecionada por configuração (SQLite local / PostgreSQL), migração `InitialCreate` nos dois conjuntos aplicada na inicialização, rotas `GET /health` e `GET /api/v1/system/status`, projetos de testes unitários e de integração com `dotnet test` verde.
- Configuração do ecossistema de SDD (prompts, templates C# e governança).
- Implementação da trava de integridade via Git Hook pre-push (`dotnet test` e checagem de memória).

---

## ⚠️ Bloqueios & Decisões Recentes

- **Datas de campanha:** sem fuso no request, interpretadas no fuso da rede; "agora" via `TimeProvider`, comparado na precisão de minuto. Herança de fuso por região/loja e moeda ficam para a US-02.6.
- **Fluxo de status:** a campanha nasce em rascunho; o analista decide quando submeter (US-01.6), e só então ela vai para `PendingApproval` e aparece ao gestor.
- **Branches empilhadas:** `feat/US-01.1-cadastrar-campanha-e-vigencia` parte de `fix/US-02.5-validar-escopo-de-atuacao`; o PR da US-01.1 deve ter base na branch da US-02.5 até ela ser mesclada.
- **Contas de usuário (em aberto com o cliente):** o login atual é provisório. A troca para importação, projeção local ou SSO é feita por outra implementação de `IUserCredentialStore` (ou de provedor externo), sem mexer em `ICurrentUser`, policies ou TenantGuard.
- **Escopo:** o token continua limitado a uma rede. O CRUD de região, loja e canal já respeita essa rede. O escopo da campanha (região, loja e canal) continua na US-02.3; alçada de aprovação na US-01.11; isolamento na consulta de campanhas na US-01.9. Categoria e produto não têm `network_id` e são compartilhados.
- **Erros:** contrato inválido → `400 VALIDATION_ERROR` (mensagens de campo em português, via `ErrorMessage`); regra de negócio → `422` com `code` próprio; recurso ausente → `404 NOT_FOUND` (`ResourceNotFoundException`); rede resolvida no serviço, fora do token → `403 SCOPE_FORBIDDEN` (`ScopeForbiddenException`).
- **Inativação:** `DELETE` de hierarquia e catálogo não apaga a linha. Marca `is_active = false` e inativa os descendentes na mesma `SaveChanges`. Reativar um filho exige o pai ativo.
- **Fuso no cadastro da rede:** `timeZone` é obrigatório no `POST` e no `PUT` de rede e volta na resposta. Herança por região e loja permanece na US-02.6.
- **Sem refresh token:** o token expira em 60 min; refresh entra com a projeção local de usuários.
- **US-07.5 real** (autenticar aplicações satélites) continua no EP-07.
- **Banco local:** PostgreSQL 17 via `docker compose up -d`. O SQLite continua como padrão sem `.env`, para quem não tem Docker.
- **Supabase:** é PostgreSQL gerenciado; a troca é só de connection string (conexão direta ou Session pooler, `SSL Mode=Require`).
- **Modelo de dados:** baseado em `requisitos.md` e nas telas de criação de campanha do frontend, porque o DER da issue #4 não está no repositório. Escopo da campanha em `campaign_scopes`: sem região e sem loja = rede inteira.
- **Nomes `snake_case`:** convenção própria em `Persistence/SnakeCaseNaming.cs`, sem pacote novo. Os `CHECK`s usam esses nomes e funcionam em SQLite e PostgreSQL (o de benefício usa `CAST(... AS REAL)` porque o SQLite grava decimal como texto).
- **Seed:** `UseSeeding`/`UseAsyncSeeding` do EF Core, controlado por `Database:SeedDemoData` (ligado só em `appsettings.Development.json`); roda após as migrações e só se não houver nenhuma rede.
- **Sincronia modelo × migrações:** `MigrationsModelTests` falha se uma entidade mudar sem migração nova.
- **Migrações por provider:** o DDL do EF Core é específico de cada banco, então existem dois assemblies de migrações (`Infrastructure.Migrations.Sqlite` e `.Postgres`). Toda migração é gerada nos dois via `scripts/add-migration.sh`.
- **Configuração:** `Database__Provider`, `Database__ConnectionString` e `Database__SeedDemoData`, com precedência variáveis de ambiente > `.env` (DotNetEnv) > `appsettings.json`. Configuração inválida impede a inicialização.
- **Registro do serviço de aplicação no `Program.cs`:** a camada Application não recebeu método `AddApplication()` para não exigir um pacote NuGet fora da spec.
- **Hook `pre-push`:** detecta `*.slnx`.
