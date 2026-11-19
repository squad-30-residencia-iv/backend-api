# 📋 Especificação Técnica: GH-068 - Desconto único por campanha e alvo por código de produto

> Status: In Progress
> História de Usuário Relacionada: issue #68 (respostas do cliente aos PAs da spec GH-066, issue #66)
> Responsável: @iagovrocha
> Data: 2026-11-18

---

## 1. Contexto & Regras de Negócio

Em 2026-11-18 o cliente respondeu aos pontos em aberto da spec GH-066:

| PA da GH-066 | Resposta | O que muda |
|---|---|---|
| PA-1: "valor do desconto só 1" | Um único desconto por campanha | D-1 |
| PA-2: alvo como lista de códigos de produto | Os códigos precisam estar cadastrados; um produto pode ter desconto em mais de uma campanha, mas só um voucher é usado por compra | D-2, D-3 e D-4 |
| PA-4: basta uma aprovação | Mantém como está | Nada (SEC-24 segue no PA-10 da GH-066) |
| PA-5: restrições no resgate | O cliente ainda não sabe | Continua em aberto (PA-1 desta spec) |
| PA-6: o dia da emissão conta? | Mantém o comportamento atual | Só o texto da GH-066 (D-5) |

### Decisões

| # | Decisão |
|---|---|
| D-1 | **Desconto único (PA-1).** Todos os alvos da campanha precisam ter o mesmo `benefitType` e o mesmo `benefitValue`. `PUT /campaigns/{id}/items` com valores diferentes responde **422 `CAMPAIGN_BENEFIT_NOT_UNIFORM`** e não grava nada. A submissão (US-01.6) confere a mesma regra, porque um rascunho gravado antes desta mudança, ou duplicado de uma campanha antiga, pode ter descontos diferentes. **O contrato não muda**: o benefício continua em cada alvo, e o portal repete o mesmo valor (ver PA-2). Campanhas já submetidas, aprovadas ou encerradas com descontos diferentes continuam valendo como estão; nenhuma migração as altera. O desempate D1 da concessão não muda. |
| D-2 | **Alvo pelo código do produto (PA-2).** `CampaignItemRequest` ganha `productCode` (opcional). Com `targetType: "Product"`, informe `productId` **ou** `productCode`, nunca os dois (400 `Items[i].ProductCode`). O código é procurado no catálogo com a mesma comparação do resto da plataforma (sem diferenciar maiúsculas, `GetProductsByCodesAsync`) e precisa existir e estar ativo: o que faltar responde **422 `CAMPAIGN_ITEM_PRODUCT_CODE_NOT_FOUND`**, e o inativo responde **422 `CAMPAIGN_ITEM_PRODUCT_INACTIVE`** (o código atual do inativo). Nos dois casos a resposta traz `codes` com os códigos recusados, como na importação em lote da GH-066 (D-3). O alvo é gravado pelo `productId` resolvido. Um código repetido, ou o mesmo produto por id e por código, cai na regra atual de alvo repetido (422). |
| D-3 | **Lista de até 1.000 alvos.** `CampaignContract.MaxItems` passa de 100 para **1.000**, o mesmo limite da importação de produtos. Para isso a validação do catálogo passa a ser feita **em lote**: uma consulta para as categorias, uma para os produtos por id e uma para os códigos, em vez de uma por alvo. Isso fecha a parte do PERF-02 da auditoria que toca os alvos. |
| D-4 | **Um voucher por compra (PA-2).** Já é assim e não muda: a concessão emite um voucher só, o da campanha que vence o desempate D1, e o resgate recusa um segundo voucher na mesma compra (`TransactionAlreadyRedeemed`). Esta spec acrescenta um teste com duas campanhas sobre o mesmo produto, para deixar a regra presa. |
| D-5 | **Texto da GH-066.** A D-1 e o PA-6 da spec GH-066 dizem que a validade termina às 23:59:59 do último dia. O código (`VoucherValidity.Compute`) soma N×24h ao instante da emissão: emitido às 14:32 do dia 18 com 30 dias, vence às 14:32 do dia 18 do mês seguinte, limitado pelo teto `cap`. O texto é corrigido e as respostas do cliente são registradas na seção 6 da GH-066. |
| D-6 | **Trilha dos alvos que não cabe na coluna** (encontrado na implementação). O `CampaignUpdated` da seção `items` grava a lista antes e depois em `audit_entries.payload`, que tem 2.000 caracteres (`AuditJson.MaxPayloadLength`). O SQLite não confere o tamanho; o PostgreSQL recusa, e a gravação dos alvos respondia 500. Pela conta do tamanho, isso já acontecia antes desta spec a partir de uns 10 alvos. A lista continua inteira quando cabe. Quando não cabe, cada lado vira um resumo: `count`, `benefitTypes`, `benefitValues`, `targets` (quantos de cada tipo) e `quotaTotal`. O depois traz ainda `changes`, com quantos alvos entraram (`added`) e saíram (`removed`). Segue o mesmo critério da lista de CPFs do público (GH-047, D-20). |

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
// Application/Campaigns/ReplaceCampaignItemsRequest.cs: MaxLength passa a CampaignContract.MaxItems = 1000.
public record CampaignItemRequest(
    string? TargetType,
    Guid? CategoryId,
    Guid? ProductId,
    string? BenefitType,
    decimal? BenefitValue,
    int? VoucherLimit,
    string? ProductCode = null); // novo (D-2), no fim para não quebrar quem monta o record; só com TargetType = Product

// Application/Campaigns/CampaignResponse.cs: o portal mostra o código sem outra consulta.
public record CampaignItemResponse(
    Guid Id,
    string TargetType,
    Guid? CategoryId,
    Guid? ProductId,
    string BenefitType,
    decimal BenefitValue,
    int? VoucherLimit,
    string? ProductCode);        // novo; preenchido quando o alvo é um produto
```

Erros novos (`CampaignErrors`):

| Código | HTTP | Quando |
|---|---|---|
| `CAMPAIGN_BENEFIT_NOT_UNIFORM` | 422 | Alvos com tipo ou valor de benefício diferentes, ao gravar os alvos ou ao submeter (D-1) |
| `CAMPAIGN_ITEM_PRODUCT_CODE_NOT_FOUND` | 422 | Código de produto que não existe no catálogo, com `codes` (D-2) |

---

## 3. Especificação do Endpoint / Serviço

| Rota | Mudança |
|---|---|
| `PUT /api/v1/campaigns/{id}/items` | Desconto único (D-1), `productCode` (D-2), até 1.000 alvos e validação em lote (D-3) |
| `POST /api/v1/campaigns/{id}/submit` | Confere o desconto único (D-1) |
| `GET /api/v1/campaigns/{id}` e demais respostas de campanha | `productCode` em cada alvo |

Autorização sem mudança. Respostas:
- **200:** os alvos gravados.
- **400:** contrato, por exemplo `productId` e `productCode` juntos, `productCode` fora do alvo `Product` ou mais de 1.000 alvos.
- **404 e 403:** como hoje.
- **422:** RN05, alvo repetido, cotas, D-1 e D-2.

---

## 4. Checklist de Implementação

- [x] 1. D-1: regra no domínio (`Campaign.ReplaceItems` e `Submit`), código e mensagem em `CampaignErrors`
- [x] 2. D-2: `ProductCode` no request, parsing (400) e resolução no catálogo (422 com `codes`)
- [x] 3. D-3: `MaxItems = 1000` e validação do catálogo em lote
- [x] 4. `ProductCode` na resposta dos alvos
- [x] 5. D-4: teste com duas campanhas sobre o mesmo produto
- [x] 6. D-5: corrigir a D-1 e o PA-6 e registrar as respostas na spec GH-066
- [x] 7. D-6: resumo da trilha dos alvos quando a lista não cabe na coluna
- [x] 8. Testes unitários e de integração; `dotnet test` em SQLite e PostgreSQL; smoke
- [x] 9. `agents.md`, roadmap e estado do projeto

---

## 5. Casos de Teste Essenciais

- [x] Dois alvos com o mesmo desconto: 200.
- [x] Dois alvos com valores ou tipos diferentes: 422 `CAMPAIGN_BENEFIT_NOT_UNIFORM`, alvos anteriores intactos.
- [x] Rascunho com descontos diferentes gravado pelo banco: a submissão responde 422 `CAMPAIGN_BENEFIT_NOT_UNIFORM`.
- [x] Alvo por `productCode` (com maiúsculas diferentes): grava o `productId` certo e devolve o `productCode`.
- [x] Código inexistente e código de produto inativo: 422 com o código e `codes`.
- [x] `productId` e `productCode` juntos, ou `productCode` com `targetType: "Category"`: 400.
- [x] O mesmo produto por id e por código: 422 alvo repetido.
- [x] 1.000 alvos importados e informados por código: 200, e a trilha grava o resumo (D-6), que cabe na coluna; 1.001: 400.
- [x] Duas campanhas aprovadas sobre o mesmo produto: a compra recebe um voucher só, e o resgate aceita um voucher só por compra.

---

## 6. Pontos em aberto

| # | Ponto | Motivo de ficar fora | Gatilho |
|---|---|---|---|
| PA-1 | Restrições na etapa de resgate (PA-5 da GH-066) | O cliente ainda não sabe quais quer; o resgate já confere escopo, faixa de valor, alvo, usos, validade e uso na mesma compra | Lista de restrições do cliente |
| PA-2 | Benefício no nível da campanha no contrato (`PUT /items` com um `benefitType` e um `benefitValue` só) | Mudaria o contrato do portal e do satélite e pediria migração; a regra D-1 já garante o desconto único sem quebrar quem integra | Pedido do frontend ou nova versão da API |
| PA-3 | Campanhas antigas com descontos diferentes | Seguem valendo como foram aprovadas; alterar uma campanha aprovada mudaria um acordo já fechado | Pedido do cliente para migrar ou encerrar essas campanhas |
| PA-4 | Mais de 1.000 alvos por campanha | 1.000 cobre a importação em lote; acima disso o motor (PERF-01) e a resposta da campanha crescem demais sem paginação | Caso real do cliente acima de 1.000 produtos |
| PA-5 | A mesma falta de espaço na trilha nas outras seções, como o escopo com muitas lojas ou as condições | Fora do pedido do cliente; o escopo passa de 2.000 caracteres a partir de umas 20 lojas | Issue de endurecimento (PA-10 da GH-066) ou primeiro 500 relatado |
