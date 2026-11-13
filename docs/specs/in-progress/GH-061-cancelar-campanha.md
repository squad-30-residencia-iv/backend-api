# 📋 Especificação Técnica: GH-061 - Cancelar campanha e invalidar os vouchers não utilizados (RN10, US-05.5)

> Status: In Progress (decisões D-1 a D-16 aprovadas pelo usuário em 2026-11-13, como propostas; pontos em aberto em debate)
> História de Usuário Relacionada: US-01.8 (EP-01), cenários 3 e 5; US-05.5 (EP-05), cenários 2 e 3. Fecha o PA-1 da spec `GH-049` e o PA-13 da `GH-045`. Issue #61.
> Responsável: @iagovrocha
> Data: 2026-11-13
> Branch: `feat/GH-061-cancelar-campanha` → PR para a `staging`
> Depende de: pausar e encerrar (spec `US-01.8-pausar-e-encerrar-campanha.md`), motor de concessão (GH-045), resgate e estorno (GH-049), acompanhamento (GH-050) e a trava de gravação simultânea (GH-047, PA-10)

---

## 1. Contexto & Regras de Negócio

### 1.1 Objetivo

O analista cancela uma campanha com erro de configuração ou irregularidade. A campanha passa a `Cancelled`, deixa de emitir e **os vouchers ainda não utilizados passam a `Cancelled`** (RN10), enquanto os utilizados ficam como estão, com o histórico e a auditoria intactos. Hoje o status `Cancelled` existe no enum, o resgate já recusa voucher de campanha `Cancelled` (GH-049) e o dashboard já tem o balde `cancelledUnused` (GH-050), mas nenhuma operação leva a campanha a esse status.

### 1.2 Regras do backlog

| Fonte | Regra |
|---|---|
| US-01.8, regras | Cancelar finaliza a campanha e **invalida os vouchers ainda não utilizados** (RN10). Destina-se a erro de configuração ou irregularidade. Encerrar e cancelar são operações distintas: não existe cancelamento que preserve vouchers. |
| US-01.8, regras | Campanhas encerradas, canceladas ou esgotadas não geram novos vouchers (RN04). Pausa, encerramento e esgotamento não afetam os vouchers emitidos (RN07a); o cancelamento é a única exceção. |
| US-01.8, cenário 3 | Ativa com 300 emitidos (100 utilizados, 200 não): cancelada → os 200 passam a "cancelado" e os 100 continuam "utilizado". |
| US-01.8, cenário 5 | Encerrar preserva os 50 vouchers "disponível"; cancelar leva os 50 a "cancelado". |
| US-05.5, cenário 2 | Ao processar o status "cancelada", todos os vouchers não utilizados passam a "cancelado" e o resgate deles é recusado. |
| US-05.5, cenário 3 | Os vouchers "utilizado" mantêm o status e o histórico de uso; a auditoria da transação é preservada. |
| US-03.3 | A trilha da campanha mostra quem cancelou e quando, e o cancelamento de vouchers da campanha. |
| US-05.4 (histórico do voucher) | O histórico do voucher registra emissão, uso, expiração ou cancelamento, e o status exibido é coerente com o último evento. |

### 1.3 Fluxo proposto

`POST /api/v1/campaigns/{id}/cancel` com `{ "reason": "..." }`, numa transação:

1. Campanha existente (404), da rede do usuário (403), status efetivo cancelável (D-2, senão 422) e motivo válido (D-4, senão 400).
2. Trava de gravação simultânea (PA-10 da GH-047): o primeiro comando é `UPDATE campaigns ... WHERE updated_at = @lido`, que também bloqueia a linha da campanha contra reservas em andamento (D-9).
3. Campanha → `Cancelled`, com `campaign_status_events` (do status efetivo para `Cancelled`, com o motivo).
4. Provisórios com a reserva em dia → descartados, um a um, pelo mesmo caminho do desfazimento da concessão (D-6).
5. Disponíveis dentro da validade → `Cancelled` em um `UPDATE` só, com `cancelled_at` (D-5, D-7).
6. Um `VoucherEvent` `Cancelled` por voucher cancelado (D-8) e uma entrada `CampaignCancelled` na auditoria, com o motivo e as contagens (D-10).
7. Commit. Resposta `200` com o `CampaignResponse`.

### 1.4 Base reutilizada

- `Campaign.Pause`/`End` e `MoveTo`: transição pelo status efetivo, histórico em `campaign_status_events` (D-2 da spec de pausar e encerrar).
- `CampaignService.SaveGuardedAsync` / `ICampaignRepository.SaveIfUnchangedAsync` (GH-047, PA-10).
- `ConcessionService.CancelAsync`: `TryDiscardVoucherAsync`, `ReleaseReservationAsync`, `MarkConcessionAsync`, `IRedemptionReverser.ReverseAllOfVoucherAsync` (GH-049, D-10).
- `RedemptionEvaluator`: já recusa `Cancelled` e `CampaignCancelled`.
- `DashboardRepository.CountVouchersAsync`: `cancelledUnused` já conta `Cancelled` com `uses_count = 0`; com uso entra em `redeemed` e a soma `issued = redeemed + expiredUnused + cancelledUnused + available` continua valendo.

### 1.5 Decisões e divergências (aprovar antes do código)

| # | Ponto | Proposta |
|---|---|---|
| D-1 | Rota e permissão. | **`POST /api/v1/campaigns/{id}/cancel`**, ao lado de `/pause` e `/end`, com a mesma policy `ManageCampaigns` (analista e gestor, só da rede da campanha). O backlog põe o cancelamento na mão do analista ("Como analista de campanhas"). |
| D-2 | De quais status se cancela. | Pelo **status efetivo**: `Scheduled`, `Active`, `Paused` e `SoldOut`. **Não** cancelam: `Draft` e `PendingApproval` (ainda não valem; o gestor reprova), `Rejected`, `Ended` (a US-01.8 fala em interromper "antes do fim da vigência"; ver PA-2) e `Cancelled`. Fora disso → `422 CAMPAIGN_CANCEL_NOT_ALLOWED` "Somente uma campanha agendada, ativa, pausada ou esgotada pode ser cancelada." |
| D-3 | Status de origem no histórico. | O efetivo no instante da ação, como na pausa (ex.: `SoldOut` ou `Active` para a agendada já iniciada). |
| D-4 | Motivo. | **Obrigatório**, `reason` de 1 a 500 caracteres depois do `trim`. Vai para `campaign_status_events.reason` e para a auditoria. Diferente da pausa (sem motivo), porque o cancelamento é irreversível, invalida direitos do cliente e o backlog o reserva a erro ou irregularidade. Ausente ou em branco → `400` no campo `Reason` "Informe o motivo do cancelamento."; maior → `400` "O motivo deve ter no máximo 500 caracteres." |
| D-5 | O que é "voucher não utilizado". | Todo voucher com o status gravado `Available` **dentro da validade**, com ou sem utilizações (voucher de vários usos já usado em parte perde o que resta). Ficam como estão: `Redeemed` (todas as utilizações consumidas: o "utilizado" do cenário 3), `Expired` e `Available` vencido (o direito já tinha acabado: segue `Expired` na leitura), `Discarded` e `Cancelled`. O voucher cancelado mantém o `uses_count`: no dashboard, o usado em parte continua em `redeemed` e o não usado vai para `cancelledUnused`. |
| D-6 | Voucher provisório (venda ainda não confirmada, reserva em dia). | **Descartado** (`Discarded`), pelo mesmo caminho do desfazimento da concessão: libera a reserva da campanha e do alvo, marca a concessão `Cancelled` e estorna o resgate feito na própria compra (GH-049, D-10), com a causa `CampaignCancelled`. Não vira `Cancelled` porque nunca valeu: entraria em `issued` sem venda confirmada. A confirmação que chegar depois responde `422 CONCESSION_CAMPAIGN_CANCELLED` "A campanha foi cancelada; o voucher desta compra não vale." (D-11). O provisório com a reserva já vencida fica para a liberação de sempre. |
| D-7 | Coluna nova. | `vouchers.cancelled_at` (UTC, nulo), como `confirmed_at` e `discarded_at`. Marca o instante do cancelamento e permite achar, na mesma transação, exatamente os vouchers que o `UPDATE` alcançou, para gravar um evento por voucher sem corrida com um resgate simultâneo (D-12). |
| D-8 | Histórico do voucher. | Um `VoucherEvent` `Cancelled` por voucher cancelado, com o ator (o usuário) e `payload` `{ "cause": "CampaignCancelled" }`. O motivo fica na campanha, não se repete em cada voucher. |
| D-9 | Concessão em andamento durante o cancelamento. | A reserva (`TryReserveAsync`, primeiro comando da transação da concessão) passa a exigir, no próprio `UPDATE`, que o status gravado da campanha seja `Scheduled` ou `Active`. Se o cancelamento gravou antes, a reserva não acontece e a concessão vê `CampaignNotActive`. Se a reserva gravou antes, o cancelamento espera a linha e descarta o provisório no passo 4. Vale também para a corrida já existente com a pausa e o encerramento. |
| D-10 | Auditoria. | **Uma** entrada `CampaignCancelled` na campanha (ator = usuário), com `{ reason, fromStatus, cancelledVouchers, discardedProvisional, reversedRedemptions }`. Sem uma entrada de auditoria por voucher: o lote pode ter dezenas de milhares e o histórico de cada voucher já fica nos eventos (D-8). Os estornos dos provisórios (D-6) seguem gravando o `VoucherRedemptionReversed` de sempre. |
| D-11 | Satélite depois do cancelamento. | `GET /satellite/campaigns` deixa de listar a campanha (já não lista o que não está ativo). Concessão → `CampaignNotActive` (já é assim). Verificação e resgate → `Cancelled` para o voucher cancelado e `CampaignCancelled` para os demais da campanha (já é assim). Confirmação de concessão descartada pelo cancelamento → `422 CONCESSION_CAMPAIGN_CANCELLED`; desfazimento dela → devolve o estado atual (`Cancelled`), idempotente como hoje. A consulta de vouchers do cliente mostra `Cancelled`. |
| D-12 | Corrida com o resgate. | O `UPDATE` em lote exige `status = 'Available'` e a validade em dia; o resgate exige `status = 'Available'` e uso restante. As duas trocam a mesma linha e o banco serializa: se o resgate gravou antes e consumiu a última utilização, o voucher está `Redeemed` e fica fora do lote; se o cancelamento gravou antes, o resgate não encontra a linha e é recusado como `Cancelled`. |
| D-13 | Estorno de resgate depois do cancelamento. | Devolve a utilização, mas o voucher **não volta a `Available`**: se a campanha está `Cancelled`, o `Redeemed` vira `Cancelled` (`TryReturnUseAsync` passa a olhar o status da campanha). Sem isso, o estorno ressuscitaria um voucher de campanha cancelada. |
| D-14 | Resposta. | `200` com o `CampaignResponse` (status `Cancelled`), como `/pause` e `/end`. As contagens ficam na auditoria (D-10) e no dashboard (`cancelledUnused`). |
| D-15 | Duplicação. | Sem mudança: a cancelada pode ser duplicada (US-01.10 aceita qualquer status). É o caminho para corrigir o erro de configuração e lançar a campanha de novo. |
| D-16 | Volume. | Tudo síncrono numa transação: o lote é um `UPDATE` só, os eventos são inseridos em blocos de 1.000 e os provisórios são poucos (só os da janela de reserva, 15 minutos por padrão). Sem job em segundo plano nesta fatia (PA-3). |

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
// Application/Campaigns/CancelCampaignRequest.cs
public record CancelCampaignRequest(
    [property: Required(ErrorMessage = CampaignContract.CancellationReasonRequired)]
    [property: StringLength(500, ErrorMessage = CampaignContract.CancellationReasonTooLong)]
    string? Reason);

// Resposta: CampaignResponse, sem mudança de forma (status "Cancelled").
```

```csharp
// Domain/Campaigns/Campaign.cs
/// <exception cref="BusinessRuleException">Status efetivo fora de Scheduled, Active, Paused ou SoldOut.</exception>
public CampaignStatusEvent Cancel(Guid actorUserId, string reason, DateTime utcNow);

// Domain/Vouchers/Voucher.cs
public DateTime? CancelledAt { get; set; }

// Application/Vouchers/IConcessionRepository.cs
public enum ReserveOutcome { Reserved, CampaignNotActive, CampaignSoldOut, TargetSoldOut }

// Application/Campaigns/ICampaignCancellationRepository.cs (ou no ICampaignRepository)
Task<IReadOnlyList<ProvisionalVoucher>> GetLiveProvisionalAsync(Guid campaignId, DateTime utcNow, CancellationToken cancellationToken);
Task<int> CancelAvailableVouchersAsync(Guid campaignId, DateTime utcNow, CancellationToken cancellationToken);
Task<IReadOnlyList<Guid>> GetVouchersCancelledAtAsync(Guid campaignId, DateTime cancelledAt, CancellationToken cancellationToken);
```

### 2.1 Códigos e mensagens novos

| Código / campo | HTTP | Mensagem |
|---|---|---|
| `Reason` | 400 | "Informe o motivo do cancelamento." |
| `Reason` | 400 | "O motivo deve ter no máximo 500 caracteres." |
| `CAMPAIGN_CANCEL_NOT_ALLOWED` | 422 | "Somente uma campanha agendada, ativa, pausada ou esgotada pode ser cancelada." |
| `CONCESSION_CAMPAIGN_CANCELLED` | 422 | "A campanha foi cancelada; o voucher desta compra não vale." |
| `CAMPAIGN_CONCURRENT_UPDATE` | 422 | (já existe, GH-047 PA-10) |

Auditoria nova: `AuditEventTypes.CampaignCancelled`. Evento de voucher: `VoucherEventTypes.Cancelled` (já existe), com a causa `CampaignCancelled`.

---

## 3. Especificação dos Endpoints

### `POST /api/v1/campaigns/{id}/cancel` (nova)

- Autorização: `ManageCampaigns` (analista e gestor); administrador → `403`; outra rede → `403`; sem token → `401`.
- `200`: `CampaignResponse` com `status: "Cancelled"`.
- `400`: motivo ausente, em branco ou longo demais.
- `404`: campanha inexistente.
- `422`: `CAMPAIGN_CANCEL_NOT_ALLOWED` (D-2) ou `CAMPAIGN_CONCURRENT_UPDATE` (outra gravação da mesma campanha ao mesmo tempo).

### `POST /api/v1/satellite/concessions/{id}/confirm` (alterada)

- Concessão descartada pelo cancelamento da campanha → `422 CONCESSION_CAMPAIGN_CANCELLED` (D-6, D-11).

### `POST /api/v1/satellite/concessions` (alterada por dentro)

- Reserva condicionada ao status gravado `Scheduled` ou `Active` (D-9). Sem mudança de contrato.

### `POST /api/v1/satellite/redemptions/{id}/reverse` (alterada por dentro)

- Voucher de campanha cancelada não volta a `Available` (D-13). Sem mudança de contrato.

### Migração

`CampaignCancellation` nos dois conjuntos (`sh scripts/add-migration.sh CampaignCancellation`): `vouchers.cancelled_at` (timestamp, nulo).

---

## 4. Checklist de Implementação

- [ ] 1. Domínio: `Campaign.Cancel` (D-2, D-3, D-4), `Voucher.CancelledAt`, `CampaignErrors` e `ConcessionErrors` novos, `AuditEventTypes.CampaignCancelled`.
- [ ] 2. Infraestrutura: coluna e migração (D-7); repositório do lote (D-5, D-7, D-12); `TryReserveAsync` com o status gravado (D-9); `TryReturnUseAsync` olhando a campanha (D-13).
- [ ] 3. Application: `CampaignService.CancelAsync` (passos 1 a 7 da 1.3), reaproveitando o descarte do provisório do `ConcessionService` num serviço comum (D-6); confirmação com `CONCESSION_CAMPAIGN_CANCELLED` (D-11).
- [ ] 4. API: `POST /campaigns/{id}/cancel` no `CampaignsController`.
- [ ] 5. Testes unitários e de integração (seção 5); smoke `scripts/smoke/cancelamento.py`.
- [ ] 6. Documentação: README, `docs/ROADMAP.md`, `docs/PROJECT_STATE.md`, PA-1 da GH-049 e PA-13 da GH-045 marcados como resolvidos.
- [ ] 7. `dotnet build -warnaserror`, `dotnet test` em SQLite e PostgreSQL, smoke com a API no ar, agente `testador-de-branch` (Opus).

---

## 5. Casos de Teste Essenciais

- [ ] US-01.8 cenário 3: 3 vouchers usados até o fim, 2 não usados → cancelada; os 2 viram `Cancelled`, os 3 seguem `Redeemed`; dashboard `cancelledUnused = 2`, `redeemed = 3`, e a soma confere.
- [ ] US-01.8 cenário 5: uma encerrada e uma cancelada, cada uma com vouchers disponíveis → os da encerrada seguem `Available`, os da cancelada viram `Cancelled`.
- [ ] US-05.5 cenário 2: depois do cancelamento, verificação e resgate do voucher recusam com `Cancelled`.
- [ ] US-05.5 cenário 3: resgate anterior intacto (`redemptions`, eventos e auditoria), e o voucher `Redeemed` não ganha evento novo.
- [ ] Voucher de vários usos usado em parte → `Cancelled`, com `uses_count` mantido; dashboard o conta em `redeemed` (D-5).
- [ ] `Available` vencido fica de fora e segue `Expired` na leitura (D-5).
- [ ] Provisório em dia → `Discarded`, reserva devolvida (`reservedVouchers` da campanha e do alvo), concessão `Cancelled`; resgate na própria compra estornado; confirmação depois → `422 CONCESSION_CAMPAIGN_CANCELLED`; desfazimento depois → `200` com o estado atual (D-6, D-11).
- [ ] Cancelável a partir de agendada, ativa, agendada já iniciada (histórico de `Active`), pausada e esgotada (histórico de `SoldOut`) (D-2, D-3).
- [ ] `422 CAMPAIGN_CANCEL_NOT_ALLOWED` para rascunho, em aprovação, reprovada, encerrada (inclusive pausada depois do fim) e cancelada.
- [ ] Motivo: ausente, em branco e com 501 caracteres → `400`; com espaços nas pontas → gravado sem eles; vai para o histórico e para a auditoria (D-4).
- [ ] Um `VoucherEvent` `Cancelled` por voucher cancelado, com o ator; uma entrada `CampaignCancelled` com as contagens; nenhuma auditoria por voucher (D-8, D-10).
- [ ] Depois do cancelamento: a concessão não emite (`CampaignNotActive`) e `GET /satellite/campaigns` não lista a campanha.
- [ ] Concessões simultâneas ao cancelamento: nenhum provisório sobra em campanha cancelada e nenhum voucher novo fica `Available` (D-9).
- [ ] Resgates simultâneos ao cancelamento: cada voucher termina `Redeemed` com o resgate gravado ou `Cancelled` sem ele, com exatamente um evento `Cancelled` para os cancelados (D-12).
- [ ] Estorno, depois do cancelamento, de um resgate que tinha consumido a última utilização → utilização devolvida e voucher `Cancelled` (D-13).
- [ ] Dois cancelamentos simultâneos → um `200` e um `422` (`CAMPAIGN_CONCURRENT_UPDATE` ou `CAMPAIGN_CANCEL_NOT_ALLOWED`), um único evento por voucher.
- [ ] Autorização: administrador `403`, outra rede `403`, sem token `401`, campanha inexistente `404`.
- [ ] A cancelada pode ser duplicada (D-15).

---

## 6. Pontos em aberto

| # | Ponto | Motivo de ficar de fora | Gatilho para retomar |
|---|---|---|---|
| PA-1 | Retomar a campanha pausada (US-01.8, cenário 4). | A spec de pausar e encerrar combinou com o portal (frontend-web#43) que não há retomada; não é parte do cancelamento. | PO ou frontend pedirem a retomada. |
| PA-2 | Cancelar a campanha já encerrada (antecipadamente ou pelo fim da vigência) cujos vouchers ainda valem. | A US-01.8 trata a interrupção "antes do fim da vigência", e o encerramento é irreversível. Uma irregularidade descoberta depois não tem como invalidar os vouchers. | PO pedir a invalidação de vouchers de campanha encerrada. |
| PA-3 | Cancelamento em segundo plano para campanhas muito grandes. | O `UPDATE` em lote é um comando só e os eventos vão em blocos; o volume do projeto cabe numa requisição. | Cancelamento passar de alguns segundos em produção ou campanhas com centenas de milhares de vouchers. |
| PA-4 | Avisar o cliente ou o satélite do cancelamento (notificação ativa). | O backend não guarda contato do cliente (D4) e o satélite consulta os vouchers na hora da compra. | Pedido de notificação, ou o satélite querer ser avisado (webhook). |
| PA-5 | Restringir o cancelamento ao gestor. | O backlog dá a ação ao analista, como pausar e encerrar. | Governança pedir aprovação para cancelar. |

---

## 7. Notas da implementação

(Preencher na implementação: diferenças em relação ao texto acima e a verificação.)
