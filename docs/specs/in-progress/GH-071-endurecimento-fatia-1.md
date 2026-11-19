# 📋 Especificação Técnica: GH-071 - Endurecimento do backend, fatia 1

> Status: In Progress
> História de Usuário Relacionada: issue #71 (PA-10 da GH-066, PA-5 da GH-068, PA-9 da GH-066)
> Responsável: @iagovrocha
> Data: 2026-11-19

---

## 1. Contexto & Regras de Negócio

A auditoria estática do backend (issue #66; o relatório fica fora do repositório) teve os itens urgentes resolvidos nas issues #66 e #68. Esta fatia pega os achados de severidade média que ainda faltam, mais dois itens ligados a entregas recentes. Os de severidade baixa e informativa ficam para as fatias seguintes da issue #71.

| Item | Tema | Situação na `staging` (`801b6a3`) |
|---|---|---|
| SEC-03 | Credencial do satélite depois de inativar a aplicação | O token vale até expirar (60 min), mesmo com a aplicação inativa, o segredo trocado ou os escopos reduzidos (GH-028, D-6) |
| SEC-05 | Limite de tentativas no `POST /satellite/token` | Bloqueio só pelo `clientId`, num dicionário em memória que só limpa a entrada quando o mesmo `clientId` volta |
| PERF-01 | Candidatas da concessão | `GetApprovedCampaignsAsync` traz toda campanha que já foi aprovada (inclusive encerradas e de vigência futura), com alvos, condições, critérios e escopo, e filtra em memória |
| PERF-02 | Validação de condições e escopo | Uma ou duas consultas por condição e por local; os alvos já são validados em lote (GH-068 D-3) |
| PERF-03 | Listagens | `GET /campaigns` e `GET /vouchers` (portal) trazem tudo; o filtro de status da campanha roda em memória |
| Trilha | Seções que não cabem em `audit_entries.payload` (2.000 caracteres) | Só os alvos têm resumo (GH-068 D-6). Escopo com umas 20 lojas, ou público com muitos municípios, dá 500 no PostgreSQL |
| SEC-06 (resto) | Rota antiga `GET /satellite/customers/{document}/vouchers` | Já responde `Deprecation` e `Link` (GH-066 D-9); não há data de retirada nem como saber se o PDV ainda a usa |

### Decisões

| # | Decisão |
|---|---|
| D-1 | **SEC-03: carimbo da aplicação.** `SatelliteApp` ganha `SecurityStamp` (texto aleatório), trocado ao inativar, reativar, trocar o segredo e mudar os escopos. O token do satélite leva a claim `stamp`. No `OnTokenValidated` do esquema do satélite, a API confere se a aplicação existe, está ativa e tem o mesmo carimbo; se não, responde 401 `AUTH_UNAUTHENTICATED`, como um token vencido. A consulta usa `IMemoryCache` por até `Satellite:StampCacheSeconds` (padrão 30); a mudança feita nesta instância apaga a entrada na hora, e outra instância percebe em até 30 s. Token emitido antes desta mudança, sem `stamp`, é recusado: o PDV pede um token novo, que é o que ele já faz depois de um 401. Migração: coluna `security_stamp`, preenchida para as aplicações existentes. |
| D-2 | **SEC-05: limite por origem.** (a) O `POST /satellite/token` passa a ter também o limite nativo por IP, como o login (GH-066 D-6): `Satellite:TokenRateLimit:PermitLimit` (padrão 20) por `WindowSeconds` (padrão 60), com 429 e `Retry-After`. (b) O bloqueio depois de 5 segredos errados passa a ser por **`clientId` + IP**: errar o segredo de outro endereço não bloqueia o PDV legítimo. (c) O dicionário tem teto (`MaxTrackedClients`, 10.000) e descarta as entradas vencidas quando cresce; acima do teto, a falha nova só conta no limite por IP. O estado continua em memória de cada instância: com mais de uma, o limite efetivo se multiplica (ver PA-1). |
| D-3 | **PERF-01: filtro no banco.** A consulta das candidatas traz só campanhas com status gravado `Scheduled`, `Active` ou `Paused` e vigência que contém o agora (`StartsAt <= agora` e `EndsAt >= agora` truncado ao segundo, o mesmo critério de `BusinessTime`). A regra fina continua no domínio (`EffectiveStatus`, PR-04). **Efeito visível:** uma compra que só casava com uma campanha encerrada, cancelada ou de vigência futura passa a ser recusada com o motivo `NoCampaignMatched`, em vez de `CampaignNotActive`. A pausada dentro da vigência continua com o motivo `CampaignNotActive`. Sem cache: a concessão continua lendo o saldo e o status atuais. |
| D-4 | **PERF-02: condições e escopo em lote.** `PUT /campaigns/{id}/conditions` consulta as categorias e os produtos das condições numa consulta cada; `PUT /campaigns/{id}/scopes` consulta as regiões e as lojas numa consulta cada (os métodos `GetCategoriesByIdsAsync` e `GetProductsByIdsAsync` da GH-068, mais os equivalentes de região e loja). Os códigos e mensagens de erro não mudam; quando mais de um local falha, vale o primeiro na ordem informada, como hoje. |
| D-5 | **PERF-03: paginação opcional.** `GET /campaigns` e `GET /vouchers` aceitam `page` (a partir de 1) e `pageSize` (1 a 100). **O corpo continua uma lista**, para não quebrar o portal; o total vai no cabeçalho `X-Total-Count`. Sem `page`, a resposta continua com tudo, como hoje. O filtro de status de `GET /campaigns` vira predicado no SQL (o status efetivo deriva do status gravado e das datas), para a página e o total já saírem filtrados. A paginação obrigatória fica no PA-2. |
| D-6 | **Trilha que não cabe.** (a) Cada seção ganha um resumo quando o payload não cabe, como os alvos: `scopes` → `locations` (quantos locais), `network` (rede inteira sim ou não), `regions`, `establishments`, `channels` e, no depois, `changes` (locais que entraram e saíram); `audience` → os critérios sem as listas de UFs e municípios, que viram `statesCount` e `cityCodesCount`; `conditions` e `details` cabem pelo contrato (até 20 condições; descrição limitada), mas passam pela mesma rede de segurança. (b) **Rede de segurança:** se um payload ainda passar de 2.000 caracteres, `AppendAudit` grava `{ "section", "truncated": true }` em vez de deixar a gravação falhar. Um teste por seção confere o caso grande no PostgreSQL. |
| D-7 | **SEC-06: data de retirada e registro de uso.** A rota antiga ganha `Sunset` quando `Satellite:LegacyVoucherLookupSunset` estiver configurado (data no formato HTTP). Cada chamada grava um aviso no log com o `clientId` da aplicação, **sem o CPF**. Com isso dá para combinar a data com o PDV e retirar a rota quando o log ficar vazio (PA-3). |

---

## 2. Contratos & Schemas (DTOs / Records)

Nenhum corpo de requisição ou resposta muda. Mudam:

| Onde | Mudança |
|---|---|
| Token do satélite | Claim `stamp` (D-1) |
| `POST /satellite/token` | 429 `SATELLITE_TOKEN_RATE_LIMITED` com `Retry-After` pelo limite por IP (D-2); o 429 `SATELLITE_TOKEN_RATE_LIMIT_EXCEEDED` do bloqueio por `clientId` continua |
| `GET /campaigns`, `GET /vouchers` | Parâmetros `page` e `pageSize` (400 fora da faixa) e cabeçalho `X-Total-Count` (D-5) |
| `GET /satellite/customers/{document}/vouchers` | Cabeçalho `Sunset`, quando configurado (D-7) |
| Concessão | Motivo `NoCampaignMatched` no lugar do motivo `CampaignNotActive` para campanha fora da vigência, encerrada ou cancelada (D-3) |

Configuração nova (`appsettings.json`, todos com padrão): `Satellite:StampCacheSeconds`, `Satellite:TokenRateLimit:PermitLimit`, `Satellite:TokenRateLimit:WindowSeconds`, `Satellite:LegacyVoucherLookupSunset`.

---

## 3. Especificação do Endpoint / Serviço

Autorização sem mudança em todas as rotas. Para os satélites, o 401 de D-1 é o mesmo de um token vencido (`AUTH_UNAUTHENTICATED`).

---

## 4. Checklist de Implementação

- [ ] 1. D-1: `SecurityStamp` em `SatelliteApp`, troca em `SatelliteAppManagementService`, claim no token, `OnTokenValidated` com cache; migrações SQLite e PostgreSQL.
- [ ] 2. D-2: política nativa por IP no `POST /satellite/token`; chave `clientId` + IP e teto no `SatelliteTokenRateLimiter`.
- [ ] 3. D-3: filtro de status e vigência em `ConcessionRepository.GetApprovedCampaignsAsync`; ajustar os testes que esperavam o motivo `CampaignNotActive` para campanha fora da vigência.
- [ ] 4. D-4: consultas em lote para condições e escopo (catálogo e organização).
- [ ] 5. D-5: paginação e `X-Total-Count` nas duas listagens; status no SQL.
- [ ] 6. D-6: resumos de `scopes` e `audience`, rede de segurança em `AppendAudit`.
- [ ] 7. D-7: `Sunset` configurável e log de uso da rota antiga.
- [ ] 8. Testes (seção 5) no SQLite e no PostgreSQL; smoke; `agents.md`, `docs/PROJECT_STATE.md`, `docs/ROADMAP.md`; aviso ao frontend (paginação opcional) e ao PDV (D-1, D-3, D-7).

---

## 5. Casos de Teste Essenciais

- [ ] D-1: token válido → 200; aplicação inativada → o mesmo token recebe 401; segredo trocado → 401; escopo reduzido → 401; token sem `stamp` → 401; token novo depois de reativar → 200.
- [ ] D-2: 5 segredos errados do IP A bloqueiam o `clientId` só para o IP A; o IP B continua conseguindo o token; o limite por IP responde 429 com `Retry-After`; o dicionário não passa do teto com `clientId` aleatórios.
- [ ] D-3: campanha encerrada, cancelada ou de vigência futura não entra nas candidatas (motivo `NoCampaignMatched`); pausada na vigência → motivo `CampaignNotActive`; o fim da vigência continua inclusivo até o último segundo.
- [ ] D-4: condições e escopo com vários itens fazem uma consulta por tipo (contador de comandos do EF); os erros de catálogo e de organização continuam os mesmos.
- [ ] D-5: `page=2&pageSize=10` traz a segunda página e `X-Total-Count` certo; filtro de status com paginação; `pageSize=0` e `101` → 400; sem `page` → tudo, como hoje.
- [ ] D-6: escopo com 200 lojas e público com 500 municípios gravam no PostgreSQL com resumo; payload que ainda não cabe vira `truncated`.
- [ ] D-7: a rota antiga responde `Sunset` só quando configurado e registra o uso sem o CPF.

---

## 6. Pontos em aberto

| # | Ponto | Motivo | Gatilho |
|---|---|---|---|
| PA-1 | Estado dos limites de tentativa (login e satélite) compartilhado entre instâncias (Redis) | Hoje há uma instância só | Deploy com mais de uma instância |
| PA-2 | Paginação obrigatória, com teto, nas listagens | Quebraria o portal, que ainda espera tudo numa chamada | O frontend adotar `page` e `pageSize` |
| PA-3 | Retirar a rota `GET` antiga | O PDV ainda pode usá-la | O log de D-7 ficar sem chamadas pelo prazo combinado com o PDV |
| PA-4 | Cache curto das candidatas da concessão | Com o filtro de D-3 a consulta fica pequena; o cache adiaria o efeito de pausar ou cancelar | Medição de carga mostrar a concessão como gargalo |
| PA-5 | Achados de severidade baixa e informativa (lista na issue #71) | Sem urgência de negócio | Fatias seguintes da issue #71, antes do deploy público |
