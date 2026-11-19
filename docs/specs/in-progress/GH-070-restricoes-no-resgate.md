# 📋 Especificação Técnica: GH-070 - Restrições no resgate

> Status: In Progress
> História de Usuário Relacionada: issue #70 (ponto 8 do cliente; PA-5 da GH-066 e PA-1 da GH-068)
> Responsável: @iagovrocha
> Data: 2026-11-19

---

## 1. Contexto & Regras de Negócio

Em 2026-11-18 o cliente pediu "restrições aplicadas no consumo/resgate, numa etapa nova" (ponto 8). Em 2026-11-19 ele escolheu as restrições numa lista:

| Restrição | Resposta | Situação na `staging` (`801b6a3`) | Decisão |
|---|---|---|---|
| Só o CPF que ganhou o voucher pode usá-lo | Quer | **Já é assim.** `RedemptionRepository.FindVoucherAsync` e `GetCustomerVouchersAsync` procuram o voucher pela rede **e** pelo CPF da compra; outro CPF recebe 404 `Voucher não encontrado.` | D-1 |
| Lojas e canais de resgate diferentes dos de concessão | **Não quer** | O resgate confere o escopo da campanha (`Campaign.IsInScope`) | Nada muda |
| Produtos ou categorias de resgate diferentes do alvo da concessão | Quer | O resgate exige o alvo da concessão na compra e calcula o desconto sobre ele (GH-049, D-7) | D-2 |
| Dias da semana ou horários para o resgate | Quer | Não existe | D-3 |
| Valor mínimo de resgate diferente do mínimo de concessão | Quer | **Já é assim.** O mínimo do resgate é o `minPurchaseAmount` de `PUT /campaigns/{id}/usage-restrictions` (US-01.12); o da concessão é a condição `PurchaseTotal` de `PUT /campaigns/{id}/conditions` (US-01.4). São campos separados | D-1 |

### Fluxo do resgate depois desta spec

`RedemptionEvaluator.Refusal`, na ordem (os passos novos em negrito):

1. já resgatado nesta compra;
2. status efetivo do voucher;
3. sem utilizações;
4. campanha cancelada;
5. escopo (loja e canal);
6. **janela de resgate (D-3)**;
7. faixa de valor;
8. **alvo na compra: os alvos de resgate, quando a campanha tem algum; senão, o alvo da concessão (D-2)**;
9. compra já com outro resgate.

A janela vem antes da faixa de valor porque o operador do PDV não consegue resolver "fora do horário" mexendo no carrinho, e o motivo precisa aparecer primeiro.

### Decisões

| # | Decisão |
|---|---|
| D-1 | **CPF titular e mínimo de resgate já existem.** Nenhum código muda. Dois testes de integração prendem as regras: (a) o voucher do CPF A, resgatado com o CPF B, responde 404 e não consome a utilização; (b) campanha com condição `PurchaseTotal` 100 e `minPurchaseAmount` 300: a compra de 150 ganha o voucher, e o resgate com 150 é recusado com `REDEMPTION_AMOUNT_BELOW_MINIMUM`. |
| D-2 | **Alvos de resgate.** A campanha ganha uma lista opcional de alvos de resgate, em `PUT /api/v1/campaigns/{id}/redemption-targets`. Cada alvo é `Category` (por `categoryId`) ou `Product` (por `productId` **ou** `productCode`, como nos alvos da concessão, GH-068 D-2). São até 1.000 alvos, validados em lote no catálogo (mesmo caminho do `ReplaceItemsAsync`: códigos resolvidos numa consulta, categorias e produtos numa consulta cada). Código inexistente ou inativo responde 422 com `codes`; alvo repetido responde 422. **Lista vazia = sem alvo de resgate**, e o resgate segue o alvo da concessão como hoje. Com a lista preenchida: (a) o voucher só serve se a compra tiver ao menos um item de algum alvo de resgate (`REDEMPTION_TARGET_NOT_IN_PURCHASE`, o motivo que já existe); (b) o desconto é calculado sobre a soma dos itens que casam com algum alvo de resgate, cada item contado uma vez. O benefício (tipo e valor) continua o do alvo da concessão, que é único na campanha (GH-068 D-1). O valor fixo continua limitado a essa base (GH-049 D-7). Não há alvo `All` de resgate: para valer em qualquer item, deixe a lista vazia e use um alvo `All` na concessão. Só em rascunho, como as outras seções. |
| D-3 | **Janelas de resgate.** A campanha ganha uma lista opcional de janelas, em `PUT /api/v1/campaigns/{id}/redemption-windows`. Cada janela tem `daysOfWeek` (um ou mais de `Monday` a `Sunday`), `start` e `end` (`HH:mm`, no fuso da rede, `Network.TimeZone`). São até 14 janelas. **Lista vazia = qualquer dia e hora**, como hoje. A janela vale de `start` até o fim do minuto de `end`, inclusive (o mesmo critério do fim da vigência, D9): `08:00`–`11:59` aceita 11:59:59. `start` precisa ser menor ou igual a `end`; janela que vira a noite é cadastrada em duas (`22:00`–`23:59` e `00:00`–`01:59` do dia seguinte). O instante avaliado é o "agora" do servidor (`TimeProvider`) convertido para o fuso da rede, e não um horário informado pelo PDV, que a compra não traz. Fora de todas as janelas: novo motivo `OutsideRedemptionWindow` → 422 `REDEMPTION_OUTSIDE_REDEMPTION_WINDOW`, mensagem `O voucher não pode ser usado neste dia e horário.`. A verificação de aplicáveis (`POST /satellite/vouchers/check`) lista o voucher como inaplicável com esse motivo. A janela só restringe o resgate; a concessão não muda. Só em rascunho. |
| D-4 | **Leitura.** `CampaignResponse` (portal) e `SatelliteCampaignResponse` (`GET /satellite/campaigns`) ganham `redemptionTargets` (com `productCode`) e `redemptionWindows`. O `VoucherResponse` não muda: com até 1.000 alvos, repetir a lista em cada voucher pesa demais, e o PDV a lê na campanha (US-07.1). |
| D-5 | **Trilha.** Cada `PUT` grava `CampaignUpdated` com as seções `redemptionTargets` e `redemptionWindows`, só quando mudaram (`AuditSection`). Os alvos de resgate usam o mesmo resumo dos alvos da concessão quando a lista não cabe nos 2.000 caracteres (GH-068 D-6): `count`, `targets` por tipo e, no depois, `changes`. As janelas sempre cabem (14 janelas). |
| D-6 | **Duplicar** (`POST /campaigns/{id}/duplicate`) copia os alvos de resgate e as janelas, como copia as outras seções. |
| D-7 | **Persistência.** Duas tabelas novas, `campaign_redemption_targets` (`id`, `campaign_id`, `target_type`, `category_id`, `product_id`) e `campaign_redemption_windows` (`id`, `campaign_id`, `days_of_week` como máscara de bits, `start_minute`, `end_minute` em minutos do dia). Cascade a partir de `campaigns`, chave `ValueGeneratedNever` e ids v7 em sequência para manter a ordem informada, como as outras coleções substituídas por inteiro. Migrações para SQLite e PostgreSQL. Campanhas existentes ficam com as duas listas vazias, ou seja, com o comportamento de hoje. |
| D-8 | **Submissão** não exige nenhuma das duas listas. |

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
// Application/Campaigns/ReplaceRedemptionTargetsRequest.cs
public record ReplaceRedemptionTargetsRequest(
    [Required] [MaxLength(CampaignContract.MaxItems)] IReadOnlyList<RedemptionTargetRequest>? Targets);

public record RedemptionTargetRequest(
    string? TargetType,      // "Category" ou "Product"
    Guid? CategoryId,        // só com Category
    Guid? ProductId,         // só com Product, sem ProductCode
    [StringLength(CampaignContract.MaxProductCodeLength)] string? ProductCode); // só com Product, sem ProductId

// Application/Campaigns/ReplaceRedemptionWindowsRequest.cs
public record ReplaceRedemptionWindowsRequest(
    [Required] [MaxLength(CampaignContract.MaxRedemptionWindows)] IReadOnlyList<RedemptionWindowRequest>? Windows);

public record RedemptionWindowRequest(
    IReadOnlyList<string?>? DaysOfWeek, // "Monday" a "Sunday", sem repetição, ao menos um
    string? Start,                      // "HH:mm"
    string? End);                       // "HH:mm", >= Start

// Respostas (CampaignResponse e SatelliteCampaignResponse ganham os dois campos, no fim)
public record RedemptionTargetResponse(Guid Id, string TargetType, Guid? CategoryId, Guid? ProductId, string? ProductCode);
public record RedemptionWindowResponse(IReadOnlyList<string> DaysOfWeek, string Start, string End);
```

`CampaignContract.MaxRedemptionWindows = 14`.

Erros novos:

| Código | HTTP | Quando |
|---|---|---|
| `CAMPAIGN_REDEMPTION_TARGET_DUPLICATED` | 422 | A mesma categoria ou o mesmo produto (por id ou por código) duas vezes (D-2) |
| `REDEMPTION_OUTSIDE_REDEMPTION_WINDOW` | 422 | Resgate fora de todas as janelas da campanha (D-3) |

Os alvos de resgate reaproveitam os códigos do catálogo que já existem para os alvos da concessão: `CAMPAIGN_ITEM_CATEGORY_NOT_FOUND`, `CAMPAIGN_ITEM_CATEGORY_INACTIVE`, `CAMPAIGN_ITEM_PRODUCT_NOT_FOUND`, `CAMPAIGN_ITEM_PRODUCT_INACTIVE` e `CAMPAIGN_ITEM_PRODUCT_CODE_NOT_FOUND` (com `codes`).

Contrato (400 `VALIDATION_ERROR`): tipo fora de `Category` e `Product`; `categoryId` ausente ou junto de produto; `productId` e `productCode` juntos ou os dois ausentes; mais de 1.000 alvos; dia inválido ou repetido; nenhum dia; hora fora de `HH:mm`; `end` menor que `start`; mais de 14 janelas.

---

## 3. Especificação do Endpoint / Serviço

| Rota | Mudança |
|---|---|
| `PUT /api/v1/campaigns/{id}/redemption-targets` | Nova (D-2) |
| `PUT /api/v1/campaigns/{id}/redemption-windows` | Nova (D-3) |
| `GET /api/v1/campaigns/{id}` e demais respostas de campanha | `redemptionTargets` e `redemptionWindows` (D-4) |
| `GET /api/v1/satellite/campaigns` | `redemptionTargets` e `redemptionWindows` (D-4) |
| `POST /api/v1/satellite/vouchers/check` e `POST /api/v1/satellite/redemptions` | Janela e alvos de resgate no `RedemptionEvaluator` (D-2, D-3) |
| `POST /api/v1/campaigns/{id}/duplicate` | Copia as duas listas (D-6) |

Autorização das rotas novas igual às outras seções do rascunho (`PUT /campaigns/{id}/usage-restrictions`). Respostas:
- **200:** a campanha com as listas gravadas.
- **400:** contrato.
- **404 e 403:** como hoje.
- **422:** campanha fora de rascunho, catálogo, alvo repetido, edição concorrente (`CAMPAIGN_CONCURRENT_UPDATE`).

---

## 4. Checklist de Implementação

- [ ] 1. Domínio: `CampaignRedemptionTarget` e `CampaignRedemptionWindow`; `Campaign.ReplaceRedemptionTargets` e `Campaign.ReplaceRedemptionWindows` (rascunho, repetição); `RedemptionWindow.Contains(localTime)`.
- [ ] 2. `RedemptionRefusal.OutsideRedemptionWindow`, mensagem em `RedemptionErrors`; `RedemptionEvaluator.Refusal` e `Discount` com os alvos de resgate e a janela (hora local recebida pelo chamador, para o domínio seguir sem fuso).
- [ ] 3. Persistência: configurações EF, `WithItems`/leitura da campanha incluindo as duas coleções (com o produto, para o `productCode`), migrações SQLite e PostgreSQL.
- [ ] 4. Contratos, `CampaignService.ReplaceRedemptionTargetsAsync` (reaproveitando a resolução de códigos e a validação em lote do catálogo) e `ReplaceRedemptionWindowsAsync`, trilha (D-5), duplicação (D-6).
- [ ] 5. Rotas no `CampaignsController`; respostas do portal e do satélite (D-4); `RedemptionService` passa a hora local da rede ao avaliador.
- [ ] 6. Testes de unidade e integração (seção 5); `dotnet test` no SQLite e no PostgreSQL.
- [ ] 7. Smoke `scripts/smoke/restricoes_resgate.py`; `agents.md` (skill do resgate), `docs/PROJECT_STATE.md`, `docs/ROADMAP.md`; aviso ao frontend (etapa nova do portal) e ao PDV (campos novos em `GET /satellite/campaigns` e o motivo novo).

---

## 5. Casos de Teste Essenciais

- [ ] D-1: voucher do CPF A resgatado com o CPF B → 404, utilização intacta.
- [ ] D-1: condição `PurchaseTotal` 100 e `minPurchaseAmount` 300 → concede com 150, recusa o resgate com 150 (`REDEMPTION_AMOUNT_BELOW_MINIMUM`) e resgata com 300.
- [ ] D-2: concessão no produto A, resgate na categoria B; compra só com A → `REDEMPTION_TARGET_NOT_IN_PURCHASE`; compra com A e B → desconto sobre os itens de B.
- [ ] D-2: percentual e valor fixo sobre a base dos alvos de resgate; valor fixo maior que a base fica limitado a ela.
- [ ] D-2: item que casa com dois alvos de resgate (o produto e a categoria dele) entra uma vez na base.
- [ ] D-2: lista vazia mantém o comportamento atual (testes do resgate existentes seguem verdes).
- [ ] D-2: 1.000 códigos, código inexistente e inativo com `codes`, repetido por id e por código → 422; contrato → 400; fora de rascunho → 422.
- [ ] D-3: janela seg a sex 08:00–11:59 no fuso da rede: 11:59:59 de sexta aceita; 12:00:00 recusa; sábado recusa; o check lista o motivo novo.
- [ ] D-3: rede com fuso diferente de UTC: o dia da semana é o local, não o UTC (ex.: domingo 22:00 em São Paulo é segunda em UTC).
- [ ] D-3: `end` menor que `start`, dia repetido, hora inválida, 15 janelas → 400.
- [ ] D-3: ordem dos motivos: fora do escopo vem antes da janela; janela vem antes do valor mínimo.
- [ ] D-4, D-5, D-6: as listas aparecem no portal e no satélite, entram na trilha só quando mudam (com resumo para 1.000 alvos) e são copiadas na duplicação.

---

## 6. Pontos em aberto

| # | Ponto | Motivo | Gatilho |
|---|---|---|---|
| PA-1 | Janela que vira a noite numa linha só (`22:00`–`02:00`) | Duas janelas resolvem; uma linha só exige decidir a qual dia o trecho depois da meia-noite pertence | O portal ou o cliente pedir |
| PA-2 | Horário da compra informado pelo PDV, em vez do relógio do servidor | A compra não traz horário; um PDV offline que manda o resgate depois seria julgado pela hora do envio | O PDV operar offline ou pedir |
| PA-3 | Alvos de resgate no `VoucherResponse` | Lista de até 1.000 itens repetida em cada voucher; o PDV lê na campanha | O PDV pedir para mostrar onde o voucher vale sem consultar a campanha |
| PA-4 | Benefício próprio do resgate (outro tipo ou valor que o da concessão) | O cliente pediu desconto único (GH-068 D-1) | O cliente pedir |
