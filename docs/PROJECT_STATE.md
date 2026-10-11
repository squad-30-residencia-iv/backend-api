# 🧠 Estado Atual do Backend (Project State)

> Atualizado ao final de cada sessão de desenvolvimento.

---

## 📌 Contexto Geral

- **Sprint:** Sprint 1
- **Foco:** Setup da solução .NET (Web API), homologação de pacotes e definição da arquitetura base

---

## 🚧 Tarefas em Andamento

- [ ] [SQUAD-2] Validar a conexão de ponta a ponta com uma instância real de PostgreSQL assim que o banco oficial for provisionado (pendência da GH-010).
- [ ] [SQUAD-2] Iniciar a primeira US de negócio, introduzindo as entidades de domínio e a respectiva migração (`sh scripts/add-migration.sh`).

---

## ✅ Últimas Entregas Concluídas

- **GH-010 — Setup do projeto:** Solution `VoucherCampaigns.slnx` (.NET 10) em camadas (Api, Application, Domain, Infrastructure), persistência com EF Core selecionada por configuração (SQLite local / PostgreSQL), migração `InitialCreate` nos dois conjuntos aplicada na inicialização, rotas `GET /health` e `GET /api/v1/system/status`, projetos de testes unitários e de integração com `dotnet test` verde.
- Configuração do ecossistema de SDD (prompts, templates C# e governança).
- Implementação da trava de integridade via Git Hook pre-push (`dotnet test` e checagem de memória).

---

## ⚠️ Bloqueios & Decisões Recentes

- **Banco oficial ainda não provisionado:** o conjunto de migrações do PostgreSQL foi validado apenas offline (`dotnet ef migrations script --idempotent`) e pela tentativa de conexão do Npgsql; falta o teste contra uma instância real.
- **Migrações por provider:** o DDL do EF Core é específico de cada banco, então existem dois assemblies de migrações (`Infrastructure.Migrations.Sqlite` e `.Postgres`). Toda migração é gerada nos dois via `scripts/add-migration.sh`.
- **Migração inicial sem tabelas de negócio:** `InitialCreate` é uma baseline; as entidades entram com as US.
- **Configuração:** `Database__Provider` e `Database__ConnectionString`, com precedência variáveis de ambiente > `.env` (DotNetEnv) > `appsettings.json`. Configuração inválida impede a inicialização.
- **Registro do serviço de aplicação no `Program.cs`:** a camada Application não recebeu método `AddApplication()` para não exigir um pacote NuGet fora da spec.
- **Hook `pre-push`:** passou a detectar `*.slnx`; antes pulava o `dotnet test` nesta estrutura.
