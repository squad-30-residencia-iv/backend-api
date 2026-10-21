# 🧠 Estado Atual do Backend (Project State)

> Atualizado ao final de cada sessão de desenvolvimento.

---

## 📌 Contexto Geral

- **Sprint:** Sprint 1
- **Foco:** Banco e autenticação/escopo prontos; próximo passo é a US-01.1 (cadastro de campanha e vigência)

---

## 🚧 Tarefas em Andamento

- [ ] [SQUAD-2] US-01.1 Cadastrar campanha e definir vigência (`POST /api/v1/campaigns`), primeira rota protegida real. Decidir antes o fuso horário da RN02 (proposta: `TimeZone` na `Network`, alinhado à US-02.6).
- [ ] [SQUAD-2] Avisar o autor do commit `56839cd` e o frontend: `escopoId` agora é o Guid da rede, novos usuários de teste e erros em `ProblemDetails` com `code`.
- [ ] [SQUAD-2] Confirmar com o cliente a origem das contas de analista e gestor (importação do sistema do cliente, projeção local ou SSO).
- [ ] [SQUAD-2] Apontar `Database__ConnectionString` para o banco oficial quando ele for provisionado (apenas configuração; Supabase usa o mesmo conjunto de migrações `Postgres`).
- [ ] [SQUAD-2] Conferir as tabelas do GH-008 com o DER da issue #4 e ajustar via nova migração, se houver divergência.
- [ ] [SQUAD-2] #12 Gestão da Hierarquia Organizacional (CRUD sobre `networks`, `regions`, `establishments`, `channels`).

---

## ✅ Últimas Entregas Concluídas

- **US-02.5 — Validar o escopo de atuação do usuário:** refaz a autenticação do commit `56839cd` (spec antiga `US-07.5-autenticacao-e-escopo.md` removida: estava truncada e com o ID errado). Login provisório `POST /api/v1/auth/login` com usuários de `Auth:MockUsers` (analista e gestor da "Supermercados Norte"); JWT com `sub`, `role` e `escopoId` (Guid da rede); seção `Auth` validada no startup, sem segredo no código; ordem `UseAuthentication()` → `UseAuthorization()` corrigida; policies `ManageCampaigns` e `ApproveCampaigns`; `ICurrentUser`; TenantGuard (`ValidateTenantScopeAttribute`) por rede via rota ou `INetworkScopedRequest`; erros `ProblemDetails` com `code` (padrão da US-07.5). 46 testes novos; validado no Postman.
- **GH-008 — Banco de dados (núcleo inicial):** entidades e mapeamento EF Core de Hierarquia Organizacional (`networks`, `regions`, `establishments`, `channels`), Catálogo (`categories`, `products`) e Campanhas (`campaigns`, `campaign_items`, `campaign_scopes`), com PKs, FKs, índices únicos e `CHECK`s (RN02, RN05); nomes em `snake_case`; migração `CoreTables` nos dois conjuntos; `docker-compose.yml` com PostgreSQL 17; seed de demonstração em Development; validado em PostgreSQL 17 real.
- **GH-010 — Setup do projeto:** Solution `VoucherCampaigns.slnx` (.NET 10) em camadas (Api, Application, Domain, Infrastructure), persistência com EF Core selecionada por configuração (SQLite local / PostgreSQL), migração `InitialCreate` nos dois conjuntos aplicada na inicialização, rotas `GET /health` e `GET /api/v1/system/status`, projetos de testes unitários e de integração com `dotnet test` verde.
- Configuração do ecossistema de SDD (prompts, templates C# e governança).
- Implementação da trava de integridade via Git Hook pre-push (`dotnet test` e checagem de memória).

---

## ⚠️ Bloqueios & Decisões Recentes

- **Contas de usuário (em aberto com o cliente):** o login atual é provisório. A troca para importação, projeção local ou SSO é feita por outra implementação de `IUserCredentialStore` (ou de provedor externo), sem mexer em `ICurrentUser`, policies ou TenantGuard.
- **Escopo:** nesta fase é só a rede. Região e estabelecimento ficam para a US-02.3; alçada de aprovação para a US-01.11; isolamento na consulta para a US-01.9.
- **Erros:** contrato inválido → `400 VALIDATION_ERROR`; regra de negócio → `422` com `code` próprio. As mensagens por campo do `400` ainda são as padrão (em inglês) do DataAnnotations.
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
