# 📋 Especificação Técnica: GH-066 - Ajustes do cliente na criação de campanha e correções urgentes da auditoria

> **Status:** In-Progress
> **História de Usuário Relacionada:** issue #66 (pontos do cliente de 2026-11-18; US-01.12, US-02.1, US-02.4, US-04.5, US-07.2; auditoria estática do backend)
> **Branch:** `feat/GH-66-ajustes-cliente-e-auditoria` → `staging`

---

## 1. Contexto & Regras de Negócio

### 1.1 Origem

- O cliente revisou a criação de campanha e trouxe 11 pontos (issue #66).
- Uma auditoria estática do backend, feita sobre o commit `0e5e8e6`, listou 39 achados. Nós os conferimos contra a `staging` `3775580`; os relatórios ficam fora do repositório.
- Esta fatia entrega o que não depende de nova validação com o cliente e os achados urgentes. O resto vai para a seção 6 com motivo e gatilho.

### 1.2 Recorte

| Item | Origem | Entrega |
|---|---|---|
| A | Cliente 1 | Validade do voucher só em dias, contados a partir da emissão |
| B | Cliente 10 e 3 | Importação em lote de lojas (código e nome) e de produtos (código e nome) |
| C | Cliente 11 + SEC-02 | Limite de vouchers por CPF atômico na concessão |
| D | SEC-04 | Sai o alias `voucher:read` do escopo de emissão |
| E | SEC-01 | Limite de tentativas no login dos usuários do portal |
| F | SEC-07, SEC-21 | Recusar a chave JWT de desenvolvimento fora de Development; `.env` só em Development |
| G | SEC-08 | `/api/v1/system/status` só para o administrador |
| H | SEC-06 | Consulta de vouchers do cliente pelo satélite sem o CPF na URL |
| I | Cliente 5 | Contrato do público elegível para o PDV documentado |

### 1.3 Decisões (propostas nesta fatia)

| # | Decisão |
|---|---|
| D-1 | **Validade (A).** `PUT /campaigns/{id}/voucher-validity` aceita só `type: "Relative"` com `unit: "Days"`; `Hours` responde 400 `Unit` e `FixedDate` responde 400 `Type`, com mensagens novas. O formato do corpo não muda; o teto `cap` continua opcional. A conta segue a atual: N dias a partir da emissão, com o fim inclusivo até 23:59:59 do último dia (D9). Campanhas já gravadas com horas ou data fixa continuam valendo e aparecendo como estão: o domínio não muda, e nenhuma migração as converte. |
| D-2 | **Importação de lojas (B).** `POST /api/v1/establishments/import` com `{ regionId, channels, establishments: [{ code, name }] }`, de 1 a 1.000 linhas. É tudo ou nada: valida tudo antes de gravar; código repetido no corpo ou já existente na região recusa o lote, e o erro aponta a linha (`Establishments[i].Code`). Resposta 201 com as lojas criadas. Só o administrador, como no cadastro um a um (`ManageHierarchy`); código já existente responde 422 `ESTABLISHMENT_CODE_TAKEN` com a lista `codes`. |
| D-3 | **Importação de produtos (B).** `POST /api/v1/products/import` com `{ categoryId, products: [{ code, name }] }`, de 1 a 1.000 linhas, com as mesmas regras de D-2 (código único no catálogo, com a mesma comparação do cadastro um a um). Só o administrador (`ManageCatalog`); código já existente responde 422 `PRODUCT_CODE_TAKEN` com a lista `codes`. |
| D-4 | **Limite por CPF (C).** Dentro da transação da reserva, depois do `UPDATE` que trava a linha da campanha, o motor conta de novo os vouchers do CPF naquela campanha (confirmados e provisórios em dia, a regra atual) e, se o limite foi atingido, desfaz a reserva e recusa com `CustomerLimitReached`. A linha travada serializa as concessões da mesma campanha, então a recontagem vê o voucher de quem chegou antes. Sem tabela nova. |
| D-5 | **Escopo (D).** Sai só `voucher:read`, porque o nome promete leitura e dá emissão. Os aliases no singular (`voucher:issue`, `campaign:read`, `voucher:redeem`) continuam. Aplicação já gravada não muda, porque o banco guarda o escopo canônico. |
| D-6 | **Login (E).** Limitador nativo do ASP.NET Core no `POST /auth/login`, por IP, com janela fixa: `Auth:LoginRateLimit:PermitLimit` (padrão 10) a cada `WindowSeconds` (padrão 60). Acima disso, 429 `AUTH_LOGIN_RATE_LIMITED` com `Retry-After`. O bloqueio por e-mail fica para a seção 6. |
| D-7 | **Configuração (F).** Fora de Development, a API não sobe com a chave JWT de desenvolvimento (a que contém `nao-usar-em-producao`). O `.env` só é lido em Development e só da pasta atual. |
| D-8 | **Status do sistema (G).** `/api/v1/system/status` passa a exigir o papel administrador; `/health` continua anônimo. |
| D-9 | **Vouchers do cliente (H).** Nova rota `POST /api/v1/satellite/customers/vouchers/search` com `{ document, status }` no corpo, mesmo escopo e mesma resposta da rota atual. A rota `GET /satellite/customers/{document}/vouchers` continua por compatibilidade, com o cabeçalho `Deprecation: true` e `Link` para a nova, até o PDV migrar (seção 6). |
| D-10 | **Contrato do público (I).** Seção nova no README sobre a integração do satélite: que atributos mandar em `customer`, como ler `requiredAttributes` e `historyWindows` em `GET /satellite/campaigns`, e o motivo `CustomerNotEligible`. |

---

## 2. Contratos & Schemas

```csharp
public record ImportEstablishmentsRequest(
    [Required] Guid? RegionId,
    [Required] IReadOnlyList<string>? Channels,
    [Required] IReadOnlyList<ImportEstablishmentLine>? Establishments);
public record ImportEstablishmentLine(string? Code, string? Name);

public record ImportProductsRequest(
    [Required] Guid? CategoryId,
    [Required] IReadOnlyList<ImportProductLine>? Products);
public record ImportProductLine(string? Code, string? Name);

public record CustomerVouchersSearchRequest([Required] string? Document, string? Status);
```

Códigos novos:
- `AUTH_LOGIN_RATE_LIMITED` (429)
- validação 400 com as mensagens de validade só em dias
- validação 400 por linha nas importações
- `ESTABLISHMENT_CODE_TAKEN` e `PRODUCT_CODE_TAKEN` (422) apontando a linha

---

## 3. Endpoints

| Rota | Mudança |
|---|---|
| `PUT /api/v1/campaigns/{id}/voucher-validity` | Só dias (D-1) |
| `POST /api/v1/establishments/import` | Nova (D-2) |
| `POST /api/v1/products/import` | Nova (D-3) |
| `POST /api/v1/satellite/concessions` | Limite por CPF atômico (D-4) |
| `POST /api/v1/satellite/apps` e `PUT` | `voucher:read` deixa de ser aceito (D-5) |
| `POST /api/v1/auth/login` | 429 acima do limite (D-6) |
| `GET /api/v1/system/status` | Só administrador (D-8) |
| `POST /api/v1/satellite/customers/vouchers/search` | Nova (D-9); a `GET` antiga fica marcada como obsoleta |

---

## 4. Checklist de Implementação

- [x] 1. D-1 validade só em dias e testes ajustados
- [x] 2. D-5 alias
- [x] 3. D-4 limite por CPF com teste de concorrência
- [x] 4. D-2 e D-3 importações
- [x] 5. D-6, D-7 e D-8
- [x] 6. D-9 e D-10
- [x] 7. `dotnet build -warnaserror`, `dotnet test` em SQLite e PostgreSQL, smoke com a API no ar, agente `testador-de-branch` (Opus)

---

## 5. Casos de Teste Essenciais

- Validade com `Hours` ou `FixedDate`: 400; com `Days`: 200, e o voucher emitido vale até o fim do N-ésimo dia.
- Campanha gravada antes com horas continua emitindo com a regra antiga.
- Importação de lojas: lote válido cria todas; código repetido no corpo ou já existente recusa o lote inteiro sem gravar nada; região de outra rede 403; acima de 1.000 linhas 400.
- Importação de produtos: as mesmas regras.
- Limite por CPF igual a 1 e 20 concessões simultâneas do mesmo CPF em transações diferentes: exatamente um voucher.
- `voucher:read` no cadastro da aplicação: 400.
- Login: a 11ª tentativa no minuto responde 429 com `Retry-After`.
- `/system/status`: sem token 401; analista 403; administrador 200.
- Busca de vouchers por `POST`: mesma resposta da `GET`; a `GET` responde com `Deprecation`.

---

## 6. Pontos em aberto

| # | Ponto | Motivo de ficar fora | Gatilho |
|---|---|---|---|
| PA-1 | Cliente 2: "valor do desconto só 1" | Ambíguo: um único desconto por campanha ou um único tipo de benefício | Resposta do cliente |
| PA-2 | Cliente 3: alvo da campanha como lista de códigos de produto | Falta saber se o código precisa existir no catálogo ou basta o satélite conhecê-lo; a importação em lote (D-3) já cobre o volume | Resposta do cliente |
| PA-3 | Cliente 4: carregar CPFs por arquivo | A API já aceita a lista; a leitura do CSV pode ser feita no portal. Retenção e base legal seguem no PA-4 da GH-047 | Pedido do frontend para uma rota de upload |
| PA-4 | Cliente 7: basta uma aprovação | Já é assim; falta o cliente confirmar se quem editou pode aprovar (SEC-24) | Resposta do cliente |
| PA-5 | Cliente 8: restrições no resgate | O resgate já confere escopo, valor mínimo e máximo, alvo e usos; a etapa nova é do portal | Lista de restrições do cliente |
| PA-6 | Cliente 1: o dia da emissão conta como o 1º dia? | Hoje N dias a partir da emissão terminam às 23:59:59 do dia emissão + N | Resposta do cliente |
| PA-7 | Cliente 6 e 9 | Só frontend | — |
| PA-8 | SEC-01: bloqueio por e-mail e log de falhas (SEC-22) | O limite por IP fecha a força bruta mais simples; o bloqueio por conta pede estado compartilhado | Deploy com mais de uma instância ou pedido do PO |
| PA-9 | SEC-06: retirar a `GET` com CPF na URL, mascarar o código e paginar | O PDV ainda usa a rota antiga | Confirmação do PDV de que migrou |
| PA-10 | SEC-03, SEC-05, SEC-09 a SEC-13, SEC-14 a SEC-24, ARQ-02, ARQ-03, PERF-01 a PERF-04, QUA-01 a QUA-08 | Endurecimento, desempenho e qualidade sem urgência de negócio | Issue de endurecimento, antes do deploy público |

---

## 7. Notas da implementação

### 7.1 Diferenças em relação ao texto acima

| Ponto | O que ficou | Por quê |
|---|---|---|
| Papéis das importações (D-2, D-3) | Só o administrador (`ManageHierarchy` e `ManageCatalog`), como no cadastro um a um; código já existente responde 422 com a lista `codes` | O texto inicial falava em analista e gestor, mas o cadastro um a um já é só do administrador |
| Comparação do código do produto (D-3) | A mesma do cadastro um a um (igualdade exata) | Manter as duas rotas coerentes; maiúsculas ficam no PERF-04 |
| `.env` (D-7) | Em Development continua procurando o `.env` nas pastas acima | A API roda de `src/VoucherCampaigns.Api` e o `.env` fica na raiz do repositório; fora de Development não é lido |
| Limite do login nos testes e no smoke | `ApiFactory` usa `Auth:LoginRateLimit:PermitLimit = 100000` e o `start-api.sh` usa 1000 | Os testes e os roteiros fazem muitos logins da mesma origem; o limite tem teste próprio (`LoginRateLimitTests`) |
| Validade (D-1) | Saíram as validações da data fixa no serviço; o domínio (`SetFixedValidity`, horas) continua | Campanhas já gravadas seguem valendo; o teste do fim depois da data fixa grava pelo banco |

### 7.2 Verificação

- **Build:** `dotnet build -warnaserror` sem avisos.
- **SQLite:** 310 unitários e 557 de integração, todos aprovados.
- **PostgreSQL, suíte inteira:** 551 de 557. As 6 falhas são o timeout conhecido ao criar e apagar o banco de teste, sem falha de asserção.
- **PostgreSQL, classes tocadas por esta branch:** concessão, importação, consulta, status, validade, satélite, rotas protegidas e as leituras do #65. A 1ª rodada passou 139 de 139; na 2ª, 3 testes das classes do #65 que criam um banco por teste caíram no mesmo timeout.
- **Limite por CPF (D-4):** 20 concessões simultâneas do mesmo CPF com limite 1.
  - SQLite: 3 de 3 com a correção; 2 de 2 falham sem ela.
  - PostgreSQL: 3 de 3 com a correção.
- **Chave JWT (D-7):** a API em `Production` com a chave de desenvolvimento não sobe e mostra a mensagem da D-7.
- **Smoke:** `smoke.py` (agora com `/system/status` 401/403/200), `motor.py`, `publico.py` e `cancelamento.py` OK; `POST /establishments/import` e `POST /products/import` 201 com a API no ar.
- **Agente `testador-de-branch` (Opus) no commit `e1f5638`: aprovado.**
  - **SQLite:** 310 unitários e 557 de integração.
  - **PostgreSQL:** 553 de 557. As falhas são o timeout conhecido ao apagar o banco de teste, em classes que a branch não tocou. `ConcessionConcurrencyTests` passou 4 rodadas de 3 de 3.
  - **Rotas e startup:** todas as rotas e todos os cenários de inicialização deram OK, inclusive o login acima do limite (429 com `Retry-After: 60`) e a API em Production recusando a chave de desenvolvimento.
  - **Leitura de código:** confirmou a recontagem do D-4 sob a trava da campanha, o limitador restrito ao login e as importações sem gravação parcial.
  - **Corrigido depois:** o comentário XML que estava no record errado.
  - **Fica registrado, sem correção nesta fatia:**
    - na busca por `POST`, o campo do erro vem como `document` (CPF inválido) ou `Document` (ausente), conforme a origem da validação;
    - o cabeçalho `Deprecation: true` segue o formato dos rascunhos, e não a data da RFC 9745;
    - o `POST /satellite/token` continua no PA-10.

