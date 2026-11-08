# 📋 Especificação Técnica: GH-023 - Reconciliar o modelo de condições e restaurar o build da `main`

> Status: Completed
> História de Usuário Relacionada: issue #23 (modelo do motor), complementa o PR #36 e o PR #30 (EP-01)
> Responsável: @iagovrocha
> Data: 2026-11-07

---

## 1. Contexto & Regras de Negócio

### 1.1 O que aconteceu

Em 2026-11-05, dois PRs que criam uma entidade `CampaignCondition` foram mesclados na `main` com 12 minutos de diferença:

| Ordem | PR | Commit | Modelo de condição | CI |
|---|---|---|---|---|
| 1 | #36 "Feat/gh 23 base motor" (issue #23) | `dc10217` | `Domain.Entities.CampaignCondition`: `ConditionType` (`MinPurchaseValue`, `SpecificProduct`, `SpecificCategory`, `DateRange`), `Value` em texto e `ConditionOperator` (`Equals`, `GreaterThan`, `Contains`). Tabela `CampaignConditions`, sem FK. | ✅ |
| 2 | #30 "Configuração da campanha (EP-01)", squash | `e3faf89` | `Domain.Campaigns.CampaignCondition`, da US-01.4: `Type`, `MinAmount`, `MinQuantity`, `CategoryId` e `ProductId` tipados. Tabela `campaign_conditions`, com FKs para `campaigns`, `categories` e `products` e `CHECK` de coerência por tipo. Usada pelo `PUT /api/v1/campaigns/{id}/conditions`. | ❌ |

O Git juntou os dois `AppDbContext` sem conflito textual, mas o resultado não compila:

```
AppDbContext.cs(24,18): error CS0104: 'CampaignCondition' is an ambiguous reference between
  'VoucherCampaigns.Domain.Campaigns.CampaignCondition' and 'VoucherCampaigns.Domain.Entities.CampaignCondition'
AppDbContext.cs(27,18): error CS0104: (idem)
AppDbContext.cs(27,37): error CS0102: The type 'AppDbContext' already contains a definition for 'CampaignConditions'
```

Desde então, a `main` não compila, o CI dela falha e nenhuma branch nova parte de uma base verde.

O PR #39 (issue #37, autenticação do portal, ainda aberto) traz um paliativo no `AppDbContext`: um alias `using CampaignCondition = ...Domain.Campaigns.CampaignCondition`, além de renomear o `DbSet` do #36 para `MotorConditions` e fixar a tabela `CampaignConditions`. O build volta a passar, mas os **dois modelos concorrentes continuam** no domínio e no banco.

### 1.2 Objetivo

1. Restaurar o build e a suíte de testes da `main`.
2. Deixar **um único** modelo de condição de geração, para que o motor (issue #27, US-04.1) saiba qual tabela ler.
3. Não alterar nenhum contrato HTTP nem comportamento existente.

### 1.3 Decisões

| # | Decisão | Motivo |
|---|---|---|
| D-1 | **Fica o modelo tipado do EP-01** (`Domain.Campaigns.CampaignCondition`, tabela `campaign_conditions`). Saem do domínio `Domain.Entities.CampaignCondition`, `Domain.Enums.ConditionType` e `Domain.Enums.ConditionOperator`. | É o único que tem rota, regras (US-01.4, cenários 1 a 4), integridade referencial e testes (36 de integração). O do #36 não tem FK, não é usado por nenhum código e só existe no `AppDbContext` e nas migrações. Os 4 tipos do #36 têm equivalente no EP-01: `MinPurchaseValue` → `PurchaseTotal`, `SpecificProduct` → `Product`, `SpecificCategory` → `Category`, `DateRange` → `PurchasePeriod` (já previsto, responde `422 CAMPAIGN_CONDITION_TYPE_UNAVAILABLE` até ter semântica). Nada se perde. |
| D-2 | Nova migração **`RemoveMotorCampaignConditions`** nos dois conjuntos (`sh scripts/add-migration.sh RemoveMotorCampaignConditions`), que apaga a tabela `CampaignConditions`. O snapshot passa a refletir só `campaign_conditions`. O id da migração fica **entre** a do #36 (27/10) e a `CampaignVoucherRules` (29/10): `20261028120000`. Ver as notas da implementação. | A tabela do #36 já foi criada pela `AddVoucherMotorEntities` (SQLite) e pela `AddMotorBaseEntities` (Postgres) nos bancos de quem rodou a `main`. Apagar a migração antiga reescreveria o histórico e quebraria esses bancos. A tabela está vazia, porque nenhuma rota grava nela, então o `DropTable` não perde dados. |
| D-3 | As demais entidades do #36 (`Voucher`, `VoucherEvent`, `AuditEntry` e `EligibilityCriterion`) **não mudam** nesta spec. | Cada uma tem um card dono que vai redesenhá-la: `AuditEntry` → issue #26 (auditoria transversal); `Voucher`/`VoucherEvent` → issue #27 (motor); `EligibilityCriterion` → EP-06. Ajustá-las aqui ampliaria o conflito com o trabalho dos colegas. As lacunas encontradas estão no PA-1. |
| D-4 | A spec do #36 vai de `docs/specs/completed/US-01.12-base-motor-e-decisoes.md` para **`docs/specs/completed/GH-023-base-motor-e-decisoes.md`**, sem mudar o conteúdo, com uma nota no topo apontando esta spec e a substituição do modelo de condição. | O id US-01.12 já pertence à spec de validade e restrições do voucher (`US-01.12-configurar-validade-e-restricoes-do-voucher.md`). Dois documentos com o mesmo id confundem a rastreabilidade. O card de origem é a issue #23. |
| D-6 | **D3 (concessão provisória, P12):** o default adotado é o das **duas fases do backlog**. O voucher nasce provisório e reserva o saldo; a confirmação da venda o torna disponível; o desfazimento ou a expiração da reserva o descarta e devolve o saldo (PR-06, PR-17, RF14c, US-04.9). Registrado em `docs/ROADMAP.md` 2.5 e na nota da spec do #36. | O default do #36 ("síncrono, sem status pendente") respondia a outra pergunta. Os EP-04, 05 e 07 foram escritos sobre as duas fases. Decidido pelo responsável da issue #27 em 2026-11-07. Falta o aceite do representante. |
| D-5 | **Ordem com o PR #39:** este conserto entra primeiro, direto na `main`. O #39 então descarta o hunk do `AppDbContext`, porque o alias e o `MotorConditions` deixam de ser necessários, e o conflito é trivial. Se o #39 entrar antes, este PR remove o alias e o `MotorConditions` no lugar dos `DbSet` duplicados. O estado final é o mesmo. | O #39 trata de autenticação e não deveria carregar a decisão de modelo. O conserto aqui é o definitivo. É preciso combinar com o @joaopauldsn. |

### 1.4 Tratamento de exceções

Não se aplica: nenhuma regra nova e nenhum código de erro novo.

---

## 2. Contratos & Schemas (DTOs / Records)

Nenhum contrato muda. Os records da US-01.4 continuam como estão (`ReplaceCampaignConditionsRequest`, `CampaignConditionRequest`, `CampaignConditionResponse`).

Estado final do `AppDbContext`:

```csharp
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Entities;
using VoucherCampaigns.Domain.Organization;

public DbSet<Voucher> Vouchers => Set<Voucher>();
public DbSet<VoucherEvent> VoucherEvents => Set<VoucherEvent>();
public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
public DbSet<CampaignCondition> CampaignConditions => Set<CampaignCondition>();   // Domain.Campaigns
public DbSet<EligibilityCriterion> EligibilityCriteria => Set<EligibilityCriterion>();
// sem modelBuilder.Entity<...Entities.CampaignCondition>
```

---

## 3. Especificação do Endpoint / Serviço

Nenhuma rota nova ou alterada. As rotas existentes que tocam a tabela continuam iguais:
- `PUT /api/v1/campaigns/{id}/conditions`;
- `GET /api/v1/campaigns/{id}`, com o campo `conditions` na resposta.

---

## 4. Checklist de Implementação

- [x] 1. Branch `fix/GH-023-reconciliar-condicoes-main` a partir da `main` (`e3faf89`).
- [x] 2. Apagar `Domain/Entities/CampaignCondition.cs`, `Domain/Enums/ConditionType.cs` e `Domain/Enums/ConditionOperator.cs`.
- [x] 3. `AppDbContext`: um único `DbSet<CampaignCondition>`, sem o `modelBuilder.Entity` do modelo removido.
- [x] 4. `sh scripts/add-migration.sh RemoveMotorCampaignConditions` (rodar pelo Bash). Conferir que o `Up` só apaga `CampaignConditions` e que o `Down` recria a tabela.
- [x] 5. Renomear a spec do #36 (D-4) e incluir a nota no topo.
- [x] 6. `docs/PROJECT_STATE.md`: entrada do conserto e tarefas do PA.
- [x] 7. `dotnet build -warnaserror` e `dotnet test`, unitários e integração.
- [x] 8. Smoke test: API rodando sobre um SQLite novo, migrações aplicadas do zero, `GET /health` e o fluxo `PUT /conditions` → `GET /campaigns/{id}`.
- [x] 9. Smoke test de upgrade: banco SQLite com as migrações até `CampaignVoucherRules`, a `main` quebrada já aplicada, e então a nova migração. A tabela `CampaignConditions` some e `campaign_conditions` mantém os dados.
- [x] 10. PR `[FIX] [GH 023] Reconcilia o modelo de condições e restaura o build da main` para a `main`. Avisar o @RiveraaVictor (#36) e o @joaopauldsn (#39).

---

## 5. Casos de Teste Essenciais

- [x] Build: 0 erros e 0 warnings com `-warnaserror`.
- [x] `MigrationsModelTests` verde: o modelo não tem mudanças pendentes nos dois providers.
- [x] Suíte existente verde, sem regressão: na branch do épico eram 156 unitários e 291 de integração. O #36 não trouxe testes, então o total esperado é o mesmo.
- [x] Os testes da US-01.4 (`CampaignConditionsEndpointTests`) continuam passando, o que prova que o modelo mantido é o ativo.
- [x] Upgrade de banco (checklist 9): a migração roda sobre um banco que já tem a tabela do #36.

Nenhum teste novo de unidade: a mudança é de modelo e de migração, já coberta pelo `MigrationsModelTests` e pela suíte de rotas.

---

## 6. Pontos em aberto

| # | Ponto | Por que fica para depois | Quando retomar |
|---|---|---|---|
| PA-1 | **Conformidade das entidades do #36 com o padrão do projeto:** tabelas em PascalCase (`Vouchers`, `VoucherEvents`, `AuditEntries`, `EligibilityCriteria`) em vez de `snake_case`; `Voucher` e `EligibilityCriterion` sem FK para `campaigns`; `Guid.NewGuid()` em vez de `Guid.CreateVersion7`; `DateTime.UtcNow` em vez de `TimeProvider`; `HasColumnType("jsonb")` no modelo compartilhado com o SQLite; entidades em `Domain/Entities` e `Domain/Enums` em vez do agregado (`Domain/Campaigns`, ...). | Cada entidade tem dono e será redesenhada no card dele (D-3). Mexer agora geraria conflito com o trabalho em curso. As tabelas estão vazias, então corrigir depois continua barato. | `AuditEntry` → spec de auditoria da issue #26. `Voucher`/`VoucherEvent` → spec do motor da issue #27, que precisa de campos novos de qualquer forma (PA-2). `EligibilityCriterion` → EP-06. |
| PA-2 | **`Voucher` não cobre o EP-04/05:** faltam cliente, loja/canal de emissão, benefício aplicado, chave de idempotência (US-04.6), data de emissão e status provisório (RF14c/US-04.9). `UsageLimit` duplica o `campaigns.max_uses_per_voucher` (US-01.12). | É escopo do motor, não do conserto. | Spec da issue #27. |
| PA-3 | ~~**Decisão D3 do #36 contradiz o backlog:**~~ **Resolvido pela D-6** (default das duas fases); falta só o aceite do representante. a spec do #36 diz que "não haverá status pendente no MVP", mas o EP-04 (PR-06, RF14c, US-04.9) exige que todo voucher nasça **provisório** e só fique disponível com a confirmação da venda. Isso define se o `VoucherStatus` ganha `Provisional`. | É decisão de negócio do motor. | Antes da spec da issue #27: confirmar com o PO e o cliente, ou registrar o recorte do MVP na spec. |
| PA-4 | **D1, D2, D4 e D9 são *defaults* do #36 sem aceite do cliente.** A D9 (fim sem hora vale até 23:59:59) não afeta o EP-01, porque as datas da campanha e da validade sempre chegam com hora (`[LocalDateTime]`). | Não bloqueia o conserto. | Junto com a PA-3, antes da spec da #27. |
| PA-5 | **Validação em PostgreSQL 17 real** das migrações intercaladas (#36 de 27/10 e EP-01 de 26/10 e 29/10). | Sem Docker na máquina do responsável. Só o `MigrationsModelTests` cobre o Postgres. | Quem tiver Docker: `docker compose up -d` + `TEST_POSTGRES_CONNECTION` (PR #34). Pedir no PR. |

---

## 7. Notas da implementação

- **Colisão do nome da PK no PostgreSQL:** a tabela do #36 tem PK `pk_campaign_conditions`, o mesmo nome da PK da `campaign_conditions` (US-01.4). No PostgreSQL, a PK cria um índice com esse nome, e nomes de índice são únicos por schema. Num banco novo, a `CampaignVoucherRules` (29/10) falharia com `42P07 relation already exists` depois da `AddMotorBaseEntities` (27/10). O SQLite não exige nomes únicos de constraint, por isso nem o CI nem a suíte local pegaram o problema. A solução é dar à migração nova o id `20261028120000`, entre as duas. Num banco novo, a tabela do #36 some antes de a nossa ser criada. Num banco existente, o EF aplica a migração mais antiga que estiver pendente. O SQL não foi rodado num PostgreSQL real (PA-5).
- **A migração do #36 no SQLite tem o nome trocado:** `AddMotorBaseEntities` (SQLite) está vazia e as tabelas estão na `AddVoucherMotorEntities`; no Postgres é o contrário. Não muda nada, porque as duas vêm antes de 28/10.
- **Smoke test:**
  - **Banco novo** (SQLite): as 9 migrações foram aplicadas em ordem, e a `RemoveMotorCampaignConditions` rodou antes da `CampaignVoucherRules`. As rotas responderam como esperado: `PUT /conditions` → `200`, quantidade 0 → `422 CAMPAIGN_CONDITION_MIN_QUANTITY_NOT_POSITIVE` e `GET` com as condições.
  - **Upgrade:** num banco no estado da `main` antiga (com a tabela `CampaignConditions` e sem a migração nova no histórico), só a migração pendente foi aplicada, a tabela do #36 sumiu e as 2 linhas de `campaign_conditions` ficaram.
- **Teste intermitente:** numa das ~50 execuções da suíte de integração, `CampaignVoucherRulesEndpointTests.PutValidity_WithInvalidContract_ReturnsValidationProblem(json: "{}")` recebeu `500` no `POST /campaigns` do *setup*. Não se repetiu em 48 rodadas seguidas e não tem relação com esta mudança, que não toca o cadastro. A causa não foi encontrada: o log não foi capturado na rodada que falhou. Registrado no `PROJECT_STATE.md` para investigar.
