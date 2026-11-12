# 📋 Especificação Técnica: GH-049 - Verificação de aplicáveis e resgate de vouchers (EP-05, US-07.1, US-07.3)

> Status: In Progress
> História de Usuário Relacionada: issue #49 (fatia da #27), US-05.1 a US-05.6, US-07.1, US-07.3, US-04.9 (cenários 4 e 5)
> Responsável: @iagovrocha
> Data: 2026-11-11

---

## 1. Contexto & Regras de Negócio

### 1.1 Objetivo

Fechar o ciclo do voucher que a issue #45 emite: com o cliente identificado e o carrinho fechado, o satélite pergunta **quais vouchers daquele cliente servem nesta compra** (US-05.1, US-07.3 cenário 1), mostra ao cliente, e **resgata o escolhido** (US-07.3 cenário 2) com as mesmas regras do motor: validade própria (US-05.3), restrições de uso da campanha (US-05.2), limite de utilizações com concorrência (US-05.4), efeito do status da campanha (US-05.5) e benefício do alvo (US-05.6). Também entrega a consulta de **campanhas vigentes** por API (US-07.1), que a issue #49 cita e nenhuma fatia anterior cobriu.

O resgate é automático do ponto de vista do cliente (RF17): ele não digita código. O código do voucher continua identificador interno; o satélite trabalha com o `id` que a verificação devolve.

Fora desta spec: público restrito (#47), relatórios e CSV (#50), o cancelamento da campanha com invalidação em lote (RN10, US-05.5 cenários 2 e 3; não existe operação de cancelar, PA-1) e o estorno de um resgate (PA-6).

### 1.2 Fluxo da verificação e do resgate (ordem de avaliação)

A verificação (`POST /satellite/vouchers/check`) e o resgate (`POST /satellite/redemptions`) recebem a **mesma compra** da concessão (D-3) e avaliam cada voucher do cliente nesta ordem. A verificação devolve o resultado de todos; o resgate aplica ao voucher escolhido e, se passar, consome a utilização.

1. Contrato (`400`): transação, cliente, loja, canal, valor total e itens (e `voucherId`, no resgate).
2. Loja da rede da aplicação, ativa e que opera o canal (`422`, D-4). Nada é gravado.
3. Vouchers do cliente na rede, exceto `Discarded`, com campanha, alvo e concessão (D-5). No resgate, só o `voucherId` informado; de outro CPF ou de outra rede → `404` (D-12).
4. Para cada voucher, o **primeiro motivo** que se aplica (D-6):
   1. `Provisional` da **mesma transação** e campanha com `allowUseInSamePurchase` → entra como aplicável, com `requiresConfirmation: true` (US-04.9 cenário 4); `Provisional` de outra transação, ou da mesma sem a permissão → `NotConfirmed` (US-04.9 cenário 5).
   2. Status efetivo `Expired` → `Expired` (US-05.3, D9 inclusivo); `Redeemed` → `NoUsesLeft`; `Cancelled` → `Cancelled`.
   3. Campanha com status gravado `Cancelled` → `CampaignCancelled` (RN10). `Paused`, `Ended` e `SoldOut` **não recusam** (RN07a, US-05.5 cenário 1).
   4. Escopo: a campanha não contempla a loja e o canal da compra → `OutOfScope` (RN08, US-05.2 cenário 4).
   5. Valor bruto da compra (PR-16) abaixo de `minPurchaseAmount` → `AmountBelowMinimum`; acima de `maxPurchaseAmount` → `AmountAboveMaximum` (US-05.2 cenário 2).
   6. Nenhum item da compra casa o alvo do voucher (alvo `All` sempre casa) → `TargetNotInPurchase` (US-05.2 cenário 3, US-05.1 cenário 5).
   7. Já existe resgate nesta transação: o voucher resgatado → `AlreadyRedeemedInThisPurchase`; os demais → `TransactionAlreadyRedeemed` (US-07.3 cenário 4).
5. Aplicáveis recebem o **desconto previsto** (D-7) e o de maior desconto é o **recomendado** (RF17b, D-8). A verificação **não grava nada** (US-05.1 cenário 3, US-07.3 cenário 1).
6. Resgate do aplicável: `UPDATE vouchers ... WHERE id = @id AND status = 'Available' AND uses_count < max_uses` atômico (D-9); zero linhas → relê o voucher e devolve o motivo atual (US-05.4 cenário 4, US-07.3 cenário 6). Em seguida grava `redemptions`, o evento `Redeemed` e a auditoria `VoucherRedeemed` no mesmo `SaveChanges`. Quando `uses_count` chega a `max_uses`, o status passa a `Redeemed` no mesmo `UPDATE` (US-05.4 cenários 1 e 2).
7. Idempotência (US-07.3 cenário 5): `redemptions` tem índice único por rede + `transactionId`. Antes do passo 6, resgate já existente nesta transação com o mesmo voucher → `200` com o resgate original; com outro voucher → `422 REDEMPTION_TRANSACTION_ALREADY_REDEEMED`. Se duas chamadas simultâneas passarem pela leitura, a violação do índice desfaz a transação do banco e a chamada relê e responde pela mesma regra.
8. Tentativa de resgate de voucher vencido grava o status `Expired` e o evento `Expired`, antes do `422` (US-05.3 cenário 2). A verificação não faz isso (só lê o status efetivo).

### 1.3 Base reutilizada

`Voucher` (`MaxUses`, `UsesCount`, `RemainingUses`, `IsValidAt`, `EffectiveStatus`), `VoucherEvent`, `Concession` (`TransactionId`), `Campaign.IsInScope`, `MinPurchaseAmount`, `MaxPurchaseAmount`, `AllowUseInSamePurchase`, `CampaignItem.Matches`, `ConcessionEvaluator.EstimatedBenefit` (base do desconto), `Purchase` e a resolução de itens por `productCode` (`ICatalogRepository.GetProductsByCodesAsync`), `IAuditTrail`, policies `SatelliteRedeemVouchers` e `SatelliteReadCampaigns` (US-07.5), `BusinessTime` (D9), `ApiFactory`, `MotorScenario`, `scripts/smoke`.

### 1.4 Decisões e divergências (aprovar antes do código)

| # | Ponto | Proposta |
|---|---|---|
| D-1 | Recorte. | Issue #49: US-05.1 a 05.6 no recorte abaixo, US-07.3, US-07.1 e US-04.9 cenários 4 e 5. A US-05.5 fica só com o cenário 1 e com a recusa de voucher de campanha `Cancelled`; a invalidação em lote vai junto com a operação de cancelar (PA-1). |
| D-2 | Modelo. | Entidade nova `Redemption` em `Domain/Vouchers`, tabela `redemptions`: um resgate por transação (índice único `network_id + transaction_id`) e vários por voucher (multiuso). Guarda loja, canal, valor bruto, desconto aplicado, CPF, aplicação e instante. Não é `IAppendOnly` porque o estorno (PA-6) pode precisar de uma coluna de status; sem rota de alteração nesta entrega. Migração `VoucherRedemptions` nos dois bancos. |
| D-3 | Mesma compra na verificação, no resgate e na concessão. | Os três recebem `transactionId`, `customer.document`, `establishmentId`, `channel`, `totalAmount` e `items`, no mesmo formato do `POST /concessions` (D-12 e D-16 da GH-045). Assim o PDV monta um payload só. `customerPurchaseCount` não entra (só condição de geração). |
| D-4 | Loja e canal. | Mesmas regras da concessão (GH-045 D-13), com códigos próprios: `422 REDEMPTION_ESTABLISHMENT_NOT_FOUND`, `REDEMPTION_ESTABLISHMENT_INACTIVE`, `REDEMPTION_CHANNEL_NOT_ENABLED`. Mensagens iguais às da emissão. Não se reaproveitam os códigos `ISSUANCE_*` para o cliente da API distinguir a etapa. |
| D-5 | Quais vouchers entram na verificação. | Todos os do CPF na rede da aplicação, exceto `Discarded`, inclusive os que não servem: a US-07.3 pede o motivo de cada um. Resposta separa `applicable` e `notApplicable`. |
| D-6 | Motivos. | Enum `RedemptionRefusal`: `NotConfirmed`, `Expired`, `NoUsesLeft`, `Cancelled`, `CampaignCancelled`, `OutOfScope`, `AmountBelowMinimum`, `AmountAboveMaximum`, `TargetNotInPurchase`, `TransactionAlreadyRedeemed`, `AlreadyRedeemedInThisPurchase`. Na verificação vai no campo `reason` com `detail` em texto; no resgate vira `422` com `code = "REDEMPTION_" + motivo em SNAKE_CASE` (tabela em 2.3) e `reason` como extensão do problem details. |
| D-7 | Desconto previsto e aplicado (US-05.6). | O voucher está preso a **um alvo** desde a emissão (GH-045 D-10, que já aplicou a precedência Produto > Categoria > Todos). O desconto é calculado sobre os itens da compra que casam esse alvo (alvo `All` = valor total): `Percentage` = `round(base × valor / 100, 2)`; `FixedAmount` = `min(valor, base)`, para o desconto nunca passar do que o alvo vale na compra. Sem acumular com outros alvos da campanha. A alternativa (recalcular item a item sobre todos os alvos da campanha, RN10a literal) fica em PA-2. |
| D-8 | Recomendado. | Maior desconto previsto; empate → menor `validUntil` (gasta primeiro o que vence primeiro) → menor `id`. `applicable` vem nessa ordem, o recomendado primeiro, e a resposta traz `onlyOnePerPurchase: true` (US-05.1 cenário 2). |
| D-9 | Consumo atômico (US-05.4 cenário 4). | `ExecuteUpdateAsync` condicional, como a reserva da concessão: `SET uses_count = uses_count + 1, status = CASE WHEN uses_count + 1 >= max_uses THEN 'Redeemed' ELSE status END WHERE id = @id AND status = 'Available' AND uses_count < max_uses`. É o primeiro comando da transação (SQLite exige a escrita antes das leituras na mesma transação). Linhas afetadas = 0 → recusa com o motivo relido. |
| D-10 | Provisório na mesma compra (US-04.9 cenário 4). | A verificação lista o provisório da própria transação como aplicável com `requiresConfirmation: true`, com o desconto previsto. O resgate dele antes da confirmação da concessão → `422 REDEMPTION_NOT_CONFIRMED` (D3 da issue #45: "o provisório só resgata depois da confirmação"). A ordem para o PDV é: conceder → confirmar → resgatar. A tensão com "confirmar depois do pagamento" está em PA-3. |
| D-11 | Validade (US-05.3). | `Voucher.IsValidAt` (inclusivo até o último segundo, D9). A vigência da campanha não entra no resgate (cenário 4). O resgate de voucher vencido persiste `Expired` + evento, numa gravação separada antes do `422`, para o status do voucher ficar consistente mesmo sem varredura. |
| D-12 | Voucher de outro CPF, de outra rede ou inexistente. | `404 NOT_FOUND` "Voucher não encontrado." nas três situações, para não revelar vouchers alheios. O `customer.document` do resgate precisa bater com o do voucher. |
| D-13 | Rotas. | `POST /api/v1/satellite/vouchers/check`, `POST /api/v1/satellite/redemptions`, `GET /api/v1/satellite/redemptions/{id}` (policy `SatelliteRedeemVouchers`) e `GET /api/v1/satellite/campaigns` (policy `SatelliteReadCampaigns`). Controllers `SatelliteRedemptionsController` e `SatelliteCampaignsController`. O `pdv-demo` ganha o escopo `vouchers:redeem` no seed de Development. |
| D-14 | Campanhas vigentes (US-07.1). | Só as com status efetivo `Active` da rede da aplicação (exclui `Scheduled`, `Paused`, `Ended`, `SoldOut`, `Cancelled` e as não aprovadas). Filtros opcionais `establishmentId` (loja da rede, senão `404 NOT_FOUND`) e `channel`: com loja, só campanhas cujo escopo a contempla (e o canal, se informado); só canal, campanhas com algum escopo naquele canal. Resumo: id, nome, vigência no fuso da rede, alvos e benefícios, condições, escopo (lojas e regiões com canais, ou `network: true`), `audience: "All"` (até a #47), `allowUseInSamePurchase`, restrições de valor e `maxUsesPerVoucher`. Sem contadores de saldo (o satélite não precisa saber quanto resta). |
| D-15 | Histórico e auditoria. | `VoucherEventTypes` ganha `Redeemed` (payload: transação, loja, canal, valor, desconto, `usesCount`) e `Expired`. `AuditEventTypes` ganha `VoucherRedeemed` e `VoucherRedemptionRefused` (com o motivo), ambos com `actor_type = Application`. A verificação não audita (é leitura). |
| D-16 | Resposta do resgate. | `200` com o `VoucherResponse` já atualizado (status `Available` com `remainingUses` menor, ou `Redeemed`), o desconto aplicado e o instante. Repetição idempotente devolve o mesmo corpo com o `redemptionId` original. |
| D-17 | `GET /customers/{cpf}/vouchers` (US-07.4). | Sem mudança de contrato. O status efetivo já reflete `Redeemed` e `Expired`. |
| D-18 | Contadores da campanha. | Nenhum contador novo em `campaigns`: resgatados e expirados saem de `vouchers` quando a #50 precisar (PA-4). |

---

## 2. Contratos & Schemas (DTOs / Records)

### 2.1 Domínio (`Domain/Vouchers`)

```csharp
public enum RedemptionRefusal
{
    NotConfirmed, Expired, NoUsesLeft, Cancelled, CampaignCancelled, OutOfScope,
    AmountBelowMinimum, AmountAboveMaximum, TargetNotInPurchase,
    TransactionAlreadyRedeemed, AlreadyRedeemedInThisPurchase
}

public class Redemption
{
    public Guid Id { get; set; }                 // v7
    public Guid NetworkId { get; set; }
    public Guid SatelliteAppId { get; set; }
    public Guid VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public Guid CampaignId { get; set; }
    public string TransactionId { get; set; }    // <= 100, único por rede
    public string CustomerDocument { get; set; } // 11 dígitos
    public Guid EstablishmentId { get; set; }
    public ChannelType Channel { get; set; }
    public decimal TotalAmount { get; set; }     // bruto, PR-16
    public decimal DiscountAmount { get; set; }  // D-7
    public int ItemCount { get; set; }
    public DateTime RedeemedAt { get; set; }
}

/// <summary>Passos 4 e 5 do fluxo (1.2), sem I/O.</summary>
public static class RedemptionEvaluator
{
    public static RedemptionRefusal? Refusal(Voucher voucher, Campaign campaign, CampaignItem target, Concession concession,
        Purchase purchase, Establishment store, ChannelType channel, string transactionId, Redemption? existing, DateTime utcNow);
    public static decimal Discount(CampaignItem target, Purchase purchase);   // D-7
    public static bool RequiresConfirmation(Voucher voucher, Campaign campaign, Concession concession, string transactionId); // D-10
}

public static class VoucherEventTypes { /* + */ public const string Redeemed = "Redeemed"; public const string Expired = "Expired"; }
public static class AuditEventTypes  { /* + */ public const string VoucherRedeemed = "VoucherRedeemed"; public const string VoucherRedemptionRefused = "VoucherRedemptionRefused"; }
```

`Voucher` ganha o método `bool CanBeRedeemedAt(DateTime utcNow) => EffectiveStatus(utcNow) == VoucherStatus.Available && RemainingUses > 0`.

### 2.2 Aplicação (`Application/Vouchers`)

```csharp
// Mesma compra da concessão (D-3), sem customerPurchaseCount.
public record PurchaseContextRequest(
    [Required, StringLength(100)] string? TransactionId,
    [Required] ConcessionCustomerRequest? Customer,
    [Required] Guid? EstablishmentId,
    [Required] string? Channel,                       // Physical | Virtual
    [Required] decimal? TotalAmount,                  // > 0, 2 casas
    [Required, MinLength(1), MaxLength(500)] IReadOnlyList<PurchaseItemRequest>? Items);

public record RedemptionCheckRequest(/* os mesmos campos de PurchaseContextRequest */);

public record RedemptionCheckResponse(
    string TransactionId,
    bool OnlyOnePerPurchase,                          // sempre true (US-05.1)
    Guid? RedemptionId,                               // resgate já feito nesta transação, se houver
    IReadOnlyList<ApplicableVoucherResponse> Applicable,      // ordem D-8, recomendado primeiro
    IReadOnlyList<InapplicableVoucherResponse> NotApplicable);

public record ApplicableVoucherResponse(VoucherResponse Voucher, decimal DiscountAmount, bool Recommended, bool RequiresConfirmation);
public record InapplicableVoucherResponse(VoucherResponse Voucher, string Reason, string Detail);

public record RedemptionRequest(
    [Required] Guid? VoucherId,
    /* + os campos de PurchaseContextRequest */);

public record RedemptionResponse(
    Guid RedemptionId,
    string TransactionId,
    VoucherResponse Voucher,                          // já com o status e remainingUses atualizados
    decimal DiscountAmount,
    DateTimeOffset RedeemedAt);                       // fuso da rede

public interface IRedemptionService
{
    Task<RedemptionCheckResponse> CheckAsync(RedemptionCheckRequest request, CancellationToken ct);
    Task<RedemptionResponse> RedeemAsync(RedemptionRequest request, CancellationToken ct);
    Task<RedemptionResponse> GetAsync(Guid redemptionId, CancellationToken ct);
}

public interface IRedemptionRepository
{
    Task<IReadOnlyList<Voucher>> GetCustomerVouchersForRedemptionAsync(Guid networkId, string document, CancellationToken ct); // com Campaign, Item, Concession
    Task<Voucher?> FindVoucherAsync(Guid voucherId, Guid networkId, string document, CancellationToken ct);
    Task<Redemption?> FindByTransactionAsync(Guid networkId, string transactionId, CancellationToken ct);
    Task<Redemption?> FindByIdAsync(Guid id, Guid networkId, CancellationToken ct);
    Task<IConcessionTransaction> BeginTransactionAsync(CancellationToken ct);
    Task<bool> TryConsumeUseAsync(Guid voucherId, CancellationToken ct);                 // D-9
    Task<bool> TryMarkExpiredAsync(Guid voucherId, DateTime utcNow, CancellationToken ct); // D-11
    void Add(Redemption redemption); void Add(VoucherEvent voucherEvent);
    Task<bool> TrySaveChangesAsync(CancellationToken ct);                                // false na violação do índice único
}

// US-07.1
public record SatelliteCampaignResponse(
    Guid Id, string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    IReadOnlyList<VoucherTargetResponse> Items,
    IReadOnlyList<SatelliteConditionResponse> Conditions,
    SatelliteScopeResponse Scope,
    string Audience,                                  // "All"
    bool AllowUseInSamePurchase, decimal? MinPurchaseAmount, decimal? MaxPurchaseAmount, int? MaxUsesPerVoucher);
public record SatelliteConditionResponse(string Type, decimal? MinAmount, int? MinQuantity, Guid? CategoryId, Guid? ProductId);
public record SatelliteScopeResponse(bool Network, IReadOnlyList<SatelliteScopeEntryResponse> Entries);
public record SatelliteScopeEntryResponse(Guid? RegionId, Guid? EstablishmentId, IReadOnlyList<string> Channels);

public interface ISatelliteCampaignService
{
    Task<IReadOnlyList<SatelliteCampaignResponse>> GetActiveAsync(Guid? establishmentId, string? channel, CancellationToken ct);
}
```

### 2.3 Erros

| Código | HTTP | Mensagem |
|---|---|---|
| `VALIDATION_ERROR` | 400 | Contrato (mensagens em `RedemptionContract`, no padrão de `ConcessionContract`). |
| `REDEMPTION_ESTABLISHMENT_NOT_FOUND` | 422 | "A loja informada não existe nesta rede." |
| `REDEMPTION_ESTABLISHMENT_INACTIVE` | 422 | "A loja informada está inativa; o resgate não é avaliado até a loja voltar." |
| `REDEMPTION_CHANNEL_NOT_ENABLED` | 422 | "A loja {nome} não opera o canal {canal}." |
| `REDEMPTION_NOT_CONFIRMED` | 422 | "O voucher ainda é provisório; confirme a concessão antes de resgatar." |
| `REDEMPTION_EXPIRED` | 422 | "O voucher venceu em {validUntil}." |
| `REDEMPTION_NO_USES_LEFT` | 422 | "O voucher já consumiu todas as utilizações." |
| `REDEMPTION_CANCELLED` | 422 | "O voucher foi cancelado." |
| `REDEMPTION_CAMPAIGN_CANCELLED` | 422 | "A campanha do voucher foi cancelada." |
| `REDEMPTION_OUT_OF_SCOPE` | 422 | "O voucher não vale nesta loja ou canal." |
| `REDEMPTION_AMOUNT_BELOW_MINIMUM` | 422 | "A compra precisa ser de pelo menos R$ {min} para usar este voucher." |
| `REDEMPTION_AMOUNT_ABOVE_MAXIMUM` | 422 | "A compra não pode passar de R$ {max} para usar este voucher." |
| `REDEMPTION_TARGET_NOT_IN_PURCHASE` | 422 | "A compra não tem itens do alvo deste voucher." |
| `REDEMPTION_TRANSACTION_ALREADY_REDEEMED` | 422 | "Esta compra já usou um voucher; só um voucher por compra." |
| `NOT_FOUND` | 404 | "Voucher não encontrado." / "Resgate não encontrado." / "A loja informada não existe nesta rede." (filtro da US-07.1) |

Todo `422` de motivo traz a extensão `reason` com o valor do enum. `AlreadyRedeemedInThisPurchase` não vira `422`: o resgate repetido do mesmo voucher na mesma transação responde `200` (idempotência).

---

## 3. Especificação dos Endpoints

Autenticação: token de aplicação satélite (US-07.5). Token de usuário → `401`. Escopo errado → `403 AUTH_FORBIDDEN_SCOPE`.

### 3.1 `POST /api/v1/satellite/vouchers/check` (policy `SatelliteRedeemVouchers`)

- Corpo: `RedemptionCheckRequest`.
- `200 RedemptionCheckResponse`. Cliente sem voucher, ou nenhum aplicável → `applicable: []` (US-05.1 cenário 4) e a compra segue.
- `400`, `422` de loja e canal (D-4). Não grava nada.

### 3.2 `POST /api/v1/satellite/redemptions` (policy `SatelliteRedeemVouchers`)

- Corpo: `RedemptionRequest`.
- `200 RedemptionResponse` (resgate novo ou o original da mesma transação com o mesmo voucher).
- `404 NOT_FOUND` voucher inexistente, de outro CPF ou de outra rede (D-12).
- `422` com os códigos de 2.3.

### 3.3 `GET /api/v1/satellite/redemptions/{id}` (policy `SatelliteRedeemVouchers`)

- `200 RedemptionResponse` do resgate da rede da aplicação; `404` fora dela.

### 3.4 `GET /api/v1/satellite/campaigns?establishmentId=&channel=` (policy `SatelliteReadCampaigns`)

- `200` lista de `SatelliteCampaignResponse`, por `endsAt` crescente e nome.
- `400` canal inválido; `404 NOT_FOUND` loja inexistente ou de outra rede.

---

## 4. Checklist de Implementação

- [ ] 1. Domínio: `Redemption`, `RedemptionRefusal`, `RedemptionEvaluator` (motivos, desconto, recomendado), `Voucher.CanBeRedeemedAt`, eventos e tipos de auditoria novos.
- [ ] 2. Persistência: `RedemptionConfiguration` (tabela `redemptions`, índice único `network_id + transaction_id`, índice `voucher_id`, FKs `vouchers` e `establishments` em `Restrict`, CHECKs de valores `>= 0`), `DbSet<Redemption>`, migração `VoucherRedemptions` (SQLite e PostgreSQL) via `scripts/add-migration.sh`.
- [ ] 3. Aplicação: contratos de 2.2, `RedemptionService` (fluxo 1.2), `RedemptionRepository` (consumo e expiração por `ExecuteUpdateAsync`), `SatelliteCampaignService`; registro em `DependencyInjection` e `Program.cs`.
- [ ] 4. API: `SatelliteRedemptionsController` e `SatelliteCampaignsController`; `pdv-demo` com `vouchers:redeem` no seed e no `appsettings.Development.json`.
- [ ] 5. Testes unitários: `RedemptionEvaluatorTests` (cada motivo, ordem dos motivos, desconto percentual e fixo com teto da base, recomendado e desempate, provisório na mesma compra).
- [ ] 6. Testes de integração: `SatelliteRedemptionsEndpointTests` (verificar, resgatar, multiuso, idempotência, um por compra, 404, 422 de loja e canal, 401 e 403), `RedemptionConcurrencyTests` (20 resgates simultâneos do último uso → um `200`), `SatelliteCampaignsEndpointTests` (US-07.1 com filtros e status), e o cenário completo conceder → confirmar → verificar → resgatar.
- [ ] 7. `scripts/smoke/motor.py`: verificar e resgatar depois da confirmação; `GET /satellite/campaigns`.
- [ ] 8. Documentos: README (seção do satélite), `agents.md` (skill do motor), `ROADMAP.md`, `PROJECT_STATE.md`; spec para `completed/`.

---

## 5. Casos de Teste Essenciais

- [ ] Voucher `Available`, dentro da validade e com item do alvo → `check` devolve aplicável com desconto; `redeem` → `200`, `usesCount` 1, status `Redeemed` quando `maxUses` = 1, evento e auditoria gravados (US-05.4 cenário 1, US-07.3 cenário 2).
- [ ] `maxUses` = 3 → três resgates em transações diferentes; o quarto → `422 REDEMPTION_NO_USES_LEFT` sem alterar o histórico (US-05.4 cenários 2 e 3).
- [ ] Dois vouchers aplicáveis com descontos R$ 15 e R$ 27 → ambos listados, o de R$ 27 recomendado, `onlyOnePerPurchase: true` (US-05.1 cenário 2).
- [ ] Cliente recusa: nenhuma chamada de resgate → voucher segue `Available` (US-05.1 cenário 3, só documental).
- [ ] Voucher de categoria sem item dessa categoria no carrinho → `notApplicable` com `TargetNotInPurchase` (US-05.1 cenário 5); alvo `All` sempre aplicável.
- [ ] Valor abaixo do mínimo e acima do máximo → `AmountBelowMinimum` / `AmountAboveMaximum`, sem consumo (US-05.2 cenário 2).
- [ ] Loja fora do escopo da campanha ou canal não contemplado → `OutOfScope` (US-05.2 cenário 4); loja inexistente, inativa ou sem o canal → `422 REDEMPTION_*`.
- [ ] Relógio depois de `validUntil` → `check` mostra `Expired`; `redeem` → `422 REDEMPTION_EXPIRED` e o banco passa a `Expired` com evento (US-05.3 cenário 2). Até 23:59:59 do último dia ainda resgata (D9).
- [ ] Campanha encerrada ou pausada depois da emissão → resgate segue (US-05.3 cenário 4, US-05.5 cenário 1). Campanha com status gravado `Cancelled` → `CampaignCancelled`.
- [ ] Mesma transação: segundo voucher → `422 REDEMPTION_TRANSACTION_ALREADY_REDEEMED`; mesmo voucher repetido → `200` idêntico, `usesCount` não muda (US-07.3 cenários 4 e 5).
- [ ] Provisório da própria transação: aplicável com `requiresConfirmation` se a campanha permite uso na mesma compra, `NotConfirmed` se não permite (US-04.9 cenários 4 e 5); resgate antes da confirmação → `422 REDEMPTION_NOT_CONFIRMED`; depois da confirmação → `200`.
- [ ] 20 resgates simultâneos do último uso → exatamente um `200`, um único evento `Redeemed` (US-05.4 cenário 4).
- [ ] Voucher de outro CPF ou de outra rede → `404`.
- [ ] `GET /satellite/campaigns`: ativa aparece; agendada, pausada, encerrada, esgotada e rascunho não; filtro por loja e canal respeita `campaign_scopes`; loja de outra rede → `404`.
- [ ] Token sem `vouchers:redeem` → `403`; token de usuário → `401`.

---

## 6. Pontos em aberto

| # | Ponto | Motivo de adiar | Gatilho |
|---|---|---|---|
| PA-1 | **Cancelamento da campanha em lote** (RN10, US-05.5 cenários 2 e 3): marcar `Cancelled` os vouchers não utilizados, com evento e auditoria, preservando os `Redeemed`. | Não existe operação de cancelar campanha (US-01.8 entregou pausar e encerrar). Aqui só se recusa o voucher cuja campanha estiver `Cancelled`. | A operação de cancelar (Onda 3 ou pedido do PO). |
| PA-2 | **Desconto item a item sobre todos os alvos da campanha** (RN10a literal, US-05.6 cenários 1 a 3). | O voucher nasce preso a um alvo (GH-045 D-10) e a cota é por alvo; recalcular sobre todos os alvos descolaria o desconto da cota consumida. | O PO confirmar que um voucher de campanha com vários alvos deve descontar itens de alvos diferentes. |
| PA-3 | **Ordem conceder → confirmar → resgatar na mesma compra.** A US-04.9 diz que a confirmação vem depois do pagamento e que o provisório pode ser aplicável na mesma compra; as duas coisas juntas exigem confirmar antes de pagar. | Decisão D3 da issue #45 já fixou "só resgata depois da confirmação". | Integração real de um PDV com uso na mesma compra. |
| PA-4 | **Contadores de resgatados e expirados na campanha** e no portal. | Saem de `vouchers` por consulta; o dashboard é a #50. | Issue #50. |
| PA-5 | **Varredura para persistir `Expired`** dos vouchers vencidos que nunca foram consultados. | O status efetivo já responde certo; a gravação acontece na tentativa de resgate (D-11). | Relatórios da #50 que leiam o status gravado. |
| PA-6 | **Estorno do resgate** quando a venda é desfeita depois do desconto aplicado (devolver a utilização). | O backlog não define a operação. A `redemptions` não é append-only por causa disso. | Pedido do PO ou do time do PDV. |
| PA-7 | **Desconto fixo maior que a base** (`FixedAmount` 50 numa compra de 30 do alvo): adotado `min(valor, base)`. | Regra não escrita no backlog. | Validação com o PO. |
| PA-8 | **Público elegível na US-07.1**: devolve `audience: "All"`. | Critérios são a #47. | Issue #47. |
| PA-9 | **Idempotência por chave própria** (US-07.3 cenário 5 fala em "chave de idempotência"): usada a `transactionId`, como na concessão (PR-07). | Uma chave extra duplicaria o papel da transação. | O PDV precisar reenviar com outra transação. |
| PA-10 | **PostgreSQL**: migração `VoucherRedemptions` e o `UPDATE` condicional validados só em SQLite nesta máquina. | Sem Docker local. | Suíte com `TEST_POSTGRES_CONNECTION`. |
