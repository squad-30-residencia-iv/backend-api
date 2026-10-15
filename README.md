# 🛠️ Guia de Desenvolvimento: Fluxo de Trabalho e SDD (.NET)

Este repositório adota a metodologia **Spec-Driven Development (SDD)** combinada com governança assistida por IA. O objetivo é garantir rastreabilidade, padronização de contratos de API e integridade técnica antes de qualquer código de produção ser escrito.

---

## 1. Setup Inicial Obrigatório (Configuração de Hooks)

Por questões de segurança padrão do Git, automações locais de hooks não são ativadas sozinhas após o clone.

Logo após clonar o repositório, abra o terminal na raiz do projeto e execute uma única vez:

```bash
git config core.hooksPath scripts/hooks
```

> **Por que isso é necessário?**  
> Esse comando vincula o script de `pre-push` do repositório ao seu Git local. Ele assegura que ninguém envie código se a suíte de testes falhar (`dotnet test`) ou se o relatório de estado (`docs/PROJECT_STATE.md`) não tiver sido atualizado.

---

## 2. Entendendo o Ciclo de Vida do SDD

Nenhum endpoint, serviço ou entidade de banco deve ser criado sem uma especificação prévia. O ciclo de trabalho é composto por três etapas:

1. **Início (`resume-session`):** Resgate o estado atual em `docs/PROJECT_STATE.md`, defina o escopo da tarefa e gere a especificação técnica em `docs/specs/in-progress/US-[ID]-[nome].md` usando o template oficial.
2. **Implementação (`implement-spec`):** O código é implementado respeitando a ordem: Contratos (DTOs/Records) $\rightarrow$ Validações/Regras de Domínio $\rightarrow$ Endpoints/Controllers $\rightarrow$ Testes unitários/integração.
3. **Fechamento (`save-session`):** Após validação via `dotnet test`, a especificação é movida para `docs/specs/completed/`, o arquivo `docs/PROJECT_STATE.md` é atualizado e um commit semântico é gerado.

---

## 3. Como Operar o Fluxo no Seu Editor

### Opção A: Utilizando o Cursor (Recomendado)

O Cursor já possui regras modulares integradas em `.cursor/rules/`. Você só precisa invocar os prompts no chat da IDE:

1. **Iniciar uma tarefa:**  
   Abra o chat (`Ctrl + L` ou `Cmd + L`) e envie:
   ```text
   @resume-session.md Quero implementar a história [ID-US] com o objetivo de [descrever brevemente o escopo].
   ```
2. **Implementar o código:**  
   Com a especificação aprovada em `docs/specs/in-progress/`, envie:
   ```text
   @implement-spec.md
   ```
3. **Encerrar e consolidar a memória:**  
   Após os testes passarem, envie:
   ```text
   @save-session.md
   ```

---

### Opção B: Utilizando VS Code, Visual Studio, JetBrains Rider ou Outras IAs

Se você utiliza outro editor ou assistentes externos (Claude Web, ChatGPT, GitHub Copilot, Gemini):

1. **Início da Sessão:**
   - Abra o arquivo `docs/prompts/resume-session.md`.
   - Copie o conteúdo e envie ao seu assistente de IA junto com o conteúdo de `docs/PROJECT_STATE.md`.
   - Solicite a criação da especificação técnica baseada em `docs/specs/templates/spec-template.md`.
   - Salve o arquivo gerado dentro de `docs/specs/in-progress/US-[ID]-[nome].md`.

2. **Implementação:**
   - Abra o arquivo `docs/prompts/implement-spec.md` e envie o conteúdo da sua spec ativa ao assistente de IA.
   - Implemente o código C# respeitando o checklist da spec.
   - Execute a suíte de testes no terminal:
     ```bash
     dotnet test --verbosity normal
     ```
   - Só avance quando todos os testes passarem sem erros.

3. **Fechamento da Sessão:**
   - Abra o arquivo `docs/prompts/save-session.md`.
   - Mova a spec de `docs/specs/in-progress/` para `docs/specs/completed/` e altere o cabeçalho para `Status: Completed`.
   - Abra `docs/PROJECT_STATE.md`, adicione a entrega em **Últimas Entregas Concluídas** e atualize as **Tarefas em Andamento**.
   - Crie o commit semântico e envie:
     ```bash
     git add .
     git commit -m "feat(api): implementa contratos e endpoints da US-[ID]"
     git push origin [sua-branch]
     ```

---

## 4. Executando a API Localmente

Pré-requisito: **.NET SDK 10** (a versão é fixada em `global.json`).

```bash
dotnet tool restore                              # instala o dotnet-ef definido em dotnet-tools.json
dotnet run --project src/VoucherCampaigns.Api    # sobe a API em http://localhost:5026
```

Sem nenhuma configuração extra, a API usa **SQLite** (`src/VoucherCampaigns.Api/vouchercampaigns.db`) e aplica as migrações pendentes na inicialização. Rotas de verificação:

- `GET /health`: estado da aplicação e do banco.
- `GET /api/v1/system/status`: provider ativo, teste de conexão e migrações aplicadas.

### Estrutura da Solution (`VoucherCampaigns.slnx`)

| Projeto | Responsabilidade |
|---|---|
| `src/VoucherCampaigns.Api` | Controllers, `Program.cs` e composição |
| `src/VoucherCampaigns.Application` | Serviços de aplicação, portas (interfaces) e contratos (Records) |
| `src/VoucherCampaigns.Domain` | Entidades e regras de domínio |
| `src/VoucherCampaigns.Infrastructure` | `AppDbContext`, repositórios e registro de DI |
| `src/VoucherCampaigns.Infrastructure.Migrations.Sqlite` / `.Postgres` | Migrações EF Core de cada banco |
| `tests/VoucherCampaigns.UnitTests` / `.IntegrationTests` | Testes unitários e de integração |

### Trocando de banco (SQLite ⇄ PostgreSQL)

A troca é apenas configuracional. Copie `.env.example` para `.env` na raiz do repositório e ajuste:

```dotenv
Database__Provider=Postgres
Database__ConnectionString="Host=localhost;Port=5432;Database=vouchercampaigns;Username=postgres;Password=postgres"
```

Ordem de precedência: variáveis de ambiente reais > `.env` > `appsettings.json`. Valores aceitos em `Database__Provider`: `Sqlite` e `Postgres`; uma configuração inválida impede a inicialização.

### PostgreSQL local com Docker

O `docker-compose.yml` da raiz sobe um PostgreSQL 17 com o banco `vouchercampaigns` (usuário e senha `postgres`), na porta 5432:

```bash
docker compose up -d       # sobe o banco
docker compose down        # para (os dados ficam no volume)
docker compose down -v     # para e apaga os dados
```

Depois, use as linhas de PostgreSQL do `.env.example` no seu `.env` e rode a API: as migrações são aplicadas na inicialização.

**Supabase:** como ele é um PostgreSQL gerenciado, basta trocar a `Database__ConnectionString` pela do painel do projeto (conexão direta ou *Session pooler*, com `SSL Mode=Require`). As mesmas migrações são usadas.

### Tabelas do núcleo inicial

| Grupo | Tabelas |
|---|---|
| Hierarquia organizacional | `networks` → `regions` → `establishments` → `channels` |
| Catálogo | `categories` → `products` |
| Campanhas | `campaigns`, `campaign_items` (alvo, benefício e quantidade), `campaign_scopes` (rede, região ou loja + canal) |

Nomes em `snake_case`; enums gravados como texto. A especificação completa (chaves, índices e `CHECK`s) está em `docs/specs/completed/GH-008-banco-de-dados.md`.

### Dados de demonstração (seed)

Em ambiente `Development`, a API insere 1 rede, 1 região, 3 lojas (001 Loja Centro, 002 Loja Shopping, 003 Loja Online) com seus canais, 3 categorias e 3 produtos, logo após aplicar as migrações. O seed só roda se o banco ainda não tiver nenhuma rede. Ele é controlado por `Database__SeedDemoData` (`true`/`false`) e nunca deve ser ligado em produção.

### Criando uma migração

O DDL do EF Core é específico de cada banco, por isso toda migração é gerada nos dois conjuntos:

```bash
sh scripts/add-migration.sh NomeDaMigracao
```

Faça o commit dos arquivos gerados nos dois projetos `Infrastructure.Migrations.*`. Elas são aplicadas automaticamente no próximo start da API.

---

## 5. Travas e Boas Práticas

- **Não use `--no-verify` no push:** Se o push for bloqueado, verifique a saída do terminal. Isso indica que os testes falharam ou que o arquivo `docs/PROJECT_STATE.md` não foi modificado nesta ramificação.
- **Padronização de Quebras de Linha (`LF`):** Arquivos de scripts em `scripts/` e arquivos de governança devem ser salvos com terminação Unix (`LF`) para evitar falhas de execução no Git e em containers Docker.
