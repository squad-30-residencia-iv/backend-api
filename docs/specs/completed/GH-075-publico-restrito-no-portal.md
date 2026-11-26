# 📋 Especificação Técnica: GH-075 - Público restrito no portal (parte do backend)

> Status: Completed
> História de Usuário Relacionada: issue #75 (front + back)
> Responsável: @iagovrocha
> Data: 2026-11-26

---

## 1. Contexto & Regras de Negócio

A API já grava, devolve e avalia o público restrito desde a GH-047:
- `PUT /campaigns/{id}/audience` com os sete tipos de critério;
- `eligibility.criteria` no `CampaignResponse`;
- `GET .../audience/customers` e `POST .../eligibility/simulate`;
- `requiredAttributes` e `historyWindows` em `GET /satellite/campaigns`;
- avaliação na concessão.

O portal (`frontend-web`, `staging`) só envia `AllCustomers`. A issue pede ao backend "só o que faltar para o portal (mensagens 422, catálogo de critérios na resposta da campanha)" e diz para **não reabrir a GH-047**. Ao conferir o código, faltam duas coisas:

| Lacuna | Situação na `staging` (`6ebcf5c`) | Decisão |
|---|---|---|
| Categoria do critério `PurchasedCategory` | Categoria inexistente responde `400 VALIDATION_ERROR`; os alvos e as condições respondem `422` com código próprio para o mesmo caso. A consulta é feita critério por critério | D-1 |
| O que o formulário precisa saber de cada critério | Os tipos, campos e limites só aparecem nas mensagens de erro; o portal teria de copiar as regras | D-2 |

### Decisões

| # | Decisão |
|---|---|
| D-1 | **Categoria do histórico.** Categoria que não existe passa a responder **422 `CAMPAIGN_AUDIENCE_CATEGORY_NOT_FOUND`**, com a mensagem atual (`A categoria informada não existe.`) e a extensão `field` (`Criteria[i].CategoryId`), para o portal marcar o campo. Segue o padrão dos alvos e das condições (contrato em 400, catálogo em 422). **Categoria inativa continua aceita:** o critério olha compras passadas (US-06.2, cenário 3; regra da GH-047 mantida). As categorias são conferidas numa consulta só (`GetCategoriesByIdsAsync`, como na GH-071 D-4). Nada mais muda nas mensagens do público: as de contrato já são por campo e em português. |
| D-2 | **Catálogo de critérios.** Nova rota `GET /api/v1/eligibility/criteria`, só leitura e igual para todas as redes. Ela diz, para cada tipo: nome para a tela, campos com tipo, obrigatoriedade e limites, se pede janela de histórico e quais atributos o satélite precisa mandar (os mesmos nomes de `requiredAttributes`). Os limites vêm das constantes que a validação já usa (`EligibilityContract`), então a tela e a API não se desencontram. A "resposta da campanha" já traz os critérios gravados (`eligibility.criteria`); o catálogo fica numa rota própria para não repetir a mesma lista em cada campanha. |
| D-3 | **Nada muda** na gravação, na leitura, na simulação, no satélite e na avaliação da concessão (GH-047). `AllCustomers` continua o padrão do assistente. |

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
// Application/Eligibility/EligibilityCatalog.cs
public record EligibilityCriterionCatalogResponse(
    string Type,                                 // AgeRange, Location, ...
    string Label,                                // "Faixa etária"
    string Description,
    IReadOnlyList<EligibilityFieldCatalogResponse> Fields,
    bool RequiresHistoryWindow,                  // TotalSpent, PurchaseFrequency, PurchasedCategory
    IReadOnlyList<string> SatelliteAttributes);  // birthDate, state, cityCode, loyaltyMember, history.*

public record EligibilityFieldCatalogResponse(
    string Name,                                 // minAge, states, documents...
    string Kind,                                 // integer, decimal, stringList, documentList, categoryId
    bool Required,
    decimal? Min,
    decimal? Max,
    int? MaxItems,
    string? Note);                               // "ao menos um entre states e cityCodes", "1 a 365 dias"...

public record EligibilityCatalogResponse(
    int MaxCriteria,                             // 20
    IReadOnlyList<EligibilityCriterionCatalogResponse> Criteria);
```

Erro novo (`CampaignErrors`):

| Código | HTTP | Quando |
|---|---|---|
| `CAMPAIGN_AUDIENCE_CATEGORY_NOT_FOUND` | 422 | Critério `PurchasedCategory` com categoria que não existe; extensão `field` com o campo |

---

## 3. Especificação do Endpoint / Serviço

| Rota | Mudança |
|---|---|
| `GET /api/v1/eligibility/criteria` | Nova (D-2). Policy `ManageCampaigns` (analista e gestor, quem monta o público). `200` com o catálogo |
| `PUT /api/v1/campaigns/{id}/audience` | Categoria inexistente: `400` → `422 CAMPAIGN_AUDIENCE_CATEGORY_NOT_FOUND` (D-1) |

---

## 4. Checklist de Implementação

- [x] 1. `EligibilityCatalog` estático a partir de `EligibilityContract` e do mapa de atributos do satélite (extrair o mapa de `SatelliteCampaignService.RequiredAttributes` para um lugar só, usado pelos dois).
- [x] 2. Rota `GET /api/v1/eligibility/criteria`.
- [x] 3. D-1 no `CampaignService` (consulta em lote, 422 com `field`); código em `CampaignErrors`.
- [x] 4. Testes: catálogo (todos os tipos do enum, limites iguais às constantes, atributos iguais aos de `requiredAttributes`), categoria inexistente 422, categoria inativa aceita, autorização da rota.
- [x] 5. `dotnet test` no SQLite e no PostgreSQL; smoke; `agents.md`, `docs/PROJECT_STATE.md`; aviso ao frontend.

---

## 5. Casos de Teste Essenciais

- [x] O catálogo tem um item para cada valor de `EligibilityCriterionType`, com os limites de `EligibilityContract` (20 critérios, idade 0 a 130, 100 UFs e municípios, janela 1 a 365, 10.000 CPFs).
- [x] Os `satelliteAttributes` de cada tipo batem com os `requiredAttributes` de uma campanha que só tem aquele critério.
- [x] `PurchasedCategory` com categoria inexistente → 422 `CAMPAIGN_AUDIENCE_CATEGORY_NOT_FOUND` com `field`; nada é gravado.
- [x] `PurchasedCategory` com categoria inativa → 200.
- [x] Rota do catálogo: analista e gestor 200; administrador 403; sem token 401.
- [x] Aceite da issue, já coberto pela GH-047 e conferido de novo: grava `Restricted` e relê no GET; `AllCustomers` é o padrão; a campanha restrita aparece no satélite com `requiredAttributes` e `historyWindows`.

---

## 6. Pontos em aberto

| # | Ponto | Motivo | Gatilho |
|---|---|---|---|
| PA-1 | Diferença entre a lista de CPFs do público (`SpecificCustomers`, avaliada no PDV) e os grupos de clientes da GH-072 (lote emitido na vigência, issue #79) | As duas usam CPFs e o portal pode apresentá-las como a mesma coisa. Hoje uma é filtro na concessão e a outra é destinatária do lote | Bruno ou o PO dizer se o portal mostra as duas, ou se uma substitui a outra |
| PA-2 | Nomes e descrições do catálogo em outros idiomas | O portal é só em português | Pedido de outro idioma |
