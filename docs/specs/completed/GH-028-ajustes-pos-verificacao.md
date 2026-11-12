# 📋 Especificação Técnica: GH-028 - Ajustes após a verificação da `staging`

> Status: Completed
> História de Usuário Relacionada: issue #28 e issue #48 (GH-028), issue #45 (motor de concessão)
> Responsável: @iagovrocha
> Data: 2026-11-11

---

## 1. Contexto & Regras de Negócio

### 1.1 O que aconteceu

Em 2026-11-11 a `staging` recebeu o PR #53 (motor de concessão), o PR #52 (cadastros no portal) e a GH-028 (aplicações satélite, rate limit e triggers append-only, mesclada direto na `main` pelo PR #54 e trazida de volta pelo PR #55). O agente de testes (`testador-de-branch`) verificou a `staging` em `a4808e3`: build sem avisos, 685 testes verdes, roteiros `smoke.py` e `motor.py` OK e todas as rotas da GH-028 funcionando. Ficaram quatro pontos:

| # | Achado | Origem |
|---|---|---|
| 1 | As rotas `/api/v1/satellite/apps/{id}` respondem `404` com `code: "NOT_FOUND"`; a spec da GH-028 prometia `SATELLITE_APP_NOT_FOUND`, e a classe de testes não tinha nenhum caso de `404`, apesar do checklist marcado. | GH-028 |
| 2 | A migração `AppendOnlyTableTriggers` protege `audit_entries` e `campaign_status_events`, mas `voucher_events` (issue #45) também é `IAppendOnly` e ficou só com a trava do `AppDbContext`, que `ExecuteUpdate`/`ExecuteDelete` e scripts manuais atravessam. | GH-028 + #45 |
| 3 | `createdAt` da aplicação satélite sai com `Z` no `POST` e sem fuso no `GET` (o SQLite devolve `DateTime` com `Kind` indefinido). | GH-028 |
| 4 | O segredo gerado tem 52 caracteres (`sec_` + 24 bytes em hexadecimal); a spec dizia 40. | GH-028 (texto) |

Além disso, o ROADMAP e o `PROJECT_STATE.md` ainda descreviam a issue #45 como "em PR", e o Foco ficou com dois parágrafos concatenados pelo merge.

### 1.2 Objetivo

Corrigir os quatro achados sem alterar nenhum contrato HTTP já usado pelo front ou pelos satélites, e alinhar os documentos ao estado real da `staging`.

### 1.3 Decisões

| # | Decisão | Motivo |
|---|---|---|
| D-1 | **O `404` das aplicações satélite continua `NOT_FOUND`; a spec da GH-028 é que muda.** | Todo recurso inexistente da API responde `404 NOT_FOUND` (`ResourceNotFoundException` → `ApiExceptionHandler`), inclusive campanha, loja e catálogo. Um código próprio para um único recurso quebraria a convenção que o front já trata. A mensagem "Aplicação satélite não encontrada." é mantida. Os cinco casos de `404` ganham teste. |
| D-2 | **Trigger em `voucher_events` numa migração nova, `VoucherEventsAppendOnlyTrigger`, nos dois bancos.** | Não se edita migração já aplicada. No PostgreSQL reaproveita a função `prevent_append_only_modification()` da GH-028; no SQLite, duas triggers (`no_update`, `no_delete`) no mesmo padrão. Sem mudança de modelo (o snapshot não muda). |
| D-3 | **`createdAt` sempre em UTC na resposta** (`DateTime.SpecifyKind(..., Utc)` no mapeamento), como o `CampaignService` já faz. | O mesmo campo tinha dois formatos conforme a rota. |
| D-4 | **O segredo fica como está (52 caracteres); a spec passa a descrever o tamanho real.** | O tamanho não é contrato: o satélite guarda o que recebe. Mudar o gerador só para bater com um número do texto não traz ganho. |
| D-5 | Esta spec nasce já `Completed`, junto com o código, porque é um ajuste pós-verificação sem decisão de produto. | O PR é o ponto de aprovação. |

### 1.4 Tratamento de exceções

Sem exceções novas. `ResourceNotFoundException` continua sendo convertida em `404 NOT_FOUND` pelo `ApiExceptionHandler`.

---

## 2. Contratos & Schemas (DTOs / Records)

Nenhum DTO muda. `SatelliteAppItemResponse.CreatedAt` passa a chegar com `Kind = Utc` (serializa com `Z`) em todas as rotas.

---

## 3. Especificação do Endpoint / Serviço

| Rota | Mudança |
|---|---|
| `GET`, `PUT`, `DELETE`, `PATCH /deactivate`, `POST /rotate-secret` em `/api/v1/satellite/apps/{id}` | Sem mudança de comportamento: `404 NOT_FOUND` com `detail` "Aplicação satélite não encontrada.", agora com teste. |
| `GET /api/v1/satellite/apps`, `GET /{id}` | `createdAt` com `Z`. |
| Banco | Migração `VoucherEventsAppendOnlyTrigger`: `UPDATE` e `DELETE` em `voucher_events` abortam. |

---

## 4. Checklist de Implementação

- [x] Migração `VoucherEventsAppendOnlyTrigger` (SQLite e PostgreSQL) via `scripts/add-migration.sh`, com o SQL das triggers e o `Down`.
- [x] `SatelliteAppManagementService.ToResponse`: `CreatedAt` em UTC.
- [x] Spec `GH-028`: `404 NOT_FOUND`, tamanho real do segredo, nota sobre `voucher_events`.
- [x] Testes: `404` nas cinco rotas por id; `createdAt` com `Kind = Utc` no `GET`; `voucher_events` bloqueada por trigger (`AppendOnlyTableTriggersTests`).
- [x] `docs/ROADMAP.md` e `docs/PROJECT_STATE.md`: #45 e GH-028 mescladas; Foco reescrito.

---

## 5. Casos de Teste Essenciais

- [x] `GET`, `PUT`, `DELETE`, `PATCH /deactivate` e `POST /rotate-secret` com id inexistente → `404`, `code: "NOT_FOUND"`, `detail` "Aplicação satélite não encontrada.".
- [x] `GET /satellite/apps/{id}` devolve `createdAt` igual ao do `POST` e com `Kind = Utc`.
- [x] Um voucher concedido pela API grava `voucher_events`; `ExecuteUpdateAsync` e `ExecuteDeleteAsync` nessa tabela lançam exceção e a linha segue intacta.
- [x] `MigrationsModelTests` continua verde (migração sem mudança de modelo).

---

## 6. Pontos em aberto

| # | Ponto | Motivo de adiar | Gatilho |
|---|---|---|---|
| PA-1 | Auditoria (`audit_entries`) ao criar, alterar, inativar e rotacionar aplicações satélite. | A spec da GH-028 não pede, e o evento precisaria de um `EventType` novo combinado com a trilha de auditoria. | Pedido do PO ou a tela de aplicações no portal. |
| PA-2 | Validar que a rede da aplicação existe (`networks`). | A GH-028 decidiu sem FK, como em `users`. | Cadastro pelo portal com lista de redes. |
| PA-3 | O bloqueio de 1 minuto no `POST /satellite/token` é por `clientId`: quem conhece o `clientId` de um PDV pode travar as emissões dele. | Comportamento especificado na GH-028. | Incidente ou revisão de segurança (limitar também por IP). |
| PA-4 | PostgreSQL: as triggers (GH-028 e esta) só foram executadas em SQLite nesta máquina. | Sem Docker local. | Alguém rodar a suíte com `TEST_POSTGRES_CONNECTION`. |

---

## 7. Notas da implementação

- A migração foi gerada vazia pelo script (o modelo não muda) e preenchida à mão com o SQL, no mesmo padrão da `AppendOnlyTableTriggers`.
- O teste de `voucher_events` usa o `MotorScenario` para obter um voucher real pela API, em vez de montar `Concession`/`Voucher` à mão.
