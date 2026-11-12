# 📋 Especificação Técnica: GH-049 - Verificação de aplicáveis e resgate de vouchers (EP-05, US-07.1, US-07.3)

> Status: Completed (decisões aprovadas pelo usuário em 2026-11-11: D-7 como proposta; D-10 na variante "resgata o provisório direto, cancelar estorna"; D-4, D-12 e D-14 como propostas)
> História de Usuário Relacionada: issue #49 (fatia da #27), US-05.1 a US-05.6, US-07.1, US-07.3, US-04.9 (cenários 4 e 5)
> Responsável: @iagovrocha
> Data: 2026-11-11

---

## 1. Contexto & Regras de Negócio

### 1.1 Objetivo

Fechar o ciclo do voucher que a issue #45 emite: com o cliente identificado e o carrinho fechado, o satélite pergunta **quais vouchers daquele cliente servem nesta compra** (US-05.1, US-07.3 cenário 1), mostra ao cliente, e **resgata o escolhido** (US-07.3 cenário 2) com as mesmas regras do motor: validade própria (US-05.3), restrições de uso da campanha (US-05.2), limite de utilizações com concorrência (US-05.4), efeito do status da campanha (US-05.5) e benefício do alvo (US-05.6). Também entrega a consulta de **campanhas vigentes** por API (US-07.1), que a issue #49 cita e nenhuma fatia anterior cobriu.

O resgate é automático do ponto de vista do cliente (RF17): ele não digita código. O código do voucher continua identificador interno; o satélite trabalha com o `id` que a verificação devolve.

Fora desta spec: público restrito (#47), relatórios e CSV (#50) e o cancelamento da campanha com invalidação em lote (RN10, US-05.5 cenários 2 e 3; não existe operação de cancelar, PA-1). O **estorno** de um resgate entra nesta fatia (D-10 e D-19), porque o provisório pode ser resgatado na própria compra e a venda ainda pode ser desfeita.

### 1.2 Fluxo da verificação e do resgate (ordem de avaliação)

A verificação (`POST /satellite/vouchers/check`) e o resgate (`POST /satellite/redemptions`) recebem a **mesma compra** da concessão (D-3) e avaliam cada voucher do cliente nesta ordem. A verificação devolve o resultado de todos; o resgate aplica ao voucher escolhido e, se passar, consome a utilização.

1. Contrato (`400`): transação, cliente, loja, canal, valor total e itens (e `voucherId`, no resgate).
2. Loja da rede da aplicação, ativa e que opera o canal (`422`, D-4). Nada é gravado.
3. Vouchers do cliente na rede, exceto `Discarded`, com campanha, alvo e concessão (D-5). No resgate, só o `voucherId` informado; de outro CPF ou de outra rede → `404` (D-12).
4. Para cada voucher, o **primeiro motivo** que se aplica (D-6):
   0. Já resgatado **nesta** transação → `AlreadyRedeemedInThisPurchase` (antes do status, que pode ter virado `Redeemed` por esse mesmo resgate).
   1. `Provisional` da **mesma transação**, com a reserva em dia e campanha com `allowUseInSamePurchase` → aplicável e **resgatável desde já** (US-04.9 cenário 4, D-10); `Provisional` da mesma transação sem a permissão → `SamePurchaseNotAllowed` (US-04.9 cenário 5); `Provisional` de outra transação → `NotConfirmed`.
   2. Status efetivo `Expired` → `Expired` (US-05.3, D9 inclusivo); `Redeemed` → `NoUsesLeft`; `Cancelled` → `Cancelled`.
   3. Campanha com status gravado `Cancelled` → `CampaignCancelled` (RN10). `Paused`, `Ended` e `SoldOut` **não recusam** (RN07a, US-05.5 cenário 1).
   4. Escopo: a campanha não contempla a loja e o canal da compra → `OutOfScope` (RN08, US-05.2 cenário 4).
   5. Valor bruto da compra (PR-16) abaixo de `minPurchaseAmount` → `AmountBelowMinimum`; acima de `maxPurchaseAmount` → `AmountAboveMaximum` (US-05.2 cenário 2).
   6. Nenhum item da compra casa o alvo do voucher (alvo `All` sempre casa) → `TargetNotInPurchase` (US-05.2 cenário 3, US-05.1 cenário 5).
   7. Já existe resgate de **outro** voucher nesta transação → `TransactionAlreadyRedeemed` (US-07.3 cenário 4).
5. Aplicáveis recebem o **desconto previsto** (D-7) e o de maior desconto é o **recomendado** (RF17b, D-8). A verificação **não grava nada** (US-05.1 cenário 3, US-07.3 cenário 1).
6. Resgate do aplicável: `UPDATE vouchers ... WHERE id = @id AND uses_count < max_uses AND (status = 'Available' OR (status = 'Provisional' AND reserved_until > @now))` atômico (D-9); zero linhas → relê o voucher e devolve o motivo atual (US-05.4 cenário 4, US-07.3 cenário 6). Em seguida grava `redemptions` (`Effective`), o evento `Redeemed` e a auditoria `VoucherRedeemed` no mesmo `SaveChanges`. Quando um `Available` chega a `max_uses`, o status passa a `Redeemed` no mesmo `UPDATE` (US-05.4 cenários 1 e 2); um `Provisional` continua `Provisional` e é a confirmação da concessão que o leva a `Available` ou `Redeemed` (D-10).
7. Idempotência (US-07.3 cenário 5): `redemptions` tem índice único por rede + `transactionId`. Antes do passo 6, resgate já existente nesta transação com o mesmo voucher → `200` com o resgate original; com outro voucher → `422 REDEMPTION_TRANSACTION_ALREADY_REDEEMED`. Se duas chamadas simultâneas passarem pela leitura, a violação do índice desfaz a transação do banco e a chamada relê e responde pela mesma regra.
8. Tentativa de resgate de voucher vencido grava o status `Expired` e o evento `Expired`, antes do `422` (US-05.3 cenário 2). A verificação não faz isso (só lê o status efetivo).
9. **Estorno** (`POST /satellite/redemptions/{id}/reverse`, D-19): resgate `Effective` → `Reversed`, `uses_count - 1` e, se o voucher estava `Redeemed`, volta a `Available`; evento `RedemptionReversed` e auditoria `VoucherRedemptionReversed`. Idempotente: já `Reversed` → `200` igual.
10. **Cancelar ou expirar a concessão** de um voucher resgatado na própria compra (D-10) estorna o resgate do mesmo jeito que o passo 9, dentro da mesma operação que descarta o voucher; **confirmar** a concessão leva o voucher a `Available` ou, se os usos acabaram, a `Redeemed`.

### 1.3 Base reutilizada

`Voucher` (`MaxUses`, `UsesCount`, `RemainingUses`, `IsValidAt`, `EffectiveStatus`), `VoucherEvent`, `Concession` (`TransactionId`), `Campaign.IsInScope`, `MinPurchaseAmount`, `MaxPurchaseAmount`, `AllowUseInSamePurchase`, `CampaignItem.Matches`, `ConcessionEvaluator.EstimatedBenefit` (base do desconto), `Purchase` e a resolução de itens por `productCode` (`ICatalogRepository.GetProductsByCodesAsync`), `IAuditTrail`, policies `SatelliteRedeemVouchers` e `SatelliteReadCampaigns` (US-07.5), `BusinessTime` (D9), `ApiFactory`, `MotorScenario`, `scripts/smoke`.

### 1.4 Decisões e divergências (aprovar antes do código)

| # | Ponto | Proposta |
|---|---|---|
| D-1 | Recorte. | Issue #49: US-05.1 a 05.6 no recorte abaixo, US-07.3, US-07.1 e US-04.9 cenários 4 e 5. A US-05.5 fica só com o cenário 1 e com a recusa de voucher de campanha `Cancelled`; a invalidação em lote vai junto com a operação de cancelar (PA-1). |
| D-2 | Modelo. | Entidade nova `Redemption` em `Domain/Vouchers`, tabela `redemptions`: um resgate por transação (índice único `network_id + transaction_id`, mesmo depois de estornado: a transação está encerrada) e vários por voucher (multiuso). Guarda loja, canal, valor bruto, desconto aplicado, CPF, aplicação, instante, `status` (`Effective`, `Reversed`) e `reversed_at`. Não é `IAppendOnly` por causa do estorno; a única alteração é `Effective → Reversed`. Migração `VoucherRedemptions` nos dois bancos. |
| D-3 | Mesma compra na verificação, no resgate e na concessão. | Os três recebem `transactionId`, `customer.document`, `establishmentId`, `channel`, `totalAmount` e `items`, no mesmo formato do `POST /concessions` (D-12 e D-16 da GH-045). Assim o PDV monta um payload só. `customerPurchaseCount` não entra (só condição de geração). |
| D-4 | Loja e canal. | Mesmas regras da concessão (GH-045 D-13), com códigos próprios: `422 REDEMPTION_ESTABLISHMENT_NOT_FOUND`, `REDEMPTION_ESTABLISHMENT_INACTIVE`, `REDEMPTION_CHANNEL_NOT_ENABLED`. Mensagens iguais às da emissão. Não se reaproveitam os códigos `ISSUANCE_*` para o cliente da API distinguir a etapa. |
| D-5 | Quais vouchers entram na verificação. | Todos os do CPF na rede da aplicação, exceto `Discarded`, inclusive os que não servem: a US-07.3 pede o motivo de cada um. Resposta separa `applicable` e `notApplicable`. |
| D-6 | Motivos. | Enum `RedemptionRefusal`: `NotConfirmed`, `Expired`, `NoUsesLeft`, `Cancelled`, `CampaignCancelled`, `OutOfScope`, `AmountBelowMinimum`, `AmountAboveMaximum`, `TargetNotInPurchase`, `TransactionAlreadyRedeemed`, `AlreadyRedeemedInThisPurchase`. Na verificação vai no campo `reason` com `detail` em texto; no resgate vira `422` com `code = "REDEMPTION_" + motivo em SNAKE_CASE` (tabela em 2.3) e `reason` como extensão do problem details. |
| D-7 | Desconto previsto e aplicado (US-05.6). | O voucher está preso a **um alvo** desde a emissão (GH-045 D-10, que já aplicou a precedência Produto > Categoria > Todos). O desconto é calculado sobre os itens da compra que casam esse alvo (alvo `All` = valor total): `Percentage` = `round(base × valor / 100, 2)`; `FixedAmount` = `min(valor, base)`, para o desconto nunca passar do que o alvo vale na compra. Sem acumular com outros alvos da campanha. A alternativa (recalcular item a item sobre todos os alvos da campanha, RN10a literal) fica em PA-2. |
| D-8 | Recomendado. | Maior desconto previsto; empate → menor `validUntil` (gasta primeiro o que vence primeiro) → menor `id`. `applicable` vem nessa ordem, o recomendado primeiro, e a resposta traz `onlyOnePerPurchase: true` (US-05.1 cenário 2). |
| D-9 | Consumo atômico (US-05.4 cenário 4). | `ExecuteUpdateAsync` condicional, como a reserva da concessão: `SET uses_count = uses_count + 1, status = CASE WHEN uses_count + 1 >= max_uses THEN 'Redeemed' ELSE status END WHERE id = @id AND status = 'Available' AND uses_count < max_uses`. É o primeiro comando da transação (SQLite exige a escrita antes das leituras na mesma transação). Linhas afetadas = 0 → recusa com o motivo relido. |
| D-10 | Provisório na mesma compra (US-04.9 cenário 4). **Aprovada na variante alternativa.** | O provisório da própria transação, com a reserva em dia e campanha que permite uso na mesma compra, é aplicável e **resgatável antes da confirmação**: o PDV concede, verifica, resgata, cobra e só então confirma. O resgate não muda o status `Provisional`; a **confirmação** leva o voucher a `Available` (ou `Redeemed`, se os usos acabaram). **Cancelar a concessão** ou **expirar a reserva** descarta o voucher e **estorna** o resgate (D-19), devolvendo a utilização e o saldo da campanha. Provisório de outra transação → `NotConfirmed`; da mesma transação sem a permissão → `SamePurchaseNotAllowed`. Substitui a leitura da D3 da GH-045 ("só resgata depois da confirmação") para o caso da mesma compra. |
| D-11 | Validade (US-05.3). | `Voucher.IsValidAt` (inclusivo até o último segundo, D9). A vigência da campanha não entra no resgate (cenário 4). O resgate de voucher vencido persiste `Expired` + evento, numa gravação separada antes do `422`, para o status do voucher ficar consistente mesmo sem varredura. |
| D-12 | Voucher de outro CPF, de outra rede ou inexistente. | `404 NOT_FOUND` "Voucher não encontrado." nas três situações, para não revelar vouchers alheios. O `customer.document` do resgate precisa bater com o do voucher. |
| D-13 | Rotas. | `POST /api/v1/satellite/vouchers/check`, `POST /api/v1/satellite/redemptions`, `GET /api/v1/satellite/redemptions/{id}` (policy `SatelliteRedeemVouchers`) e `GET /api/v1/satellite/campaigns` (policy `SatelliteReadCampaigns`). Controllers `SatelliteRedemptionsController` e `SatelliteCampaignsController`. O `pdv-demo` ganha o escopo `vouchers:redeem` no seed de Development. |
| D-14 | Campanhas vigentes (US-07.1). | Só as com status efetivo `Active` da rede da aplicação (exclui `Scheduled`, `Paused`, `Ended`, `SoldOut`, `Cancelled` e as não aprovadas). Filtros opcionais `establishmentId` (loja da rede, senão `404 NOT_FOUND`) e `channel`: com loja, só campanhas cujo escopo a contempla (e o canal, se informado); só canal, campanhas com algum escopo naquele canal. Resumo: id, nome, vigência no fuso da rede, alvos e benefícios, condições, escopo (lojas e regiões com canais, ou `network: true`), `audience: "All"` (até a #47), `allowUseInSamePurchase`, restrições de valor e `maxUsesPerVoucher`. Sem contadores de saldo (o satélite não precisa saber quanto resta). |
| D-15 | Histórico e auditoria. | `VoucherEventTypes` ganha `Redeemed` (payload: transação, loja, canal, valor, desconto, `usesCount`) e `Expired`. `AuditEventTypes` ganha `VoucherRedeemed` e `VoucherRedemptionRefused` (com o motivo), ambos com `actor_type = Application`. A verificação não audita (é leitura). |
| D-16 | Resposta do resgate. | `200` com o `VoucherResponse` já atualizado (status `Available` com `remainingUses` menor, ou `Redeemed`), o desconto aplicado e o instante. Repetição idempotente devolve o mesmo corpo com o `redemptionId` original. |
| D-17 | `GET /customers/{cpf}/vouchers` (US-07.4). | Sem mudança de contrato. O status efetivo já reflete `Redeemed` e `Expired`. |
| D-18 | Contadores da campanha. | Nenhum contador novo em `campaigns`: resgatados e expirados saem de `vouchers` quando a #50 precisar (PA-4). |
| D-19 | Estorno (antes PA-6). | `POST /api/v1/satellite/redemptions/{id}/reverse`, policy `SatelliteRedeemVouchers`, sem corpo. Resgate `Effective` da rede → `Reversed` com `reversedAt`; `UPDATE vouchers SET uses_count = uses_count - 1, status = CASE WHEN status = 'Redeemed' THEN 'Available' ELSE status END WHERE id = @id AND uses_count > 0`. Não reabre o índice da transação. Voucher já vencido continua `Expired` no status efetivo (a utilização volta, mas não há prazo para usá-la). Sem janela de tempo e sem motivo obrigatório nesta entrega (PA-6 fica para o PO definir limites). Idempotente. |

---

## 2. Contratos & Schemas (DTOs / Records)

### 2.1 Domínio (`Domain/Vouchers`)

```csharp
public enum RedemptionRefusal
{
    NotConfirmed, SamePurchaseNotAllowed, Expired, NoUsesLeft, Cancelled, CampaignCancelled, OutOfScope,
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
    public RedemptionStatus Status { get; set; }   // Effective | Reversed
    public DateTime? ReversedAt { get; set; }
}

public enum RedemptionStatus { Effective, Reversed }

/// <summary>Passos 4 e 5 do fluxo (1.2), sem I/O.</summary>
public static class RedemptionEvaluator
{
    public static RedemptionRefusal? Refusal(Voucher voucher, Campaign campaign, CampaignItem target, Concession concession,
        Purchase purchase, Establishment store, ChannelType channel, string transactionId, Redemption? existing, DateTime utcNow);
    public static decimal Discount(CampaignItem target, Purchase purchase);   // D-7
}

public static class VoucherEventTypes { /* + */ public const string Redeemed = "Redeemed"; public const string RedemptionReversed = "RedemptionReversed"; public const string Expired = "Expired"; }
public static class AuditEventTypes  { /* + */ public const string VoucherRedeemed = "VoucherRedeemed"; public const string VoucherRedemptionRefused = "VoucherRedemptionRefused"; public const string VoucherRedemptionReversed = "VoucherRedemptionReversed"; }
```

`Voucher` ganha `bool CanBeRedeemedAt(DateTime utcNow, bool sameTransactionAllowed)`: `Available` dentro da validade com usos restantes, ou `Provisional` com a reserva em dia quando `sameTransactionAllowed` (D-10).

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

public record ApplicableVoucherResponse(VoucherResponse Voucher, decimal DiscountAmount, bool Recommended);
public record InapplicableVoucherResponse(VoucherResponse Voucher, string Reason, string Detail);

public record RedemptionRequest(
    [Required] Guid? VoucherId,
    /* + os campos de PurchaseContextRequest */);

public record RedemptionResponse(
    Guid RedemptionId,
    string TransactionId,
    string Status,                                    // Effective | Reversed
    VoucherResponse Voucher,                          // já com o status e remainingUses atualizados
    decimal DiscountAmount,
    DateTimeOffset RedeemedAt,                        // fuso da rede
    DateTimeOffset? ReversedAt);

public interface IRedemptionService
{
    Task<RedemptionCheckResponse> CheckAsync(RedemptionCheckRequest request, CancellationToken ct);
    Task<RedemptionResponse> RedeemAsync(RedemptionRequest request, CancellationToken ct);
    Task<RedemptionResponse> ReverseAsync(Guid redemptionId, CancellationToken ct);      // D-19
    Task<RedemptionResponse> GetAsync(Guid redemptionId, CancellationToken ct);
}

/// <summary>Usado por ConcessionService e ReservationReleaser ao descartar um provisório resgatado (D-10).</summary>
public interface IRedemptionReverser
{
    Task ReverseEffectiveAsync(Voucher voucher, string cause, DateTime utcNow, CancellationToken ct);
}

public interface IRedemptionRepository
{
    Task<IReadOnlyList<Voucher>> GetCustomerVouchersForRedemptionAsync(Guid networkId, string document, CancellationToken ct); // com Campaign, Item, Concession
    Task<Voucher?> FindVoucherAsync(Guid voucherId, Guid networkId, string document, CancellationToken ct);
    Task<Redemption?> FindByTransactionAsync(Guid networkId, string transactionId, CancellationToken ct);
    Task<Redemption?> FindByIdAsync(Guid id, Guid networkId, CancellationToken ct);
    Task<IConcessionTransaction> BeginTransactionAsync(CancellationToken ct);
    Task<bool> TryConsumeUseAsync(Guid voucherId, DateTime utcNow, CancellationToken ct); // D-9
    Task<bool> TryReturnUseAsync(Guid voucherId, CancellationToken ct);                   // D-19
    Task<Redemption?> FindEffectiveByVoucherAsync(Guid voucherId, CancellationToken ct);
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
| `REDEMPTION_NOT_CONFIRMED` | 422 | "O voucher é provisório de outra compra; só vale depois da confirmação dela." |
| `REDEMPTION_SAME_PURCHASE_NOT_ALLOWED` | 422 | "A campanha não permite usar o voucher na compra que o gerou." |
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

### 3.3a `POST /api/v1/satellite/redemptions/{id}/reverse` (policy `SatelliteRedeemVouchers`)

- Sem corpo. `200 RedemptionResponse` com `status: "Reversed"` e o voucher com a utilização devolvida. Repetição → `200` igual. `404` fora da rede.

### 3.3b Efeito nas rotas da concessão (GH-045)

- `POST /concessions/{id}/confirm`: o voucher vai a `Available` ou, se `uses_count >= max_uses`, a `Redeemed`.
- `POST /concessions/{id}/cancel` e a expiração da reserva (lazy e varredura): se houver resgate `Effective` do voucher, ele é estornado junto (evento e auditoria), e o voucher é descartado.

### 3.4 `GET /api/v1/satellite/campaigns?establishmentId=&channel=` (policy `SatelliteReadCampaigns`)

- `200` lista de `SatelliteCampaignResponse`, por `endsAt` crescente e nome.
- `400` canal inválido; `404 NOT_FOUND` loja inexistente ou de outra rede.

---

## 4. Checklist de Implementação

- [x] 1. Domínio: `Redemption`, `RedemptionStatus`, `RedemptionRefusal`, `RedemptionEvaluator` (motivos, desconto, recomendado), `Voucher.CanBeRedeemedAt`, eventos e tipos de auditoria novos.
- [x] 2. Persistência: `RedemptionConfiguration` (tabela `redemptions`, índice único `network_id + transaction_id`, índice `voucher_id`, FKs `vouchers` e `establishments` em `Restrict`, CHECKs de valores `>= 0`), `DbSet<Redemption>`, migração `VoucherRedemptions` (SQLite e PostgreSQL) via `scripts/add-migration.sh`.
- [x] 3. Aplicação: contratos de 2.2, `RedemptionService` (fluxo 1.2, inclusive estorno), `RedemptionRepository` (consumo, devolução e expiração por `ExecuteUpdateAsync`), `IRedemptionReverser` chamado por `ConcessionService.CancelAsync` e `ReservationReleaser`; `TryConfirmVoucherAsync` passa a decidir `Available`/`Redeemed`; `SatelliteCampaignService`; registro em `DependencyInjection` e `Program.cs`.
- [x] 4. API: `SatelliteRedemptionsController` e `SatelliteCampaignsController`; `pdv-demo` com `vouchers:redeem` no seed e no `appsettings.Development.json`.
- [x] 5. Testes unitários: `RedemptionEvaluatorTests` (cada motivo, ordem dos motivos, desconto percentual e fixo com teto da base, recomendado e desempate, provisório na mesma compra).
- [x] 6. Testes de integração: `SatelliteRedemptionsEndpointTests` (verificar, resgatar, multiuso, idempotência, um por compra, estorno, 404, 422 de loja e canal, 401 e 403), `RedemptionConcurrencyTests` (20 resgates simultâneos do último uso → um `200`), `SatelliteCampaignsEndpointTests` (US-07.1 com filtros e status), o cenário conceder → confirmar → verificar → resgatar e o da mesma compra: conceder → verificar → resgatar → confirmar (vira `Redeemed`) e conceder → resgatar → cancelar (estorno + `Discarded`).
- [x] 7. `scripts/smoke/motor.py`: verificar e resgatar depois da confirmação; `GET /satellite/campaigns`.
- [x] 8. Documentos: README (seção do satélite), `agents.md` (skill do motor), `ROADMAP.md`, `PROJECT_STATE.md`; spec para `completed/`.

---

## 5. Casos de Teste Essenciais

- [x] Voucher `Available`, dentro da validade e com item do alvo → `check` devolve aplicável com desconto; `redeem` → `200`, `usesCount` 1, status `Redeemed` quando `maxUses` = 1, evento e auditoria gravados (US-05.4 cenário 1, US-07.3 cenário 2).
- [x] `maxUses` = 3 → três resgates em transações diferentes; o quarto → `422 REDEMPTION_NO_USES_LEFT` sem alterar o histórico (US-05.4 cenários 2 e 3).
- [x] Dois vouchers aplicáveis com descontos R$ 15 e R$ 27 → ambos listados, o de R$ 27 recomendado, `onlyOnePerPurchase: true` (US-05.1 cenário 2).
- [x] Cliente recusa: nenhuma chamada de resgate → voucher segue `Available` (US-05.1 cenário 3, só documental).
- [x] Voucher de categoria sem item dessa categoria no carrinho → `notApplicable` com `TargetNotInPurchase` (US-05.1 cenário 5); alvo `All` sempre aplicável.
- [x] Valor abaixo do mínimo e acima do máximo → `AmountBelowMinimum` / `AmountAboveMaximum`, sem consumo (US-05.2 cenário 2).
- [x] Loja fora do escopo da campanha ou canal não contemplado → `OutOfScope` (US-05.2 cenário 4); loja inexistente, inativa ou sem o canal → `422 REDEMPTION_*`.
- [x] Relógio depois de `validUntil` → `check` mostra `Expired`; `redeem` → `422 REDEMPTION_EXPIRED` e o banco passa a `Expired` com evento (US-05.3 cenário 2). Até 23:59:59 do último dia ainda resgata (D9).
- [x] Campanha encerrada ou pausada depois da emissão → resgate segue (US-05.3 cenário 4, US-05.5 cenário 1). Campanha com status gravado `Cancelled` → `CampaignCancelled`.
- [x] Mesma transação: segundo voucher → `422 REDEMPTION_TRANSACTION_ALREADY_REDEEMED`; mesmo voucher repetido → `200` idêntico, `usesCount` não muda (US-07.3 cenários 4 e 5).
- [x] Provisório da própria transação: aplicável se a campanha permite uso na mesma compra, `SamePurchaseNotAllowed` se não permite (US-04.9 cenários 4 e 5); provisório de outra transação → `NotConfirmed`.
- [x] Mesma compra, campanha com `maxUses` = 1: resgate antes da confirmação → `200`, voucher segue `Provisional`; confirmar → `Redeemed`, saldo da campanha consumido. Outra concessão: resgatar e cancelar → resgate `Reversed`, voucher `Discarded`, saldo devolvido. Reserva expirada depois do resgate → o mesmo efeito do cancelamento.
- [x] Estorno de um resgate normal: `maxUses` = 1 resgatado → `reverse` → `200 Reversed`, voucher volta a `Available` com 1 uso; repetir o `reverse` → `200` igual; a mesma transação não resgata outro voucher.
- [x] 20 resgates simultâneos do último uso → exatamente um `200`, um único evento `Redeemed` (US-05.4 cenário 4).
- [x] Voucher de outro CPF ou de outra rede → `404`.
- [x] `GET /satellite/campaigns`: ativa aparece; agendada, pausada, encerrada, esgotada e rascunho não; filtro por loja e canal respeita `campaign_scopes`; loja de outra rede → `404`.
- [x] Token sem `vouchers:redeem` → `403`; token de usuário → `401`.

---

## 6. Pontos em aberto

| # | Ponto | Motivo de adiar | Gatilho |
|---|---|---|---|
| PA-1 | **Cancelamento da campanha em lote** (RN10, US-05.5 cenários 2 e 3): marcar `Cancelled` os vouchers não utilizados, com evento e auditoria, preservando os `Redeemed`. | Não existe operação de cancelar campanha (US-01.8 entregou pausar e encerrar). Aqui só se recusa o voucher cuja campanha estiver `Cancelled`. | A operação de cancelar (Onda 3 ou pedido do PO). |
| PA-2 | **Desconto item a item sobre todos os alvos da campanha** (RN10a literal, US-05.6 cenários 1 a 3). | O voucher nasce preso a um alvo (GH-045 D-10) e a cota é por alvo; recalcular sobre todos os alvos descolaria o desconto da cota consumida. | O PO confirmar que um voucher de campanha com vários alvos deve descontar itens de alvos diferentes. |
| PA-3 | **Resgate do provisório sem confirmação posterior.** Se o PDV resgata na mesma compra e nunca confirma, a reserva expira, o voucher é descartado e o resgate estornado, mas o desconto já foi dado na venda. | É o preço da D-10 na variante aprovada; a plataforma não tem como saber se a venda se concluiu sem a confirmação. | Monitorar `RedemptionReversed` por expiração; se for frequente, prazo de reserva maior ou confirmação automática no resgate. |
| PA-4 | **Contadores de resgatados e expirados na campanha** e no portal. | Saem de `vouchers` por consulta; o dashboard é a #50. | Issue #50. |
| PA-5 | **Varredura para persistir `Expired`** dos vouchers vencidos que nunca foram consultados. | O status efetivo já responde certo; a gravação acontece na tentativa de resgate (D-11). | Relatórios da #50 que leiam o status gravado. |
| PA-6 | **Limites do estorno** (D-19): janela de tempo, motivo obrigatório, quem pode estornar, e o que fazer se a validade do voucher já venceu entre o resgate e o estorno. | A operação entrou sem regra do backlog; nesta entrega é livre e idempotente. | Pedido do PO ou do time do PDV. |
| PA-7 | **Desconto fixo maior que a base** (`FixedAmount` 50 numa compra de 30 do alvo): adotado `min(valor, base)`. | Regra não escrita no backlog. | Validação com o PO. |
| PA-8 | **Público elegível na US-07.1**: devolve `audience: "All"`. | Critérios são a #47. | Issue #47. |
| PA-9 | **Idempotência por chave própria** (US-07.3 cenário 5 fala em "chave de idempotência"): usada a `transactionId`, como na concessão (PR-07). | Uma chave extra duplicaria o papel da transação. | O PDV precisar reenviar com outra transação. |
| PA-10 | ~~**PostgreSQL**~~ Resolvido em 2026-11-12: a suíte de integração (443 testes) passou num PostgreSQL local (WSL, porta 5432) com `TEST_POSTGRES_CONNECTION`. | — | — |

---

## 7. Notas da implementação

- `ConcessionRequest`, `RedemptionCheckRequest` e `RedemptionRequest` implementam `IPurchaseRequest`; o parsing e a resolução do catálogo saíram do `ConcessionService` para `PurchaseParsing`, e o mapeamento do voucher para `VoucherMapper`.
- Ordem dos motivos: o voucher já resgatado nesta compra é verificado **antes** do status, porque o próprio resgate pode tê-lo levado a `Redeemed`; a repetição do resgate devolve o original (`200`).
- `TryConsumeUseAsync` aceita `Available` ou `Provisional` com a reserva em dia e só muda o status (`Redeemed`) no `Available`; `TryConfirmVoucherAsync` passou a decidir `Available`/`Redeemed` por `uses_count >= max_uses`.
- O estorno pela varredura grava só o evento `RedemptionReversed` (sem ator); pelo desfazimento ou pela rota, grava também a auditoria `VoucherRedemptionReversed` com a aplicação.
- O estorno é sempre **por id do resgate** (`TryReverseAsync` com `WHERE id = @id AND status = 'Effective'`); ao descartar um voucher, `ReverseAllOfVoucherAsync` percorre os resgates efetivos dele. A primeira versão estornava por `voucher_id` e, num voucher multiuso, revertia todos os resgates de uma vez; o agente de testes pegou o defeito e um teste com `maxUses = 2` cobre o caso.
- O `422` de motivo traz a extensão `reason` via `BusinessRuleException.Extensions`, copiada pelo `ApiExceptionHandler`.
- Idempotência sob concorrência: o resgate relê o resgate da transação em dois pontos além da entrada, quando perde a corrida pela utilização e quando a avaliação recusa o voucher (ele pode ter sido relido já `Redeemed` por um pedido igual que gravou antes). O CI pegou esse segundo caso num dos dez pedidos simultâneos da mesma transação.
- Nos testes de integração, cada teste usa um CPF próprio: os vouchers dos outros testes da mesma fábrica não entram na verificação.
- O `pdv-demo` já tinha o escopo `vouchers:redeem` no `appsettings.Development.json`.
