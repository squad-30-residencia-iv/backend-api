# 🧠 Estado Atual do Backend (Project State)

> Atualizado ao final de cada sessão de desenvolvimento.

---

## 📌 Contexto Geral

- **Sprint:** Sprint 1
- **Foco:** Setup da solução .NET (Web API), homologação de pacotes e definição da arquitetura base

---

## 🚧 Tarefas em Andamento

- [ ] [SQUAD-2] Apontar `Database__ConnectionString` para o banco oficial quando ele for provisionado (apenas configuração).
- [ ] [SQUAD-2] Iniciar a primeira US de negócio, introduzindo as entidades de domínio e a respectiva migração (`sh scripts/add-migration.sh`).

---

## ✅ Últimas Entregas Concluídas

- **GH-010 — Setup do projeto:** Solution `VoucherCampaigns.slnx` (.NET 10) em camadas (Api, Application, Domain, Infrastructure), persistência com EF Core selecionada por configuração (SQLite local / PostgreSQL), migração `InitialCreate` nos dois conjuntos aplicada na inicialização, rotas `GET /health` e `GET /api/v1/system/status`, projetos de testes unitários e de integração com `dotnet test` verde.
- Configuração do ecossistema de SDD (prompts, templates C# e governança).
- Implementação da trava de integridade via Git Hook pre-push (`dotnet test` e checagem de memória).

---

## ⚠️ Bloqueios & Decisões Recentes

- **Banco oficial ainda não provisionado:** a troca para PostgreSQL foi validada contra um PostgreSQL 17 local em Docker (2026-10-11): migração `InitialCreate` aplicada no startup, reinício sem reaplicar, `GET /health` e `GET /api/v1/system/status` retornando 200 com `provider = Postgres`.
- **Migrações por provider:** o DDL do EF Core é específico de cada banco, então existem dois assemblies de migrações (`Infrastructure.Migrations.Sqlite` e `.Postgres`). Toda migração é gerada nos dois via `scripts/add-migration.sh`.
- **Migração inicial sem tabelas de negócio:** `InitialCreate` é uma baseline; as entidades entram com as US.
- **Configuração:** `Database__Provider` e `Database__ConnectionString`, com precedência variáveis de ambiente > `.env` (DotNetEnv) > `appsettings.json`. Configuração inválida impede a inicialização.
- **Registro do serviço de aplicação no `Program.cs`:** a camada Application não recebeu método `AddApplication()` para não exigir um pacote NuGet fora da spec.
- **Hook `pre-push`:** passou a detectar `*.slnx`; antes pulava o `dotnet test` nesta estrutura.
