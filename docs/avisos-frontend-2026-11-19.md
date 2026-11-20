# Mudanças na API para o portal (frontend)

> Backend `backend-api`, na `main` e na `staging` desde 2026-11-20 (PR #64, `d9df871`).
> Os erros seguem o formato de sempre: `ProblemDetails` com `code` e, nos erros de campo (`400 VALIDATION_ERROR`), `errors` por campo.

## 1. O que pode quebrar o portal (ação necessária)

| # | Onde | Antes | Agora | O que o portal precisa fazer | Issue |
|---|---|---|---|---|---|
| 1 | `PUT /api/v1/campaigns/{id}/voucher-validity` | Aceitava `Relative` em dias ou horas e `FixedDate` | Só `{"type": "Relative", "amount": N, "unit": "Days"}`; o campo `cap` (teto) continua opcional. `Hours` e `FixedDate` dão `400` | Tirar horas e data fixa da tela; a validade é contada em N×24h a partir da emissão do voucher | #66 |
| 2 | `PUT /api/v1/campaigns/{id}/items` e `POST .../submit` | Cada alvo com desconto próprio | **Um desconto único por campanha:** todos os alvos com o mesmo `benefitType` e `benefitValue`, senão `422 CAMPAIGN_BENEFIT_NOT_UNIFORM`. A submissão confere a mesma regra (rascunho antigo com descontos diferentes não submete) | Pedir o desconto uma vez na tela e repetir o valor em todos os alvos do `PUT` | #68 |
| 3 | Qualquer gravação de campanha (`PUT` das seções, `submit`, `approve`, `reject`, `pause`, `end`, `cancel`) | A segunda gravação simultânea sobrescrevia a primeira | `422 CAMPAIGN_CONCURRENT_UPDATE` quando outra requisição alterou a campanha ao mesmo tempo | Recarregar a campanha e tentar de novo (mensagem: "A campanha foi alterada por outra requisição ao mesmo tempo. Recarregue e tente de novo.") | #47 |
| 4 | `POST /api/v1/auth/login` | Sem limite | Limite de tentativas por IP: `429 AUTH_LOGIN_RATE_LIMITED`, com o cabeçalho `Retry-After` (segundos) | Mostrar "muitas tentativas, aguarde" usando o `Retry-After` | #66 |
| 5 | `GET /api/v1/system/status` | Qualquer usuário logado | Só o administrador; os outros papéis recebem `403` | Esconder a tela de status para quem não é administrador | #66 |

## 2. Novidades (o portal passa a poder usar)

### 2.1 Alvos da campanha (issue #68)

- O alvo de produto aceita **`productCode`** no lugar de `productId` (nunca os dois juntos, senão `400`). A busca do código não diferencia maiúsculas.
  ```json
  { "items": [ { "targetType": "Product", "productCode": "MOD-001", "benefitType": "Percentage", "benefitValue": 10 } ] }
  ```
- Código que não existe: `422 CAMPAIGN_ITEM_PRODUCT_CODE_NOT_FOUND`; produto inativo: `422 CAMPAIGN_ITEM_PRODUCT_INACTIVE`. Nos dois casos, `codes` traz a lista dos códigos recusados, para a tela marcar as linhas.
- O limite subiu de 100 para **1.000 alvos**.
- Cada alvo da resposta traz `productCode`.

### 2.2 Cadastro em lote (issue #66)

Só o administrador. Até 1.000 linhas por chamada; a lista inteira é validada antes de gravar.

| Rota | Corpo |
|---|---|
| `POST /api/v1/establishments/import` | `{ "regionId": "...", "channels": ["Physical", "Virtual"], "establishments": [ { "code": "L001", "name": "Loja Centro" } ] }` |
| `POST /api/v1/products/import` | `{ "categoryId": "...", "products": [ { "code": "MOD-001", "name": "Camiseta" } ] }` |

Resposta `201` com o que foi criado. Código repetido dentro da lista dá `400` no campo da linha (`Products[i].Code` ou `Establishments[i].Code`). Código já cadastrado recusa a lista inteira com `422` e `codes`.

### 2.3 Público restrito (issue #47)

- `PUT /api/v1/campaigns/{id}/audience` com `{ "audienceType": "Restricted", "criteria": [ ... ] }` (ou `"AllCustomers"` sem critérios). Tipos de critério:

  | `type` | Campos |
  |---|---|
  | `AgeRange` | `minAge`, `maxAge` |
  | `Location` | `states` (UF) e/ou `cityCodes` (código IBGE, 7 dígitos), até 100 cada |
  | `LoyaltyMember` | — |
  | `TotalSpent` | `minAmount`, `windowDays` |
  | `PurchaseFrequency` | `minPurchases`, `windowDays` |
  | `PurchasedCategory` | `categoryId`, `windowDays` |
  | `SpecificCustomers` | `documents` (CPFs, com ou sem máscara, até 10.000) |

- **Lista de CPFs por arquivo (ponto 4 do cliente):** o portal lê o CSV e manda os CPFs em `documents`. CPF inválido responde `400` com a mensagem `CPFs inválidos nas posições: 2, 4, 5.` (posição na lista enviada, a partir de 1), para a tela apontar a linha do arquivo.
- A resposta da campanha traz `eligibility.criteria`; a lista de CPFs aparece só como `customerCount`. Para ver os CPFs: `GET /api/v1/campaigns/{id}/audience/customers?page=&pageSize=` (até 500 por página).
- `POST /api/v1/campaigns/{id}/eligibility/simulate` avalia um cliente de exemplo contra os critérios.

### 2.4 Cancelar campanha (issue #61)

- `POST /api/v1/campaigns/{id}/cancel` com `{ "reason": "..." }` (obrigatório, até 500 caracteres). Analista ou gestor.
- Só cancela campanha **agendada, ativa, pausada ou esgotada**; nos outros status, `422 CAMPAIGN_CANCEL_NOT_ALLOWED`.
- Cancelar invalida os vouchers ainda não usados. O status passa a `Cancelled`.

### 2.5 Restrições no resgate, a etapa nova do portal (issue #70, ponto 8 do cliente)

Duas seções novas do rascunho, como as outras (só em rascunho, `422 CAMPAIGN_NOT_DRAFT` depois):

| Rota | Corpo | Lista vazia |
|---|---|---|
| `PUT /api/v1/campaigns/{id}/redemption-targets` | `{ "targets": [ { "targetType": "Category", "categoryId": "..." }, { "targetType": "Product", "productCode": "CAL-001" } ] }` (ou `productId`), até 1.000 | O voucher vale no alvo da concessão, como hoje |
| `PUT /api/v1/campaigns/{id}/redemption-windows` | `{ "windows": [ { "daysOfWeek": ["Monday", "Friday"], "start": "08:00", "end": "11:59" } ] }`, até 14 | Qualquer dia e hora |

- **Alvos de resgate:** onde o voucher pode ser usado. Com a lista preenchida, o desconto é calculado sobre os itens desses alvos na compra. Só `Category` e `Product` (sem `All`). Mesmos erros de catálogo dos alvos (`codes` incluído); alvo repetido: `422 CAMPAIGN_REDEMPTION_TARGET_DUPLICATED`.
- **Janelas de resgate:** horário `HH:mm` no fuso da rede; o minuto do fim vale inteiro (`11:59` aceita até 11:59:59). **Fim antes do início vira a noite**: `{"daysOfWeek": ["Friday"], "start": "22:00", "end": "01:59"}` vale de sexta 22:00 até sábado 01:59 (os dias são os do início). Dias: `Monday` a `Sunday`, sem repetir.
- A resposta da campanha traz `redemptionTargets` (com `productCode`) e `redemptionWindows` (dias de segunda a domingo).
- **O que já existia e o cliente também pediu:** o voucher só vale para o CPF que o ganhou, e o valor mínimo de resgate (`PUT .../usage-restrictions`, `minPurchaseAmount`) é separado do mínimo de concessão (condição `PurchaseTotal` em `PUT .../conditions`). A tela pode deixar isso claro.

### 2.6 Paginação opcional (issue #71)

- `GET /api/v1/campaigns` e `GET /api/v1/vouchers` aceitam `page` (a partir de 1) e `pageSize` (1 a 100; padrão 20 quando só `page` vem). Fora da faixa: `400`.
- O corpo continua sendo uma lista; o total da consulta vem no cabeçalho **`X-Total-Count`** (também sem paginação).
- **Sem `page` e `pageSize`, a resposta é a mesma de antes.** Recomendamos adotar a paginação: quando o portal usar, ela passa a ser obrigatória (com teto) num próximo passo.
- Os filtros (`status`, `campaignId`, `search`, `issuedFrom`, `issuedTo`) continuam iguais e agora rodam no banco.

## 3. Já conhecidos pelo portal (só para registro)

- Acompanhamento (EP-03, PR #59 e #65): `GET /api/v1/dashboard`, `GET /api/v1/campaigns/{id}/dashboard`, `GET /api/v1/campaigns/{id}/audit` e `.../audit/export` (CSV), `GET /api/v1/vouchers` e `GET /api/v1/vouchers/{id}` (CPF mascarado em `customerDocumentMasked`), `GET /api/v1/reports/overview` e `.../catalog`, `GET /api/v1/audit-entries`; `CampaignResponse` com `redeemedVouchers` e `expiredVouchers`.

## 4. Perguntas para o frontend

1. Os itens da seção 1 já valem na `main` e na `staging`. Se algum ambiente de vocês ainda aponta para uma versão anterior, nos digam qual.
2. Quando a tela de campanhas e a de vouchers passarem a usar `page`/`pageSize`, nos avisem: é o gatilho para a paginação ficar obrigatória.
3. O relatório de auditoria do frontend que recebemos ainda não foi conferido do lado de vocês; se algum item depender da API, nos chamem.
