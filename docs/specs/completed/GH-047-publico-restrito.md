# 📋 Especificação Técnica: GH-047 - Público restrito: critérios de elegibilidade e avaliação na emissão (EP-06, US-04.2)

> Status: Completed (decisões D-1 a D-20 aprovadas pelo usuário em 2026-11-13, como propostas)
> História de Usuário Relacionada: issue #47 (última fatia da #27). Cobre US-06.1 a US-06.5 e US-04.2; US-06.6 proposta na D-17.
> Responsável: @iagovrocha
> Data: 2026-11-13
> Branch: `feat/GH-047-publico-restrito` → PR para a `staging`
> Depende de: US-01.5 (`audienceType`, hoje só `AllCustomers`), motor de concessão (GH-045) e decisões D2 e D4 do `docs/ROADMAP.md` (2.5)

---

## 1. Contexto & Regras de Negócio

### 1.1 Objetivo

Hoje a campanha só pode ser "para todos os clientes": `PUT /campaigns/{id}/audience` com `Restricted` responde `422 CAMPAIGN_AUDIENCE_RESTRICTED_UNAVAILABLE` (US-01.5, D-4). Esta fatia libera o **público restrito**: o analista configura critérios de **perfil** (US-06.1), de **histórico de compras** com janela de tempo (US-06.2), combinados (US-06.3), ou uma **lista de clientes específicos** (US-06.4, 06.5 cenário 2), e o motor **avalia a elegibilidade na emissão** (US-04.2, US-06.5) antes de reservar saldo, registrando o snapshot dos atributos usados (RN11a).

Fora desta spec: consulta em massa de elegíveis (US-06.6 cenário 4, fora de escopo pelo próprio backlog), grupos reutilizáveis entre campanhas (PA-1), cadastro de clientes na plataforma (D4) e edição do público após o vigor (US-01.7, RN03b).

### 1.2 Regras do backlog

- **Modelo push (PR-08, RF28b, decisão D4):** os atributos do cliente chegam na própria requisição de concessão. A plataforma não tem cadastro de cliente e não grava histórico de compras (D2). Se o satélite enviar só o CPF, só a lista de clientes específicos é avaliável (US-04.2, dependência).
- **Combinação AND (US-06.3, RN11):** o cliente só é elegível se atender a todos os critérios; o sistema registra quais foram avaliados e qual falhou.
- **Janela obrigatória no histórico (US-06.2, RN12):** valor gasto, frequência e categoria são sempre "nos últimos N dias".
- **Avaliação só na emissão (RN13, US-06.4 cenário 3):** o resgate não reavalia elegibilidade. O resgate da GH-049 já não olha o público; esta fatia só acrescenta o teste.
- **Inelegível não consome saldo e recebe o motivo (US-06.4 cenário 2, US-04.2 cenário 2).**
- **Sem critério de perfil = sem restrição de perfil (US-06.1 cenário 2).** "Todos os clientes" continua sem avaliação nenhuma (US-04.2 cenário 3).

### 1.3 Fluxo da concessão com elegibilidade

Os passos da GH-045 (1.2) continuam; a elegibilidade entra depois das condições de geração (US-04.2) e antes da reserva:

1. Contrato, loja, idempotência, liberação de reservas vencidas (inalterados).
2. Candidatas no escopo, condições de geração, ordenação D1 (inalterados).
3. Para cada satisfeita, na ordem D1: status efetivo e esgotada (inalterados) → **elegibilidade do cliente (novo)** → limite por cliente → cota do alvo.
4. A primeira apta reserva (inalterado). Inelegível não reserva, então não consome saldo; a próxima satisfeita pode ganhar.
5. Se nenhuma for apta e a melhor bloqueada estiver ativa e inelegível, a concessão `NotGranted` grava `refusal = CustomerNotEligible` e a auditoria `VoucherIssuanceRefused` com o critério que falhou.

### 1.4 Base reutilizada

- `Campaign.AudienceType` (US-01.5), `PUT /audience`, filtro `audience` do dashboard (GH-050) e o snapshot de seção da auditoria de edições (GH-050, D-9).
- `ConcessionEvaluator.Block` (GH-045) recebe o passo novo; `Concession` ganha a coluna do snapshot.
- `GET /satellite/campaigns` (GH-049, US-07.1) passa a dizer o que o satélite precisa mandar.
- A entidade `EligibilityCriterion` e o enum `CriterionType` do #36 (`Domain/Entities`, tabela `EligibilityCriteria`, nunca usados) **são removidos** e substituídos pelo modelo da D-3, como foi feito com `Voucher` e `VoucherEvent` na #45.

### 1.5 Decisões e divergências (aprovar antes do código)

| # | Ponto | Proposta |
|---|---|---|
| D-1 | Recorte. | US-06.1, 06.2, 06.3, 06.4, 06.5 e US-04.2 completas, mais a simulação da US-06.6 (D-17). Fica de fora a consulta em massa (06.6 cenário 4) e grupos reutilizáveis (PA-1). |
| D-2 | De onde vêm os atributos do cliente. | **Da requisição de concessão** (PR-08, D4). O objeto `customer` ganha campos opcionais: `birthDate`, `state`, `cityCode`, `loyaltyMember` e `history`. O satélite só precisa mandar o que as campanhas da rede exigem; o `GET /satellite/campaigns` lista isso (D-15). Nada é gravado como cadastro, só como snapshot da decisão (D-12). |
| D-3 | Tipos de critério. | **Perfil:** `AgeRange` (`minAge` e/ou `maxAge`, anos completos), `Location` (lista de UFs e/ou de municípios), `LoyaltyMember` (cliente do clube de vantagens, o exemplo das US-01.5 e 04.2). **Histórico:** `TotalSpent` (`minAmount` em `windowDays`), `PurchaseFrequency` (`minPurchases` em `windowDays`), `PurchasedCategory` (`categoryId` em `windowDays`). **Clientes:** `SpecificCustomers` (lista de CPFs, D-8). Um critério de cada tipo por campanha, exceto `PurchasedCategory`, que pode repetir com categorias diferentes. "Dados cadastrais" do backlog fica só com o clube; outros atributos (gênero, tags de CRM) vão para o PA-2. |
| D-4 | Como o histórico chega com a janela da campanha (RN12) sem a plataforma guardar compras (D2). | O satélite manda **agregados por janela**: `history: [{ windowDays, totalSpent, purchaseCount, productCodes }]` (a categoria sai do catálogo pelo produto, ver 7.1). O critério usa a entrada com o mesmo `windowDays`; sem ela, o atributo conta como ausente (D-5). `windowDays` livre de **1 a 365**. *Alternativa:* janelas fixas (30, 60, 90, 180, 365), que simplificam o satélite mas limitam o analista. |
| D-5 | Atributo ausente na requisição. | O critério **não é atendido** (o cliente não é elegível) e o snapshot registra `missing`. Elegibilidade nunca é presumida: o backlog quer "impedir concessão indevida" (EP-06). |
| D-6 | Combinação (US-06.3). | **AND entre todos os critérios**, inclusive `SpecificCustomers`. Dentro de `Location`, UFs e municípios valem como **OU** (mora em SP **ou** em Curitiba). O texto da US-06.5 cenário 2 ("somente esses clientes, independentemente de outros segmentos") é lido como "a lista basta": o analista que quer só a lista configura só a lista. *Divergência:* se o PO quiser que a lista **ignore** os outros critérios, ela vira um modo exclusivo do público. |
| D-7 | "Grupo" (US-06.4 cenário 1). | O grupo **é a combinação de critérios da campanha**. Não há entidade de grupo nomeado e reaproveitável; o backlog não descreve cadastro de grupos (PA-1). |
| D-8 | Lista de clientes específicos e a US-06.5 cenário 3 ("identificador que não existe ou está inativo"). | Sem cadastro de cliente (D4), não há como saber se o CPF "existe" ou está "inativo". A validação é de **formato**: 11 dígitos depois de tirar pontuação **e dígito verificador**, porque a lista é digitada ou colada pelo analista e erro de digitação deixa o cliente de fora sem aviso. Inválidos voltam todos de uma vez no `400`, com a posição de cada um, e nada é gravado (o analista corrige e reenvia, como pede o cenário). Repetidos são unificados. Máximo de **10.000 CPFs** por campanha. *Divergência:* a concessão aceita CPF sem conferir o dígito (PA da GH-045, issue #57); um CPF da lista nunca casa com um inválido, então não há conflito. |
| D-9 | Rota da configuração. | A mesma **`PUT /api/v1/campaigns/{id}/audience`**, que passa a aceitar `{ "audienceType": "Restricted", "criteria": [...] }`. Substitui tudo de uma vez, como `/items` e `/conditions`. `Restricted` exige ao menos um critério; `AllCustomers` com critérios é `400`; trocar para `AllCustomers` apaga os critérios. Só em `Draft` (D6 e US-06.3 cenário 3). O `422 CAMPAIGN_AUDIENCE_RESTRICTED_UNAVAILABLE` sai. |
| D-10 | Leitura da configuração (US-06.1 cenário 1, "exibir na consulta"). | `CampaignResponse` ganha `eligibility`: a lista de critérios com os parâmetros; o `SpecificCustomers` traz só `customerCount`. A lista completa sai em `GET /api/v1/campaigns/{id}/audience/customers` (paginada, `page` e `pageSize` até 500), para não pesar todas as leituras de campanha com até 10.000 CPFs. |
| D-11 | Idade (`AgeRange`). | Anos completos na **data da compra no fuso da rede** (mesmo cálculo de data da D9). `minAge` e `maxAge` de 0 a 130, ao menos um informado; `minAge > maxAge` → `400` "A idade mínima não pode ser maior que a idade máxima" (US-06.1 cenário 3, texto do backlog). `birthDate` no futuro é `400` na concessão. |
| D-12 | Snapshot da decisão (RN11a, US-04.2 cenários 1 e 2, US-06.3 cenário 1). | A `concessions` ganha `eligibility` (JSON) com, por critério: tipo, parâmetros, **o valor do atributo usado** e o resultado (`met`, `notMet`, `missing`). Vale para a concedida e para a recusada. **Minimização:** grava só os atributos que algum critério da campanha avaliada usou; o `SpecificCustomers` grava apenas `met` ou `notMet`, nunca a lista. O evento `Issued` e a auditoria `VoucherIssued` levam o mesmo resumo; `VoucherIssuanceRefused` leva o critério que falhou. Campanha `AllCustomers` grava `eligibility = null` (cenário 3: "sem avaliar critérios"). |
| D-13 | O que o satélite recebe na recusa. | Só `refusal: "CustomerNotEligible"`, com a mensagem "O cliente não atende ao público da campanha." O critério que falhou fica na auditoria e na simulação (D-17), não no caixa: o PDV não precisa saber da segmentação, e mostrá-la ao operador expõe a regra da campanha e atributos do cliente. |
| D-14 | Posição do passo no `Block` e o motivo quando várias campanhas falham. | Depois de status e esgotada e **antes** do limite por cliente e da cota (1.3). O motivo continua o da satisfeita melhor colocada com a preferência atual por campanha ativa (GH-045); `CustomerNotEligible` conta como bloqueio de campanha ativa. |
| D-15 | `GET /satellite/campaigns` (US-07.1). | `audience` passa a ser `All` ou `Restricted` e a campanha restrita traz `requiredAttributes` (por exemplo `["birthDate", "loyaltyMember"]`) e `historyWindows` (por exemplo `[90]`). **Sem** limites, UFs ou a lista de CPFs: o satélite precisa saber o que mandar, não a regra. |
| D-16 | Resgate (US-06.4 cenário 3, RN13). | Sem mudança: o resgate não reavalia a elegibilidade. Entra um teste: voucher concedido a cliente elegível é resgatado com atributos que hoje o tornariam inelegível. |
| D-17 | Simulação (US-06.6, cenários 1 e 2). | **`POST /api/v1/campaigns/{id}/eligibility/simulate`** com o mesmo objeto `customer` da concessão (`document` e atributos). Responde `eligible` e o resultado critério a critério, sem emitir voucher, sem consumir saldo e sem gravar auditoria. Qualquer status da campanha. *Divergência:* o cenário 3 ("cliente inexistente") não se aplica sem cadastro (D4); um atributo não informado aparece como `missing`. A campanha `AllCustomers` responde `eligible: true` sem critérios. |
| D-18 | Submissão (US-01.6) e duplicação (US-01.10). | A submissão continua exigindo o público (`audienceType` não nulo); com a D-9 não existe `Restricted` vazio, então não há pendência nova. A duplicação copia critérios e lista de clientes. |
| D-19 | Persistência. | Tabela **`campaign_eligibility_criteria`** (id, campaign_id, type, min_age, max_age, min_amount, min_purchases, category_id, window_days, states, city_codes), com `CHECK` por tipo, como `campaign_conditions`; `states` e `city_codes` em texto separado por vírgula. Tabela **`campaign_eligible_customers`** (campaign_id, document), com chave composta para a busca por CPF na concessão. A tabela `EligibilityCriteria` do #36 é apagada na migração `CampaignEligibility`, nos dois conjuntos. |
| D-20 | Auditoria da edição (GH-050, D-9). | A seção `audience` passa a guardar os critérios no antes e depois. A lista de clientes entra só como **quantidade e resumo SHA-256** da lista ordenada: detecta a troca sem copiar CPFs para a trilha. |

### 1.6 Localização (`Location`)

- `states`: siglas de UF (`SP`, `PR`...), sem diferenciar maiúsculas, validadas contra as 27 UFs.
- `cityCodes`: código IBGE do município, 7 dígitos. O nome do município é ambíguo e o PDV costuma ter o código na nota fiscal.
- Na concessão, o cliente atende se a UF está em `states` **ou** o `cityCode` está em `cityCodes`. Ao menos uma das listas no critério.
- *Alternativa:* município pelo nome, comparado sem acento e sem maiúsculas, se o satélite não tiver o código.

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
// Domain/Campaigns
public enum EligibilityCriterionType { AgeRange, Location, LoyaltyMember, TotalSpent, PurchaseFrequency, PurchasedCategory, SpecificCustomers }

public class CampaignEligibilityCriterion   // tabela campaign_eligibility_criteria
{
    public Guid Id; public Guid CampaignId; public EligibilityCriterionType Type;
    public int? MinAge; public int? MaxAge;
    public IReadOnlyList<string> States; public IReadOnlyList<string> CityCodes;
    public decimal? MinAmount; public int? MinPurchases; public Guid? CategoryId; public int? WindowDays;
}

public class CampaignEligibleCustomer { public Guid CampaignId; public string Document; }   // campaign_eligible_customers

// Domain/Eligibility
public sealed record CustomerProfile(string Document, DateOnly? BirthDate, string? State, string? CityCode,
    bool? LoyaltyMember, IReadOnlyList<CustomerHistoryWindow> History);
public sealed record CustomerHistoryWindow(int WindowDays, decimal? TotalSpent, int? PurchaseCount, IReadOnlyList<Guid> CategoryIds);
public enum CriterionOutcome { Met, NotMet, Missing }
public sealed record CriterionResult(EligibilityCriterionType Type, object Parameters, object? Observed, CriterionOutcome Outcome);
public sealed record EligibilityResult(bool Eligible, IReadOnlyList<CriterionResult> Criteria);
public static class EligibilityEvaluator
{
    // Puro. isListed vem do repositório (CPF na campaign_eligible_customers).
    public static EligibilityResult Evaluate(Campaign campaign, CustomerProfile customer, DateOnly purchaseDate, bool isListed);
}

// Domain/Vouchers
ConcessionRefusal.CustomerNotEligible        // novo valor
Concession.Eligibility (string? JSON)          // snapshot D-12

// Application/Campaigns: PUT /audience
public record UpdateCampaignAudienceRequest(
    [Required] string? AudienceType,
    IReadOnlyList<EligibilityCriterionRequest?>? Criteria);

public record EligibilityCriterionRequest(
    [Required] string? Type,
    int? MinAge, int? MaxAge,
    IReadOnlyList<string?>? States, IReadOnlyList<string?>? CityCodes,
    decimal? MinAmount, int? MinPurchases, Guid? CategoryId, int? WindowDays,
    IReadOnlyList<string?>? Documents);

// CampaignResponse: novo campo ao final
CampaignEligibilityResponse? Eligibility
public record CampaignEligibilityResponse(IReadOnlyList<EligibilityCriterionResponse> Criteria);
public record EligibilityCriterionResponse(Guid Id, string Type, int? MinAge, int? MaxAge,
    IReadOnlyList<string>? States, IReadOnlyList<string>? CityCodes,
    decimal? MinAmount, int? MinPurchases, Guid? CategoryId, int? WindowDays, int? CustomerCount);

public record EligibleCustomersResponse(int Page, int PageSize, int Total, IReadOnlyList<string> Documents);

// Application/Vouchers: customer da concessão (e da simulação)
public record ConcessionCustomerRequest(
    [Required] string? Document,
    DateOnly? BirthDate,
    [StringLength(2)] string? State,
    [StringLength(7)] string? CityCode,
    bool? LoyaltyMember,
    [MaxLength(10)] IReadOnlyList<CustomerHistoryRequest?>? History);

public record CustomerHistoryRequest(
    [Required, Range(1, 365)] int? WindowDays,
    [Range(0, 99_999_999.99)] decimal? TotalSpent,
    [Range(0, int.MaxValue)] int? PurchaseCount,
    [MaxLength(500)] IReadOnlyList<string?>? ProductCodes);   // produtos comprados na janela; a categoria vem do catálogo (7.1)

// Simulação
public record EligibilitySimulationResponse(Guid CampaignId, string AudienceType, bool Eligible,
    IReadOnlyList<CriterionResultResponse> Criteria);
public record CriterionResultResponse(string Type, string Outcome, object? Observed);

// SatelliteCampaignResponse: audience All|Restricted + RequiredAttributes + HistoryWindows (D-15)
```

### 2.1 Parâmetros por tipo (validação em `400 VALIDATION_ERROR`, campo `criteria[i].<campo>`)

| Tipo | Obrigatórios | Regras |
|---|---|---|
| `AgeRange` | `minAge` e/ou `maxAge` | 0 a 130; `minAge <= maxAge` (D-11). |
| `Location` | `states` e/ou `cityCodes` | UF válida; código IBGE com 7 dígitos; até 100 itens em cada lista; repetidos unificados. |
| `LoyaltyMember` | nenhum | Atende quem envia `loyaltyMember: true`. |
| `TotalSpent` | `minAmount`, `windowDays` | `minAmount > 0` com até 2 casas; janela 1 a 365 (US-06.2 cenários 2 e 3). |
| `PurchaseFrequency` | `minPurchases`, `windowDays` | `minPurchases >= 1` (frequência zero é inválida, cenário 3). |
| `PurchasedCategory` | `categoryId`, `windowDays` | Categoria existente no catálogo, senão `400` "A categoria informada não existe." (cenário 3). |
| `SpecificCustomers` | `documents` | 1 a 10.000 CPFs válidos (D-8). |

Parâmetro de outro tipo informado → `400` no campo ("Este parâmetro não se aplica ao critério X."). Tipo repetido (exceto `PurchasedCategory` com categorias diferentes) → `400`.

### 2.2 Mensagens novas

| Constante / código | Status | Mensagem |
|---|---|---|
| `EligibilityCriteriaRequired` | 400 | "Informe ao menos um critério para o público restrito." |
| `EligibilityCriteriaNotAllowed` | 400 | "O público 'todos os clientes' não aceita critérios." |
| `AgeRangeInverted` | 400 | "A idade mínima não pode ser maior que a idade máxima." |
| `HistoryWindowRequired` | 400 | "Informe o período, em dias, a ser considerado no histórico de compras." |
| `CategoryNotFound` | 400 | "A categoria informada não existe." |
| `DocumentsInvalid` | 400 | "CPFs inválidos nas posições: 3, 17." |
| `CustomerNotEligible` (refusal da concessão) | 201/200 com `status: NotGranted` | "O cliente não atende ao público da campanha." |
| `BirthDateInFuture` | 400 | "A data de nascimento não pode estar no futuro." |

---

## 3. Especificação dos Endpoints

### `PUT /api/v1/campaigns/{id}/audience` (alterada)

- Autorização: `ManageCampaigns`. Ordem: contrato (`400`) → campanha (`404`) → escopo (`403`) → `Draft` (`422 CAMPAIGN_NOT_DRAFT`) → categoria existente (`400`).
- `200` com `CampaignResponse` (com `eligibility`).

### `GET /api/v1/campaigns/{id}/audience/customers?page=&pageSize=` (nova)

- Autorização: `ManageCampaigns`, escopo da rede. `200` com a página da lista em ordem; campanha sem `SpecificCustomers` devolve `total = 0`.

### `POST /api/v1/campaigns/{id}/eligibility/simulate` (nova, D-17)

- Autorização: `ManageCampaigns`, escopo da rede. Corpo: `{ "customer": { ... } }`. `200` com `EligibilitySimulationResponse`; `400` no contrato; `404`/`403`.

### `POST /api/v1/satellite/concessions` (alterada)

- `customer` aceita os atributos da D-2. Resposta igual; na recusa por público, `refusal: "CustomerNotEligible"` (D-13).

### `GET /api/v1/satellite/campaigns` (alterada, D-15)

### Migração

- `sh scripts/add-migration.sh CampaignEligibility`: cria `campaign_eligibility_criteria` e `campaign_eligible_customers`, adiciona `concessions.eligibility`, apaga `EligibilityCriteria` do #36. Nos dois conjuntos.

---

## 4. Checklist de Implementação

- [x] 1. Domínio: `EligibilityCriterionType`, `CampaignEligibilityCriterion`, `CampaignEligibleCustomer`, `Campaign.SetAudience(type, criteria, documents)` com as regras da 2.1; remover `EligibilityCriterion` e `CriterionType` do #36.
- [x] 2. Domínio: `CustomerProfile`, `EligibilityEvaluator` (idade no fuso da rede, AND, `missing`) e `ConcessionRefusal.CustomerNotEligible`; passo novo no `ConcessionEvaluator.Block`.
- [x] 3. Validação de CPF com dígito verificador (para a lista) e de UF e código IBGE.
- [x] 4. Infra: configurações EF, `CHECK` por tipo, `Concession.Eligibility`, repositório (critérios no carregamento das aprovadas; `IsListedAsync(campaignIds, document)` em uma consulta) e migração dupla.
- [x] 5. Application: `UpdateAudienceAsync` com critérios e auditoria (D-20), leitura da lista paginada, simulação, `ConcessionService` com perfil e snapshot (D-12), `SatelliteCampaignService` (D-15), duplicação (D-18).
- [x] 6. API: rotas novas no `CampaignsController`; contratos da concessão.
- [x] 7. Testes unitários (avaliador, validação) e de integração (rotas, concessão, resgate, simulação, auditoria).
- [x] 8. `dotnet test` em SQLite e PostgreSQL local, sem warnings; smoke (`smoke.py` e `motor.py`) com público restrito.
- [x] 9. Documentação: README, `agents.md` se houver padrão novo, `PROJECT_STATE.md`, `ROADMAP.md` (US-06.1 a 06.6, 04.2, P1 e P2) e a spec em `completed/`.

---

## 5. Casos de Teste Essenciais

**Configuração**

- [x] `Restricted` com `LoyaltyMember` → `200`; `GET` devolve o critério (US-06.1 cenário 1).
- [x] `Restricted` sem critérios → `400`; `AllCustomers` com critérios → `400`; trocar para `AllCustomers` apaga critérios e lista.
- [x] `AgeRange` com `minAge 40, maxAge 30` → `400` com a mensagem do backlog (US-06.1 cenário 3).
- [x] Histórico sem `windowDays` → `400` pedindo o período (US-06.2 cenário 2); `minAmount` negativo, `minPurchases 0` e categoria inexistente → `400` no parâmetro (cenário 3).
- [x] Lista com CPFs inválidos → `400` com as posições, nada gravado; repetidos unificados; 10.001 CPFs → `400` (US-06.5 cenário 3).
- [x] `GET .../audience/customers` paginado; `CampaignResponse` só com `customerCount`.
- [x] Fora de `Draft` → `422`; outra rede → `403`; administrador → `403`; sem token → `401`.
- [x] Edição auditada com antes e depois; repetição idêntica não gera evento; a lista aparece só como quantidade e resumo (D-20).
- [x] Duplicar copia critérios e lista.

**Concessão**

- [x] Clube: cliente com `loyaltyMember: true` recebe voucher e o snapshot registra o atributo (US-04.2 cenário 1, US-06.5 cenário 1).
- [x] Clube: cliente sem o atributo ou com `false` → `NotGranted` `CustomerNotEligible`, saldo da campanha e do alvo intactos, auditoria com o critério que falhou (US-04.2 cenário 2, US-06.4 cenário 2).
- [x] `AllCustomers` concede sem avaliar e grava `eligibility = null` (US-04.2 cenário 3).
- [x] AND: perfil atendido e histórico não → inelegível, com o critério de histórico indicado (US-06.3 cenário 2).
- [x] Histórico usa só a janela do critério: entrada de 30 dias não serve para critério de 90 (US-06.2 cenário 1, D-4).
- [x] Idade no limite: aniversário no dia da compra, no fuso da rede (D-11).
- [x] Lista de clientes: CPF da lista recebe; fora da lista não (US-06.5 cenário 2).
- [x] Inelegível na campanha de maior benefício, elegível em outra → a outra concede (D-14).
- [x] Idempotência: repetir a transação devolve a mesma decisão, mesmo com atributos diferentes.
- [x] Edição antes da ativação: as concessões seguintes usam a combinação nova (US-06.3 cenário 3).

**Resgate, simulação e satélite**

- [x] Resgate não reavalia: voucher do elegível é resgatado mesmo com atributos que hoje o recusariam (US-06.4 cenário 3).
- [x] Simulação elegível e não elegível, critério a critério, sem voucher, sem saldo e sem auditoria (US-06.6 cenários 1 e 2).
- [x] `GET /satellite/campaigns` mostra `Restricted`, `requiredAttributes` e `historyWindows`, sem limites nem CPFs.

---

## 6. Pontos em aberto

| # | Ponto | Motivo de ficar de fora | Gatilho para retomar |
|---|---|---|---|
| PA-1 | Grupos de clientes nomeados e reaproveitáveis entre campanhas. | O backlog descreve o grupo como a combinação de critérios da campanha (US-06.4) e não pede cadastro de grupos. | PO pedir segmentos salvos, ou o portal pedir para reaproveitar públicos. |
| PA-2 | Outros atributos de "dados cadastrais" (gênero, tags de CRM, cliente novo). | O backlog só exemplifica o clube; cada atributo novo é campo novo no satélite (D4). | Representante listar os atributos que o PDV consegue enviar. |
| PA-3 | Importar a lista de CPFs por arquivo (CSV). | A rota aceita a lista em JSON; arquivo é conveniência do portal. | Frontend pedir upload. |
| PA-4 | Retenção e base legal (LGPD) do snapshot e da lista de CPFs. | A minimização (D-12, D-20) reduz o risco, mas o prazo de guarda depende do jurídico. Liga-se ao PA de CPF da GH-045 (#57). | Definição do jurídico ou do cliente sobre retenção. |
| PA-5 | Consulta em massa de clientes elegíveis (US-06.6 cenário 4). | Fora de escopo pelo backlog; depende de serviço externo de segmentação. | Novo épico ou integração com CRM. |
| PA-6 | Alterar o público depois do vigor (US-01.7, RN03b). | D6: a mecânica congela na aprovação. | Spec da US-01.7 após o vigor. |
| PA-7 | Janelas fixas no histórico (D-4, alternativa). | A janela livre atende RN12 sem pedir decisão ao satélite. | Time do PDV dizer que não consegue calcular janelas arbitrárias. |
| PA-8 | Município pelo nome (1.6, alternativa). | O código IBGE evita ambiguidade. | Satélite não tiver o código IBGE do cliente. |

---

## 7. Notas da implementação

### 7.1 Diferenças em relação ao texto acima

| Ponto | Como ficou | Por quê |
|---|---|---|
| Histórico de categoria (D-4) | O satélite manda `productCodes` na janela, e não `categoryCodes`. A categoria sai do catálogo pelo produto; código desconhecido é ignorado. | As categorias do catálogo não têm código, só id. O PDV já conhece o código do produto, o mesmo dos itens da compra. |
| Quantidade e resumo da lista (D-10, D-20) | Ficam na própria linha do critério `SpecificCustomers` (`customer_count`, `customers_digest`). | A leitura da campanha e a auditoria não precisam abrir a tabela de CPFs. |
| Snapshot (D-12) | `concessions.eligibility` é texto sem limite. | Com até 20 critérios e 100 municípios, um limite fixo poderia recusar a gravação no PostgreSQL. |
| Auditoria da recusa (D-12, D-13) | `VoucherIssuanceRefused` ganha `failedCriterion` e `failedOutcome` (`NotMet` ou `Missing`). O `VoucherIssued` e o evento `Issued` levam o resumo em `eligibility`. | Indica o critério que falhou, para auditoria (US-06.3 cenário 2). |
| `requiredAttributes` (D-15) | Nomes dos campos de `customer`: `birthDate`, `state`, `cityCode`, `loyaltyMember`, `history.totalSpent`, `history.purchaseCount`, `history.productCodes`. A lista de CPFs não pede atributo além do `document`. | O satélite monta o corpo da concessão direto desses nomes. |
| Janelas repetidas | A mesma categoria pode repetir com janelas diferentes; tipo, categoria e janela iguais são `400`. | Extensão natural da regra da D-3. |
| Categoria inexistente | `400` no campo `Criteria[i].CategoryId`, como diz a 2.1; categoria inativa é aceita. | O critério olha compras passadas: a categoria pode ter sido inativada depois. |

### 7.2 Verificação

- `dotnet build -warnaserror`: sem avisos.
- `dotnet test`: 300 unitários e 495 de integração em SQLite. No PostgreSQL local, 494 de 495: a única falha foi `DatabaseSchemaTests`, pelo timeout conhecido do `DROP DATABASE` na limpeza da fábrica, sem relação com esta entrega.
- Smoke com a API no ar: `smoke.py`, `motor.py` e o novo `scripts/smoke/publico.py` (público restrito) OK.
