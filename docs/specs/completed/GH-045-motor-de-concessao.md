# 📋 Especificação Técnica: GH-045 - Motor de concessão de vouchers (EP-04, US-07.2 e US-07.4)

> Status: Completed
> História de Usuário Relacionada: issue #45 "Motor de concessão [1P]", fatia da issue #27. Regras de origem: US-04.1 a US-04.9 (`D:\residenciaIV\docs\epico_04.md`), US-07.2 e US-07.4 (`epico_07.md`), US-06.5 só para o público "todos" (`epico_06.md`).
> Responsável: Iago V. R.
> Data: 2026-11-10
> Branch: `feat/GH-045-motor-de-concessao` (a partir de `staging`, com o PR #51 mesclado nela) → PR para `staging`
> Depende de: issue #26 (status efetivo, auditoria e US-07.5) ✅ na `staging`; **PR #51** (escopo `campaign_scopes`, pausar e encerrar), ainda aberto.
> Fora desta spec: verificação de aplicáveis e resgate (issue #49), público restrito (#47), cadastro de aplicações, trava no banco e limite de tentativas (#48), acompanhamento e exportação (#50), US-07.1 (campanhas vigentes por API).

---

## 1. Contexto & Regras de Negócio

**Objetivo:** com o carrinho fechado e antes do pagamento, a aplicação satélite envia a compra e o cliente; o motor decide se alguma campanha ativa foi satisfeita, reserva o saldo e devolve um voucher **provisório**. Depois do pagamento, o satélite **confirma** (o voucher vira disponível e o saldo é consumido) ou **desfaz** (o voucher é descartado e o saldo volta). É o Marco 1 do projeto: campanha aprovada e ativa → satélite recebe voucher.

### 1.1 Regras do backlog aplicadas

- **Duas fases (PR-06, RF14c, US-04.9, decisão D3):** o voucher nasce provisório e reserva o saldo da campanha e do alvo; a confirmação o torna disponível; o desfazimento ou a expiração do prazo de reserva (PR-17) o descarta e devolve o saldo (RN09b). O saldo reservado conta para o esgotamento (US-04.9, cenário 6).
- **Idempotência (PR-07, US-04.6, US-07.2 cenário 6):** a requisição carrega o identificador da transação; a repetição devolve o resultado original, sem nova emissão nem novo consumo de saldo. Confirmar e desfazer também são idempotentes (RNF01).
- **Uma compra, um voucher (PR-09, US-04.1 cenário 5, decisão D1):** se mais de uma campanha for satisfeita, vence a de maior benefício naquela compra; no empate, a que termina primeiro; se ainda empatar, a aprovada primeiro.
- **Condições (RF14a, US-04.1):** cumulativas, avaliadas sobre o valor bruto (PR-16). Os tipos configuráveis hoje são `PurchaseTotal`, `ItemQuantity`, `Category` e `Product` (US-01.4). A decisão D2 (o satélite envia a quantidade de compras do cliente) libera `CustomerPurchaseCount`.
- **Escopo (RN08, US-02.3):** só concede campanha cujo escopo contempla a loja e o canal da compra. Loja inativada não encerra a campanha: a concessão naquela loja é recusada até a loja voltar (D7).
- **Status (PR-10, RN04, US-04.4):** só a campanha **efetivamente ativa** concede (`Campaign.CanIssueVouchers`, PR-04). Rascunho, em aprovação, agendada, pausada, encerrada, cancelada e esgotada não concedem. Bloquear a emissão não afeta vouchers já emitidos (RN07a).
- **Saldo (RN09, RN09a, RF06c, US-04.5):** total da campanha, cota por alvo e limite por cliente. A contagem é atômica sob concorrência (RNF02). Atingido o total, a campanha fica **esgotada**; atingida a cota de um alvo, só aquele alvo bloqueia.
- **Validade (RF18a, US-04.3):** calculada na emissão: prazo relativo (dias ou horas) com teto opcional, ou data fixa (US-01.12). Vigência às 23:59 vale até 23:59:59, no fuso da rede (D9).
- **Elegibilidade (RN11, RN11a, US-04.2, US-06.5):** avaliada depois das condições; o público "todos" dispensa critérios. O CPF identifica o cliente (D4).
- **Histórico (RNF05, US-04.7) e auditoria (RF27a, US-07.5 cenário 1):** emissão, confirmação, desfazimento e expiração ficam no histórico imutável do voucher; as ações da aplicação satélite entram na trilha de auditoria com o id da aplicação. As recusas são registradas com o motivo.
- **Disponibilizar o voucher (RF35, US-04.8, US-07.4):** a plataforma não notifica o cliente; o satélite consulta os vouchers dele pela API.
- **Não obstrução da venda (RNF04, US-07.2 cenário 2):** não haver concessão não é erro.

### 1.2 Fluxo da concessão (ordem de avaliação)

1. Contrato (`400`): transação, cliente, loja, canal, valor total e itens.
2. Loja da rede da aplicação, ativa e que opera o canal (`422`, decisão D-13). Nada é gravado.
3. Transação já conhecida na rede → devolve a concessão original (D-3).
4. **Candidatas:** campanhas aprovadas da rede (status gravado diferente de `Draft`, `PendingApproval` e `Rejected`) cujo escopo contempla a loja e o canal (D-14).
5. **Satisfeitas:** candidatas cujas condições são todas atendidas pela compra (D-11).
6. **Aptas:** satisfeitas que estão efetivamente ativas (não `SoldOut`, D-5), cujo cliente não atingiu o limite por cliente (D-15) e cujo alvo escolhido (D-10) ainda tem cota.
7. **Escolhida:** a primeira apta pela ordem D1 (D-9).
8. Reserva atômica do saldo da campanha e do alvo (D-4); se a reserva falhar por corrida, a campanha vira bloqueada por esgotamento e a avaliação segue para a próxima apta.
9. Voucher provisório, evento `Issued`, auditoria `VoucherIssued` e concessão `Provisional`, no mesmo `SaveChanges`.
10. Sem apta: concessão `NotGranted` com o motivo da satisfeita melhor colocada na ordem D1 (`CampaignNotActive`, `CampaignSoldOut`, `TargetSoldOut`, `CustomerLimitReached`) ou `NoCampaignMatched`.

Antes do passo 4, as reservas vencidas das campanhas da rede são liberadas (D-6), para que o saldo delas conte.

### 1.3 Base reutilizada (já na `staging` ou no PR #51)

`Campaign.EffectiveStatus` e `CanIssueVouchers` (GH-026), `campaign_conditions` e `allowUseInSamePurchase` (US-01.4), validade e restrições (US-01.12), itens e cotas (US-01.2 e 01.3), `campaign_scopes` (US-02.3, PR #51), `IAuditTrail` e `AuditActorType.Application` (GH-026), esquema `Satellite`, policy `SatelliteIssueVouchers`, `ICurrentSatelliteApp` e `[ValidateTenantScope]` (US-07.5), `NetworkTime` (US-02.6) e `TimeProvider`.

### 1.4 Decisões e divergências (aprovar antes do código)

| # | Ponto | Proposta |
|---|---|---|
| D-1 | Recorte. | Esta spec cobre a issue **#45**: EP-04 inteiro (04.2 limitado ao público "todos" e ao limite por cliente), US-07.2 e US-07.4. A issue #27 continua como guarda-chuva; o resgate (EP-05, US-07.3) é a issue #49, que já pode ter spec escrita sobre o `Voucher` daqui. |
| D-2 | Modelo. | Três entidades em `Domain/Vouchers`: **`Concession`** (o pedido e a decisão: chave de idempotência, dados da compra, status e motivo), **`Voucher`** e **`VoucherEvent`** (histórico, `IAppendOnly`). Substituem `Voucher` e `VoucherEvent` do #36 (`Domain/Entities`, tabelas `Vouchers` e `VoucherEvents`, vazias), como a GH-026 fez com `AuditEntry`. `EligibilityCriterion` e a tabela `EligibilityCriteria` ficam para a #47. |
| D-3 | Idempotência. | Chave = rede da aplicação + `transactionId` (índice único). **Toda** chamada avaliada grava uma concessão, inclusive `NotGranted`: a repetição devolve a mesma concessão no estado atual, sem reavaliar e sem reservar de novo. O payload da repetição não é comparado (PA-4). |
| D-4 | Saldo e concorrência (RNF02). | Contadores `issued_vouchers` e `reserved_vouchers` em `campaigns` e em `campaign_items`. A reserva é um só comando: `UPDATE campaigns SET reserved_vouchers = reserved_vouchers + 1 WHERE id = @id AND (total_voucher_limit IS NULL OR issued_vouchers + reserved_vouchers < total_voucher_limit)` (`ExecuteUpdateAsync`, igual em SQLite e PostgreSQL); zero linhas = esgotada. O mesmo para a cota do alvo. Confirmar, desfazer e expirar também são `UPDATE` condicionais pelo status atual, para dois pedidos simultâneos não consumirem ou devolverem duas vezes. Tudo numa transação por requisição. |
| D-5 | Esgotada é derivada (RN09, GH-026 PA-1, `epico_01.md`: "esgotada é status derivado"). | `EffectiveStatus` devolve `SoldOut` quando a campanha está efetivamente ativa e `issued + reserved >= total`. Nada é gravado em `status`: se uma reserva expira, a campanha volta a conceder sozinha. `GET /campaigns` e `CanIssueVouchers` passam a refletir isso. |
| D-6 | Prazo de reserva (PR-17). | `Vouchers:ReservationMinutes`, padrão **15**, de 1 a 1440, validado no startup. A expiração não tem job dedicado: as reservas vencidas são liberadas (a) no início de cada concessão, para as campanhas da rede; (b) ao confirmar ou desfazer o próprio voucher, que então responde `422 CONCESSION_RESERVATION_EXPIRED`; (c) por um `HostedService` a cada `Vouchers:ReservationSweepSeconds` (padrão 60; `0` desliga; desligado no `ApiFactory`), que usa o mesmo serviço. Na leitura, o status do voucher é o efetivo: provisório com reserva vencida aparece `Discarded`; disponível com validade vencida aparece `Expired`. |
| D-7 | D9 na vigência e na validade. | "Fim às 23:59 vale até 23:59:59": `Campaign.EffectiveStatus`, `Approve` e `CanIssueVouchers` passam a contar o minuto do fim inteiro (`utcNow < EndsAt` truncado ao minuto + 1 minuto). **Altera a D-4 da spec GH-026-status-efetivo** (hoje às 23:59:00 a campanha já está encerrada) e os testes que afirmam `Ended` no instante exato do fim. A validade por data fixa e o teto são gravados no último segundo do minuto (`23:59:59`); o prazo relativo é o instante exato (01/10 14:30 + 30 dias = 31/10 14:30). A comparação é inclusiva (`utcNow <= valid_until`). |
| D-8 | Cálculo da validade (US-04.3). | `Relative`: emissão + `amount` em `Days` ou `Hours`; com teto, o menor dos dois. `FixedDate`: `validUntil` da campanha. Tudo em UTC; a resposta devolve com o offset da rede. |
| D-9 | Desempate D1. | Benefício estimado **nesta compra**: `FixedAmount` = valor; `Percentage` = percentual sobre a base, onde a base é o valor dos itens da compra que casam o alvo (alvo `All` = valor total; alvo ausente na compra = 0). Ordem: maior benefício → menor `EndsAt` → menor `DecidedAt` → menor `Id`. Determinística: a mesma compra repetida escolhe a mesma campanha. |
| D-10 | Alvo do voucher (US-04.3: "vinculado ao alvo que originou"). | O alvo mais específico presente na compra (`Product` antes de `Category`); sem nenhum, o alvo `All`; sem `All`, o primeiro alvo configurado (condição e alvo são independentes, US-01.4 D-11). Cota do alvo esgotada → recusa `TargetSoldOut`, sem cair para outro alvo (US-04.5, cenário 2). |
| D-11 | Avaliação das condições. | `PurchaseTotal`: `totalAmount >= minAmount`. `ItemQuantity`: soma de `quantity` `>= minQuantity`. `Category` / `Product`: algum item cujo `productCode` resolve ao produto ou a um produto da categoria. **`CustomerPurchaseCount`** (D2): `customerPurchaseCount` do pedido `>= minQuantity`; ausente → não satisfeita. O tipo passa a ser aceito no `PUT /conditions` (US-01.4), com `minQuantity > 0` e o `CHECK` ajustado. `ProductCombination` e `PurchasePeriod` continuam indisponíveis (PA-11). |
| D-12 | Itens da compra. | Lista obrigatória (1 a 500) com `productCode` (código do catálogo, `products.code`), `quantity` (`>= 1`) e `unitPrice` (`>= 0`, 2 casas). Código desconhecido conta no total e na quantidade, mas não casa categoria nem produto. `totalAmount` é o informado (bruto, PR-16), sem conferir com a soma dos itens. |
| D-13 | Loja e canal. | `establishmentId` (Guid) e `channel` (`Physical` ou `Virtual`). Loja inexistente ou de outra rede → `422 ISSUANCE_ESTABLISHMENT_NOT_FOUND`; inativa → `422 ISSUANCE_ESTABLISHMENT_INACTIVE` (D7: a campanha não para); canal que a loja não opera (PR-11) → `422 ISSUANCE_CHANNEL_NOT_ENABLED`. Esses `422` não gravam concessão. |
| D-14 | Escopo (RN08). | A campanha vale na loja e no canal se alguma linha de `campaign_scopes` casa: rede inteira (região e loja nulas), a região da loja, ou a própria loja, sempre com o canal da compra. Depende do PR #51. |
| D-15 | Elegibilidade (US-04.2, US-06.5). | Público `AllCustomers` → elegível, e o evento de emissão grava o snapshot `{"audienceType":"AllCustomers"}` (RN11a). O público `Restricted` ainda não pode ser configurado (`422` na US-01.5), então os critérios do EP-06 ficam para a #47. Limite por cliente (RF06c): contam os vouchers do cliente na campanha que não foram descartados. |
| D-16 | Cliente (D4). | `customer.document` = CPF: aceita máscara, grava só os 11 dígitos; outro tamanho → `400`. Sem validação de dígito verificador e sem pseudonimização (PA-7). |
| D-17 | Código do voucher (US-04.3, cenário 4). | 12 caracteres aleatórios em Crockford base32 (sem I, L, O e U), índice único. Colisão em 60 bits é desprezível; se ocorrer, a violação do índice vira `500` (PA-10). |
| D-18 | Rotas. | Em `/api/v1/satellite`, policy `SatelliteIssueVouchers` (escopo `vouchers:issue`, US-07.5 D-5), numa controller própria (`SatelliteConcessionsController`) para não conflitar com as rotas de administração da #48. |
| D-19 | Não concessão. | `200` com `status: "NotGranted"` e `reason` (US-07.2, cenário 2: não é erro; a venda segue). |
| D-20 | Auditoria e histórico. | `audit_entries`: `VoucherIssued`, `VoucherConfirmed` e `VoucherCancelled` com `ActorType = Application` e `ActorId` = id da aplicação; `VoucherIssuanceRefused` só quando alguma campanha satisfeita foi bloqueada (status, saldo ou limite), para não auditar toda compra sem campanha. `voucher_events` (`IAppendOnly`): `Issued`, `Confirmed`, `Cancelled`, `ReservationExpired`. A expiração não tem ator e fica só no histórico. |
| D-21 | `CampaignResponse`. | `issuedVouchers` passa a ser real (confirmados) e entra `reservedVouchers`. Sem outra mudança de contrato no portal. |
| D-22 | Migração `VoucherConcessions`. | Apaga `Vouchers` e `VoucherEvents` (#36); cria `concessions`, `vouchers` e `voucher_events`; contadores em `campaigns` e `campaign_items`; `CHECK` de `campaign_conditions` com `CustomerPurchaseCount`. Gerada depois do PR #51 entrar, para ficar após `CampaignScopeIds`. |
| D-23 | CI na `staging`. | O workflow só roda em `main` e `feat/EP-*`: os PRs para `staging` (#44, #51, #52) não passaram pelo CI. **Decidido (2026-11-10):** `staging` entra em `push` e `pull_request` do `standard.yml` neste PR. |
| D-24 | Aceite das decisões D1, D2, D4, D7 e D9. | **Decidido (2026-11-10):** a issue #45 (2026-11-09) vale como o aceite. O `ROADMAP.md` (2.4 e 2.5) passa a citá-la como fonte, e a D3 deixa de estar "à espera do aceite". |

---

## 2. Contratos & Schemas (DTOs / Records)

### 2.1 Domínio (`Domain/Vouchers`)

```csharp
public enum ConcessionStatus { NotGranted, Provisional, Confirmed, Cancelled, Expired }
public enum ConcessionRefusal { NoCampaignMatched, CampaignNotActive, CampaignSoldOut, TargetSoldOut, CustomerLimitReached }
public enum VoucherStatus { Provisional, Available, Redeemed, Expired, Cancelled, Discarded }   // Redeemed, Expired e Cancelled: EP-05

public static class VoucherEventTypes
{
    public const string Issued = "Issued";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
    public const string ReservationExpired = "ReservationExpired";
}

/// Pedido de concessão e a decisão tomada; chave de idempotência (rede + transação).
public class Concession
{
    public Guid Id { get; set; }                       // v7 do instante
    public Guid NetworkId { get; set; }
    public Guid SatelliteAppId { get; set; }
    public string TransactionId { get; set; }          // 100, único por rede
    public string CustomerDocument { get; set; }       // 11 dígitos
    public Guid EstablishmentId { get; set; }
    public ChannelType Channel { get; set; }
    public decimal TotalAmount { get; set; }           // decimal(10,2)
    public int ItemCount { get; set; }
    public int? CustomerPurchaseCount { get; set; }
    public DateTime RequestedAt { get; set; }          // UTC
    public ConcessionStatus Status { get; set; }
    public ConcessionRefusal? Refusal { get; set; }
    public Guid? RefusedCampaignId { get; set; }       // campanha satisfeita que foi bloqueada
    public Guid? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public DateTime? ReservedUntil { get; set; }
    public DateTime? DecidedAt { get; set; }           // confirmação, desfazimento ou expiração
}

public class Voucher
{
    public Guid Id { get; set; }
    public string Code { get; set; }                   // 12, único
    public Guid NetworkId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid CampaignItemId { get; set; }           // alvo que originou (D-10); FK Restrict
    public Guid ConcessionId { get; set; }
    public string CustomerDocument { get; set; }
    public VoucherStatus Status { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ValidUntil { get; set; }           // inclusivo (D-7)
    public DateTime? ReservedUntil { get; set; }
    public int MaxUses { get; set; }                   // MaxUsesPerVoucher da campanha na emissão
    public int UsesCount { get; set; }
    public Guid EstablishmentId { get; set; }
    public ChannelType Channel { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? DiscardedAt { get; set; }

    public VoucherStatus EffectiveStatus(DateTime utcNow);   // Provisional vencido = Discarded; Available vencido = Expired
}

public class VoucherEvent : IAppendOnly
{
    public Guid Id { get; set; }
    public Guid VoucherId { get; set; }
    public string EventType { get; set; }              // VoucherEventTypes, 30
    public DateTime OccurredAt { get; set; }
    public AuditActorType? ActorType { get; set; }     // null na expiração
    public Guid? ActorId { get; set; }
    public string? Payload { get; set; }               // JSON curto, 2000
}
```

Mudanças em `Campaign` e `CampaignItem`:

```csharp
// Campaign
public int IssuedVouchers { get; set; }       // confirmados
public int ReservedVouchers { get; set; }     // provisórios vivos
public bool HasAvailableVouchers => TotalVoucherLimit is null || IssuedVouchers + ReservedVouchers < TotalVoucherLimit;
public CampaignStatus EffectiveStatus(DateTime utcNow);   // + SoldOut derivado (D-5) e fim inclusivo (D-7)
public bool IsInScope(Establishment establishment, ChannelType channel);   // D-14
public CampaignItem ChooseTarget(Purchase purchase);                         // D-10
public bool IsSatisfiedBy(Purchase purchase);                                // D-11

// CampaignItem
public int IssuedVouchers { get; set; }
public int ReservedVouchers { get; set; }
public bool HasQuota => VoucherLimit is null || IssuedVouchers + ReservedVouchers < VoucherLimit;
```

Serviços de domínio, sem I/O (testáveis em unidade):

```csharp
// Compra já resolvida contra o catálogo: cada item sabe o produto e a categoria, ou nada.
public record Purchase(decimal TotalAmount, IReadOnlyList<PurchaseItem> Items, int? CustomerPurchaseCount);
public record PurchaseItem(string ProductCode, int Quantity, decimal UnitPrice, Guid? ProductId, Guid? CategoryId);

public static class ConcessionEvaluator
{
    /// Aplica 1.2 (passos 4 a 7) sobre as candidatas já carregadas; devolve a escolhida ou o motivo.
    public static EvaluationResult Evaluate(IReadOnlyList<Campaign> candidates, Purchase purchase, int[] customerVouchersByCampaign, DateTime utcNow);
    public static decimal EstimatedBenefit(CampaignItem target, Purchase purchase);   // D-9
}

public static class VoucherValidity
{
    public static DateTime Compute(Campaign campaign, DateTime issuedAtUtc);   // D-8
}
```

### 2.2 Aplicação (`Application/Vouchers`)

```csharp
public record ConcessionRequest(
    [Required, StringLength(100)] string? TransactionId,
    [Required] ConcessionCustomerRequest? Customer,
    [Required] Guid? EstablishmentId,
    [Required] string? Channel,                                   // Physical | Virtual
    [Required] decimal? TotalAmount,                              // > 0, até 2 casas
    [Required, MinLength(1), MaxLength(500)] IReadOnlyList<PurchaseItemRequest?>? Items,
    [Range(0, int.MaxValue)] int? CustomerPurchaseCount);         // D2

public record ConcessionCustomerRequest([Required] string? Document);   // CPF

public record PurchaseItemRequest(
    [Required, StringLength(50)] string? ProductCode,
    [Required, Range(1, 100_000)] int? Quantity,
    [Required] decimal? UnitPrice);                               // >= 0, até 2 casas

public record ConcessionResponse(
    Guid ConcessionId,
    string TransactionId,
    string Status,                 // NotGranted | Provisional | Confirmed | Cancelled | Expired
    string? Reason,                // ConcessionRefusal, só no NotGranted
    DateTimeOffset? ReservedUntil, // só no Provisional
    VoucherResponse? Voucher);

public record VoucherResponse(
    Guid Id,
    string Code,
    Guid CampaignId,
    string CampaignName,
    string Status,                 // efetivo
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    int MaxUses,
    int RemainingUses,
    bool AllowUseInSamePurchase,
    VoucherTargetResponse Target,
    Guid ConcessionId,
    string TransactionId);

public record VoucherTargetResponse(string TargetType, Guid? CategoryId, Guid? ProductId, string BenefitType, decimal BenefitValue);

public interface IConcessionService
{
    Task<ConcessionResponse> GrantAsync(ConcessionRequest request, CancellationToken cancellationToken);
    Task<ConcessionResponse> ConfirmAsync(Guid concessionId, CancellationToken cancellationToken);
    Task<ConcessionResponse> CancelAsync(Guid concessionId, CancellationToken cancellationToken);
    Task<ConcessionResponse> GetAsync(Guid concessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VoucherResponse>> GetCustomerVouchersAsync(string document, string? status, CancellationToken cancellationToken);
}

/// Libera as reservas vencidas (D-6): UPDATE condicional por voucher e devolução dos contadores.
public interface IReservationReleaser
{
    Task<int> ReleaseExpiredAsync(Guid? networkId, DateTime utcNow, CancellationToken cancellationToken);
}
```

Configuração (`Vouchers`), validada no startup como as demais:

| Chave | Padrão | Regra |
|---|---|---|
| `Vouchers:ReservationMinutes` | 15 | 1 a 1440 |
| `Vouchers:ReservationSweepSeconds` | 60 | 0 (desligado) a 3600 |

### 2.3 Mensagens e códigos

| Constante / código | Status | Mensagem |
|---|---|---|
| `TransactionIdRequired` | 400 | "Informe o identificador da transação." |
| `CustomerDocumentRequired` / `CustomerDocumentInvalid` | 400 | "Informe o CPF do cliente." / "O CPF deve ter 11 dígitos." |
| `EstablishmentRequired` | 400 | "Informe a loja da compra." |
| `ChannelRequired` / `ChannelInvalid` | 400 | "Informe o canal da compra." / "Canal inválido. Use Physical ou Virtual." |
| `TotalAmountRequired` / `TotalAmountPositive` / `TotalAmountScale` | 400 | "Informe o valor total da compra." / "O valor total da compra deve ser maior que zero." / "O valor total da compra deve ter no máximo 2 casas decimais." |
| `ItemsRequired` / `ItemsTooMany` | 400 | "Informe os itens da compra." / "A compra pode ter no máximo 500 itens." |
| `ItemProductCodeRequired` / `ItemQuantityRange` / `ItemUnitPriceInvalid` | 400 | "Informe o código do produto." / "A quantidade do item deve ser de 1 a 100.000." / "O preço unitário não pode ser negativo e deve ter no máximo 2 casas decimais." |
| `VoucherStatusInvalid` | 400 | "Status inválido. Use Provisional, Available, Redeemed, Expired ou Cancelled." |
| `ISSUANCE_ESTABLISHMENT_NOT_FOUND` | 422 | "A loja informada não existe nesta rede." |
| `ISSUANCE_ESTABLISHMENT_INACTIVE` | 422 | "A loja informada está inativa; a concessão não é avaliada até a loja voltar." |
| `ISSUANCE_CHANNEL_NOT_ENABLED` | 422 | "A loja {código} não opera o canal {canal}." |
| `CONCESSION_NOT_GRANTED` | 422 | "Esta concessão não gerou voucher." (confirmar ou desfazer uma `NotGranted`) |
| `CONCESSION_RESERVATION_EXPIRED` | 422 | "O prazo de reserva desta concessão expirou e o voucher foi descartado." |
| `CONCESSION_ALREADY_CANCELLED` | 422 | "Esta concessão já foi desfeita." (confirmar depois de desfazer) |
| `CONCESSION_ALREADY_CONFIRMED` | 422 | "Esta concessão já foi confirmada." (desfazer depois de confirmar) |

Motivos de não concessão (`reason`): `NoCampaignMatched`, `CampaignNotActive`, `CampaignSoldOut`, `TargetSoldOut`, `CustomerLimitReached`.

---

## 3. Especificação dos Endpoints

Todas as rotas: policy `SatelliteIssueVouchers`; `401 AUTH_UNAUTHENTICATED` sem token ou com token de usuário; `403 AUTH_FORBIDDEN_SCOPE` sem o escopo. A rede é sempre a da aplicação (`ICurrentSatelliteApp.NetworkId`): nada é informado pelo satélite.

### `POST /api/v1/satellite/concessions`

- Corpo: `ConcessionRequest`.
- Ordem: contrato (`400`) → loja e canal (`422`) → idempotência → fluxo 1.2.
- `200`: `ConcessionResponse`. Concedido: `status = "Provisional"`, `reservedUntil` e `voucher` (status `Provisional`). Não concedido: `status = "NotGranted"` e `reason`. Repetição: a concessão original no estado atual (`Provisional`, `Confirmed`, `Cancelled`, `Expired` ou `NotGranted`).

### `POST /api/v1/satellite/concessions/{id:guid}/confirm`

- `200`: `Provisional` → `Confirmed`, voucher `Available`, saldo consumido (`reserved - 1`, `issued + 1`). Já confirmada → `200` igual (idempotente).
- `404 NOT_FOUND`: inexistente ou de outra rede.
- `422`: `CONCESSION_NOT_GRANTED`, `CONCESSION_RESERVATION_EXPIRED` (a reserva é liberada na hora, se ainda não foi), `CONCESSION_ALREADY_CANCELLED`.

### `POST /api/v1/satellite/concessions/{id:guid}/cancel`

- `200`: `Provisional` → `Cancelled`, voucher `Discarded`, saldo devolvido. Já desfeita ou já expirada → `200` com o estado atual (nada a devolver).
- `404`; `422`: `CONCESSION_NOT_GRANTED`, `CONCESSION_ALREADY_CONFIRMED`.

### `GET /api/v1/satellite/concessions/{id:guid}`

- `200`: estado atual; `404`.

### `GET /api/v1/satellite/customers/{document}/vouchers?status=`

- US-07.4 e US-04.8. `document` com 11 dígitos (máscara aceita) → senão `400`.
- `200`: vouchers do cliente na rede da aplicação, dos mais recentes para os mais antigos, com o status efetivo. Sem filtro, não traz `Discarded`. `status` filtra pelo efetivo (`Provisional`, `Available`, `Redeemed`, `Expired`, `Cancelled`); valor desconhecido → `400`. Cliente sem voucher → `200` com lista vazia.

### Portal (sem rota nova)

- `GET /api/v1/campaigns` e `GET /api/v1/campaigns/{id}`: `status` pode vir `SoldOut` (D-5); `issuedVouchers` real e `reservedVouchers` (D-21).
- `PUT /api/v1/campaigns/{id}/conditions`: aceita `CustomerPurchaseCount` com `minQuantity` (D-11).

### Migração `VoucherConcessions` (nos dois conjuntos)

- `DROP TABLE Vouchers, VoucherEvents` (#36, vazias).
- `concessions`: `id`, `network_id`, `satellite_app_id`, `transaction_id varchar(100)`, `customer_document varchar(11)`, `establishment_id` (FK Restrict), `channel varchar(20)`, `total_amount decimal(10,2)`, `item_count`, `customer_purchase_count`, `requested_at`, `status varchar(20)`, `refusal varchar(30)`, `refused_campaign_id`, `voucher_id`, `reserved_until`, `decided_at`. Único `(network_id, transaction_id)`; índice `(network_id, customer_document, requested_at)`.
- `vouchers`: `id`, `code varchar(12)` único, `network_id`, `campaign_id` (FK Restrict), `campaign_item_id` (FK Restrict), `concession_id` (FK Restrict), `customer_document varchar(11)`, `status varchar(20)`, `issued_at`, `valid_until`, `reserved_until`, `max_uses`, `uses_count`, `establishment_id`, `channel varchar(20)`, `confirmed_at`, `discarded_at`. Índices `(campaign_id, status)`, `(network_id, customer_document, issued_at)`, `(status, reserved_until)` para a liberação. `CHECK uses_count >= 0 AND uses_count <= max_uses`.
- `voucher_events`: `id`, `voucher_id` (FK Cascade), `event_type varchar(30)`, `occurred_at`, `actor_type varchar(20)`, `actor_id`, `payload varchar(2000)`; índice `(voucher_id, occurred_at)`.
- `campaigns.issued_vouchers`, `campaigns.reserved_vouchers`, `campaign_items.issued_vouchers`, `campaign_items.reserved_vouchers`: `integer NOT NULL DEFAULT 0`, `CHECK >= 0`.
- `ck_campaign_conditions_parameters` ganha `(type = 'CustomerPurchaseCount' AND min_quantity IS NOT NULL AND min_quantity > 0 AND min_amount IS NULL AND category_id IS NULL AND product_id IS NULL)`.

---

## 4. Checklist de Implementação

- [x] 1. Enums, `Concession`, `Voucher`, `VoucherEvent` e `VoucherEventTypes` em `Domain/Vouchers`; `AuditEventTypes` ganha `VoucherIssued`, `VoucherConfirmed`, `VoucherCancelled` e `VoucherIssuanceRefused`; remoção de `Domain/Entities/Voucher.cs`, `VoucherEvent.cs`, `Enums/VoucherStatus.cs` e `Enums/VoucherEventType.cs`.
- [x] 2. `Campaign`: contadores, `EffectiveStatus` com `SoldOut` (D-5) e fim inclusivo (D-7, também em `Approve`), `IsInScope`, `IsSatisfiedBy`, `ChooseTarget`; `CustomerPurchaseCount` habilitado em `ReplaceConditions`; `CampaignItem` com contadores. `VoucherValidity`, `ConcessionEvaluator` e `Purchase`.
- [x] 3. Contratos, `ConcessionContract` (mensagens), `IConcessionService`, `IConcessionRepository`, `IReservationReleaser`, `VoucherOptions` (`Vouchers:*`) validado no startup.
- [x] 4. `ConcessionService`: fluxo 1.2 com transação, `ExecuteUpdateAsync` condicional para reservar, confirmar, desfazer e liberar; catálogo por `productCode`; auditoria e eventos; `GetCustomerVouchersAsync`. `ReservationReleaser` e o `HostedService` do sweep.
- [x] 5. Configurações EF (`concessions`, `vouchers`, `voucher_events`, contadores, `CHECK`), `DbSet`s, `AppDbContext` sem `Domain.Entities` de voucher, migração `VoucherConcessions` via `scripts/add-migration.sh` (depois do PR #51).
- [x] 6. `SatelliteConcessionsController` (5 rotas) e `CampaignService.Map` com `SoldOut`, `issuedVouchers` e `reservedVouchers`.
- [x] 7. Testes unitários (`ConcessionEvaluatorTests`, `VoucherValidityTests`, `CampaignSoldOutTests`, D9) e de integração (`SatelliteConcessionsEndpointTests`, `ConcessionConcurrencyTests`, `CustomerVouchersEndpointTests`, auditoria e append-only).
- [x] 8. `appsettings.json` (`Vouchers`), `.env.example`, `README.md` (fluxo do integrador), `agents.md` (skill do motor), `ROADMAP.md` (2.4/2.5: D1, D2, D4, D7 e D9 decididos na issue #45), `PROJECT_STATE.md`.
- [x] 9. `dotnet test` verde sem warnings (inclui `MigrationsModelTests`); smoke test com a API: token do `pdv-demo` → campanha aprovada → conceder → confirmar; em PostgreSQL se houver Docker.
- [x] 10. `staging` no `standard.yml` (D-23).

---

## 5. Casos de Teste Essenciais

**Unidade (domínio, relógio fixo):**

- [x] Esquema da US-04.1: valor total 180 / 149,99 com `PurchaseTotal 150`; 12 / 9 itens com `ItemQuantity 10`; categoria presente / ausente → satisfeita ou não. Cenários 1 e 2 (cumulativas).
- [x] `CustomerPurchaseCount 3`: pedido com 3 → satisfeita; com 2 ou sem o campo → não.
- [x] D-9: duas campanhas satisfeitas, 10% sobre R$ 200 (20) contra R$ 15 fixo → vence a de 20; empate → menor `EndsAt`; empate → menor `DecidedAt`; repetição escolhe a mesma.
- [x] D-10: compra com produto do alvo `Product` e da categoria do alvo `Category` → `Product`; sem nenhum → `All`; sem `All` → primeiro alvo.
- [x] US-04.3: 30 dias a partir de 01/10/2026 14:30 → 31/10/2026 14:30; data fixa 15/11/2026 23:59 → 23:59:59; 60 dias com teto 31/10/2026 23:59 emitido em 25/10 → 31/10 23:59:59; 48 horas.
- [x] D-7: campanha com fim 31/10 23:59: às 23:59:30 ainda `Active`; às 00:00:00 de 01/11 `Ended`. `Approve` às 23:59:30 do fim ainda aprova.
- [x] D-5: total 5, `issued 3 + reserved 2` → `SoldOut`; `reserved 1` → `Active`; sem total → nunca `SoldOut`.
- [x] `Voucher.EffectiveStatus`: provisório com reserva vencida → `Discarded`; disponível com validade vencida → `Expired`; no último segundo da validade → `Available`.
- [x] Escopo: rede inteira, região da loja, a própria loja, canal diferente, loja de outra região → casa / não casa.

**Integração (`TestAuth.CreateSatelliteToken`, `ReadyCampaign` aprovada, relógio fixo dentro da vigência):**

- [x] US-07.2 cenário 1: compra que satisfaz → `200 Provisional`, voucher com código de 12 caracteres, `reservedUntil = agora + 15 min`, `validUntil` de 30 dias com o offset da rede; `campaigns.reserved_vouchers = 1`; `voucher_events` com `Issued`; `audit_entries` com `VoucherIssued` e o id da aplicação.
- [x] Cenário 4: `confirm` → `Confirmed`, voucher `Available`, `issued 1 / reserved 0`; segundo `confirm` → `200` igual.
- [x] Cenário 5: `cancel` → `Cancelled`, voucher `Discarded`, saldo devolvido; `confirm` depois → `422 CONCESSION_ALREADY_CANCELLED`. Reserva vencida (relógio adiantado 16 min) → `confirm` → `422 CONCESSION_RESERVATION_EXPIRED` e saldo devolvido; `GET` mostra `Expired`.
- [x] Cenário 6 / US-04.6: mesma `transactionId` duas vezes → mesmo `concessionId` e código, `reserved_vouchers` continua 1; transações distintas → dois vouchers.
- [x] Cenário 2: compra que não satisfaz nenhuma → `200 NotGranted NoCampaignMatched`, sem linha em `vouchers` nem em `audit_entries`; a repetição devolve o mesmo `concessionId`.
- [x] US-04.4 (esquema): `Draft`, `PendingApproval`, `Scheduled` (antes do início), `Paused`, `Ended`, `Rejected` → `NotGranted CampaignNotActive`; agendada com vigência começada → concede. Cenário 1: voucher emitido sobrevive ao encerramento (`GET` continua `Available`).
- [x] US-04.5: total 1 → segunda compra `CampaignSoldOut` e `GET /campaigns/{id}` com `status = SoldOut`; cota do alvo esgotada com saldo no outro → `TargetSoldOut`, contadores do outro alvo intactos; limite por cliente 2 → terceira compra do mesmo CPF `CustomerLimitReached`, outro CPF concede.
- [x] Cenário 3 (concorrência): 20 requisições simultâneas, total 1 → exatamente uma `Provisional`, 19 `CampaignSoldOut`, `issued + reserved = 1` (SQLite e, com `TEST_POSTGRES_CONNECTION`, PostgreSQL).
- [x] Liberação ao conceder: reserva vencida de outra transação libera o saldo e a nova compra concede; `voucher_events` da antiga com `ReservationExpired`.
- [x] Escopo: campanha só da Loja Centro → compra na Loja Shopping `NotGranted NoCampaignMatched`; canal virtual na loja física → `422 ISSUANCE_CHANNEL_NOT_ENABLED`; loja inativada → `422 ISSUANCE_ESTABLISHMENT_INACTIVE`; loja de outra rede ou inexistente → `422 ISSUANCE_ESTABLISHMENT_NOT_FOUND`.
- [x] Contrato: sem transação, CPF com 10 dígitos, canal inválido, total 0, itens vazios, quantidade 0 → `400` com o campo.
- [x] Segurança: sem token → `401`; token de usuário → `401`; aplicação sem `vouchers:issue` → `403 AUTH_FORBIDDEN_SCOPE`; concessão de outra rede → `404`.
- [x] US-07.4: `GET /customers/{cpf}/vouchers` lista provisório e disponível com `remainingUses`; `?status=Available` filtra; CPF sem voucher → `[]`; CPF inválido → `400`; o voucher descartado só aparece com `?status=`... (não aparece: `Discarded` não é filtro).
- [x] Append-only: alterar um `voucher_events` pelo `AppDbContext` lança e o registro fica igual.
- [x] `PUT /conditions` com `CustomerPurchaseCount` `minQuantity 3` → `200`; `minQuantity 0` → `422`.
- [x] Suíte anterior: testes de status efetivo e ciclo ajustados ao fim inclusivo (D-7).

---

### Notas da implementação

- `concessions` não tem `voucher_id`: a relação 1:1 fica em `vouchers.concession_id` (índice único), e `Concession.Voucher` é a navegação. Evita a referência circular na gravação.
- A cota do alvo é reservada sempre (`campaign_items.reserved_vouchers`/`issued_vouchers`), mesmo sem `voucher_limit`: os contadores por alvo ficam completos para o EP-03.
- A migração `VoucherConcessions` gerada pelo EF renomeava `Vouchers` para `vouchers` e alterava colunas; foi reescrita à mão para apagar `Vouchers` e `VoucherEvents` (vazias) e criar `vouchers` do zero, nos dois conjuntos (SQLite `20261110141616`, PostgreSQL `20261110141622`), como a GH-023 fez com a PK repetida.
- `valid_until` guarda o último instante válido (inclusivo); a comparação trunca o "agora" ao segundo. A data fixa e o teto entram como `23:59:59`.
- A liberação das reservas vencidas (`ReservationReleaser`) fica na camada de aplicação sobre o `IConcessionRepository`; a varredura (`ReservationSweepService`) é um `BackgroundService` da Api com `PeriodicTimer` e o `TimeProvider` injetado.
- No `ConfirmAsync`/`CancelAsync`, se o comando condicional não pegar a linha (outro pedido chegou antes), a concessão é relida e reavaliada uma vez; a resposta é sempre o estado gravado.
- Os testes de integração criam uma loja por cenário (`MotorScenario`): a fábrica é compartilhada na classe e campanhas de rede inteira dos outros testes seriam candidatas na mesma compra.
- Testes: 36 unitários novos (`ConcessionEvaluatorTests`, `VoucherValidityTests`) e 41 de integração (`SatelliteConcessionsEndpointTests`, `ConcessionConcurrencyTests`, `CustomerVouchersEndpointTests`); os de status efetivo e ciclo foram ajustados ao fim inclusivo (D-7). Smoke: `scripts/smoke/motor.py`.
- PostgreSQL não foi exercitado nesta máquina (sem Docker): a migração só foi compilada e o teste de concorrência rodou em SQLite.

---

## 6. Pontos em aberto

| # | Ponto | Por que fica de fora agora | Quando / onde retomar |
|---|---|---|---|
| PA-1 | **Verificação de aplicáveis e resgate** (US-05.x, US-07.3), inclusive o uso na mesma compra (US-04.9, cenários 4 e 5). | É a issue #49. O voucher provisório só resgata depois da confirmação (D3). | Issue #49, sobre `Voucher`, `VoucherEvent` e `allowUseInSamePurchase` daqui. |
| PA-2 | **Público restrito** (US-04.2 com critérios, US-06.5 completa, snapshot RN11a completo). | `Restricted` ainda não pode ser configurado (US-01.5). `EligibilityCriterion` do #36 continua no `Domain/Entities`. | Issue #47. |
| PA-3 | ~~Aceite formal de D1, D2, D4, D7 e D9.~~ | Resolvido pela D-24: a issue #45 vale como o aceite. | — |
| PA-4 | **Mesma `transactionId` com payload diferente.** | Hoje devolve a concessão original sem comparar. Um hash do pedido permitiria `422`. | Quando os integradores pedirem; EP-07. |
| PA-5 | **Sweep com várias instâncias da API.** | O `HostedService` roda em cada instância; como a liberação é um `UPDATE` condicional por voucher, não há dupla devolução, só trabalho repetido. | Deploy (issue #28) e #48. |
| PA-6 | **Volume e retenção de `concessions` `NotGranted`.** | Toda compra avaliada grava uma linha (idempotência e US-04.7). Sem política de retenção. | #50 e deploy. |
| PA-7 | **CPF: dígito verificador e pseudonimização (LGPD).** | O backlog só diz que o satélite envia o CPF. | Decisão do cliente; se vier, hash com sal por rede. |
| PA-8 | **Loja por código em vez de Guid.** | O código da loja só é único dentro da região. O satélite precisa do Guid, que hoje só o portal lista. | US-07.1 (campanhas vigentes com lojas) ou rota de consulta para satélite. |
| PA-9 | **Limite por cliente sob concorrência.** | Duas compras simultâneas do mesmo CPF no limite podem ultrapassá-lo por 1: a contagem é lida antes da reserva. Saldo da campanha e do alvo não têm essa janela. | Se o cliente exigir, lock por cliente e campanha. |
| PA-10 | **Colisão do código do voucher.** | 60 bits aleatórios; a violação do índice único viraria `500`. | Se ocorrer, uma nova tentativa no serviço. |
| PA-11 | **Condições `ProductCombination` e `PurchasePeriod`.** | Semântica indefinida no backlog (US-01.4 PA-4). | Refinar com o PO. |
| PA-12 | **Indisponibilidade do motor (US-04.1, cenário 4).** | Não bloquear a venda é responsabilidade do satélite (timeout e seguir). A API só precisa responder rápido. | `README.md` para integradores; monitoramento no deploy. |
| PA-13 | **Cancelamento da campanha invalidando vouchers** (RN10, US-05.5 cenário 2) e **vouchers emitidos quando o total é reduzido** (RN03b). | US-01.8 (cancelar) e US-01.7 após o vigor não existem. | Onda 3. |
| PA-14 | **`reservedVouchers` e `SoldOut` no portal.** | Mudança de resposta sem rota nova. | Avisar o frontend ao abrir o PR. |
