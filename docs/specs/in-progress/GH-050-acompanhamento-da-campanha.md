# 📋 Especificação Técnica: GH-050 - Acompanhamento da campanha: dashboard, filtros, trilha de auditoria e exportação (EP-03)

> Status: In Progress
> História de Usuário Relacionada: issue #50 (fatia da #27), US-03.1 a US-03.5
> Responsável: @iagovrocha
> Data: 2026-11-12

---

## 1. Contexto & Regras de Negócio

### 1.1 Objetivo

Dar ao portal os dados do acompanhamento gerencial da campanha (EP-03), lendo o que o motor já grava (`vouchers`, `redemptions`, `concessions`, `audit_entries`): indicadores por campanha (US-03.1), filtros por período, categoria, loja, região e tipo de cliente (US-03.2), a lista de todas as campanhas com indicadores resumidos (US-03.3), a trilha de auditoria das operações de configuração e ciclo de vida (US-03.4) e a exportação em CSV do que está na tela (US-03.5, decisão D8). O backend entrega números e arquivos; cards e gráficos são do frontend.

Fora desta spec: histórico de movimentação de cada voucher (EP-05, RF21: fica em `voucher_events`, consultável depois), cancelamento de campanha (não existe a operação), tipo de cliente além de "todos" (#47) e paginação da trilha (PA-5).

### 1.2 Indicadores (US-03.1)

Calculados na leitura, no instante da consulta, a partir do status **efetivo** de cada voucher (D-3). Cada voucher emitido cai em exatamente um balde, para a conta fechar (US-03.1 cenário 1):

| Indicador | Definição |
|---|---|
| `issued` | Vouchers confirmados: status gravado diferente de `Provisional` e `Discarded` (o provisório ainda não é emissão; o descartado nunca valeu). |
| `redeemed` | Dos emitidos, os usados ao menos uma vez (`uses_count > 0`), inclusive os parcialmente usados de campanha multiuso. |
| `expiredUnused` | Dos emitidos, os com status efetivo `Expired` e `uses_count = 0`. |
| `cancelledUnused` | Dos emitidos, os com status `Cancelled` e `uses_count = 0` (hoje sempre zero: não há cancelamento de campanha). |
| `available` | Dos emitidos, os com status efetivo `Available` e `uses_count = 0`. |
| `utilizationRate` | `redeemed / issued`, com 4 casas (ex.: `0.3750`); `null` quando `issued = 0` (US-03.1 cenário 3). |
| `reserved` | Provisórios com a reserva em dia (informativo; não entra na soma). |
| `redemptions` | Utilizações efetivas (`redemptions` com status `Effective`); numa campanha de 1 uso é igual a `redeemed`. |
| `discountTotal` | Soma de `discount_amount` dos resgates efetivos, 2 casas. |

Invariante: `issued = redeemed + expiredUnused + cancelledUnused + available`. O teste de integração confere a igualdade em cada cenário.

### 1.3 Filtros (US-03.2)

Todos opcionais e cumulativos (cenário 2). Sem nenhum, a visão é a consolidada (cenário 4). Sem dado no recorte, `200` com zeros (cenário 3).

| Filtro | Parâmetro | Aplica-se a |
|---|---|---|
| Período | `from`, `to` (datas `yyyy-MM-dd`, inclusivas, no fuso da rede) | `issued_at` do voucher. Um voucher emitido no período conta em todos os indicadores, mesmo que o resgate seja depois (D-5). |
| Loja | `establishmentId` | Loja da emissão (`vouchers.establishment_id`). |
| Região | `regionId` | Região da loja da emissão. |
| Categoria | `categoryId` | Alvo do voucher: `campaign_items.category_id` igual, ou `product_id` de produto dessa categoria. Alvo "todos" não casa categoria (D-6). |
| Tipo de cliente | `audience` (`AllCustomers`, `Restricted`) | `campaigns.audience_type`. Até a #47 só existe `AllCustomers` (PA-2). |
| Rede | token | O analista e o gestor só veem a própria rede; a lista exige `networkId` como `GET /campaigns` (D-2). |

`from > to`, loja ou região de outra rede, categoria inexistente → `400 VALIDATION_ERROR`. Loja ou região inexistente → `404 NOT_FOUND`.

### 1.4 Trilha de auditoria da campanha (US-03.4)

Lê `audit_entries` com `campaign_id` da campanha e `entity_type = "Campaign"`, em ordem cronológica crescente, com o responsável resolvido pela tabela `users` (nome, papel). Hoje a trilha só tem submissão, aprovação, reprovação, pausa, encerramento e duplicação; a **criação e cada edição passam a ser auditadas** (D-8), com o que mudou (cenário 1). Campanha criada antes desta entrega mostra um evento `CampaignCreated` sintético a partir de `created_at`, sem responsável (D-9). Os eventos de voucher (`VoucherIssued`, `VoucherRedeemed` etc.) ficam fora da trilha da campanha (cenário 3); o dashboard mostra os totais.

### 1.5 Exportação (US-03.5, D8)

CSV com o mesmo conteúdo da visão (D-10): a lista do dashboard com os filtros aplicados, ou a trilha de auditoria de uma campanha. Visão vazia → `422 EXPORT_EMPTY` "Não há dados para exportar." (cenário 4), sem gerar arquivo.

### 1.6 Base reutilizada

`Campaign.EffectiveStatus`, `Voucher.EffectiveStatus` e `RemainingUses` (#45), `Redemption` (#49, PR #58), `AuditEntry`/`IAuditTrail` e `AppendAudit` do `CampaignService` (GH-026), `NetworkScope.Ensure`, `NetworkTime`, `ICampaignRepository`, policy `ManageCampaigns` (analista e gestor; o administrador segue sem campanhas), `ApiFactory`/`MotorScenario`/`ReadyCampaign` nos testes.

### 1.7 Decisões e divergências (aprovar antes do código)

| # | Ponto | Proposta |
|---|---|---|
| D-1 | Recorte e dependência. | Issue #50: US-03.1 a 03.5 no recorte acima. A branch parte da `staging` **depois** do merge do PR #58 (precisa de `redemptions`); a spec é escrita antes. |
| D-2 | Quem lê. | Policy `ManageCampaigns` (analista e gestor da rede), como as demais rotas de campanha; o administrador da plataforma não lê campanhas (decisão da US-02.5). Não existe papel "auditor" (PA-1). |
| D-3 | Cálculo na leitura, sem contadores novos. | Os indicadores são agregados por consulta (`GROUP BY` sobre `vouchers` e `redemptions`), com o status efetivo calculado pela data (`valid_until < agora` → expirado). Não entram colunas em `campaigns`: os contadores `issued_vouchers`/`reserved_vouchers` do #45 continuam servindo ao saldo, e `CampaignResponse.issuedVouchers` fica como está. Volume de vouchers por campanha (até dezenas de milhares) cabe numa agregação indexada por `campaign_id, status`. |
| D-4 | Status efetivo no SQL. | `Expired` efetivo = status `Available` **e** `valid_until < agora` (truncado ao segundo, D9); `Available` efetivo = `Available` e `valid_until >= agora`; `Reserved` = `Provisional` e `reserved_until > agora`. A mesma regra de `Voucher.EffectiveStatus`, repetida no SQL do repositório, com teste que compara as duas contagens. |
| D-5 | Período pela emissão. | O período filtra pela data de emissão do voucher, no fuso da rede (dia inteiro, inclusivo). Resgates são atribuídos ao voucher, não ao dia do resgate: assim "emitidos = resgatados + ..." continua fechando dentro do recorte. Período pelo resgate fica em PA-3. |
| D-6 | Categoria pelo alvo do voucher. | Casa `category_id` do alvo ou a categoria do produto do alvo. Alvo "todos" não entra no filtro de categoria, porque não dá para atribuir uma categoria ao voucher. |
| D-7 | Rotas. | `GET /api/v1/campaigns/{id}/dashboard` (uma campanha), `GET /api/v1/dashboard?networkId=` (todas as campanhas da rede, qualquer status, com indicadores resumidos e totais: US-03.3), `GET /api/v1/campaigns/{id}/audit` (trilha), `GET /api/v1/dashboard/export` e `GET /api/v1/campaigns/{id}/audit/export` (CSV). Controllers `DashboardController` e `CampaignAuditController`. |
| D-8 | Auditar criação e edição. | `AuditEventTypes` ganha `CampaignCreated` e `CampaignUpdated`. `CampaignService.CreateAsync` grava `CampaignCreated` (payload: nome, vigência, rede). Cada `PUT` de rascunho (`details`, `items`, `quantities`, `audience`, `voucher-validity`, `usage-restrictions`, `conditions`, `scopes`) grava `CampaignUpdated` com `section`, `before` e `after` (a seção mapeada antes e depois, no formato do `CampaignResponse`), só quando algo mudou. |
| D-9 | Campanhas antigas sem evento de criação. | A trilha devolve um `CampaignCreated` sintético (`synthetic: true`, `occurredAt = created_at`, `actor: null`) quando não há evento gravado, para a lista nunca vir vazia (US-03.4 cenário 4). Sem migração de dados. |
| D-10 | Formato do CSV (D8). | UTF-8 com BOM, separador `;`, `CRLF`, decimais com vírgula, datas `dd/MM/yyyy HH:mm` no fuso da rede, `Content-Type: text/csv; charset=utf-8`, `Content-Disposition: attachment; filename="..."`. Primeiras linhas: `Gerado em` e `Filtros` (período, loja, região, categoria, tipo de cliente, ou "nenhum"), linha em branco e o cabeçalho. O repositório do frontend não está nesta máquina para conferir "o formato que as telas já baixam"; alinhar antes de mesclar (PA-4). |
| D-11 | Nomes no CSV e na API. | Cabeçalho do CSV em português ("Campanha", "Status", "Início", "Fim", "Emitidos", "Resgatados", "Expirados sem uso", "Cancelados sem uso", "Disponíveis", "Taxa de utilização", "Utilizações", "Desconto total"); chaves da API em inglês como o resto dos contratos. |
| D-12 | Responsável na trilha. | `actor`: `{ "type": "User", "id", "name", "role" }` resolvido de `users`; usuário apagado ou desconhecido → `name: null`. Eventos de aplicação não entram nesta trilha. |
| D-13 | Status da campanha na lista e no dashboard. | O efetivo (`Campaign.EffectiveStatus`), o mesmo de `GET /campaigns`, com `SoldOut` derivado. A lista do dashboard aceita `status=` com a mesma semântica de `GET /campaigns`. |

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
namespace VoucherCampaigns.Application.Dashboard;

public record DashboardFilters(DateOnly? From, DateOnly? To, Guid? EstablishmentId, Guid? RegionId, Guid? CategoryId, string? Audience);

public record CampaignIndicatorsResponse(
    int Issued, int Reserved, int Redeemed, int ExpiredUnused, int CancelledUnused, int Available,
    decimal? UtilizationRate, int Redemptions, decimal DiscountTotal);

public record CampaignDashboardResponse(
    Guid CampaignId, string Name, string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    DashboardFiltersResponse Filters, CampaignIndicatorsResponse Indicators, DateTimeOffset GeneratedAt);

public record DashboardFiltersResponse(DateOnly? From, DateOnly? To, Guid? EstablishmentId, string? EstablishmentName,
    Guid? RegionId, string? RegionName, Guid? CategoryId, string? CategoryName, string? Audience);

public record CampaignSummaryResponse(
    Guid CampaignId, string Name, string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Audience,
    CampaignIndicatorsResponse Indicators);

public record NetworkDashboardResponse(
    Guid NetworkId, DashboardFiltersResponse Filters, IReadOnlyList<CampaignSummaryResponse> Campaigns,
    CampaignIndicatorsResponse Totals, DateTimeOffset GeneratedAt);

public record CampaignAuditEntryResponse(
    Guid Id, string EventType, DateTimeOffset OccurredAt, AuditActorResponse? Actor, bool Synthetic, JsonElement? Payload);

public record AuditActorResponse(string Type, Guid Id, string? Name, string? Role);

public record CampaignAuditResponse(Guid CampaignId, string Name, string Status, IReadOnlyList<CampaignAuditEntryResponse> Entries);

public interface IDashboardService
{
    Task<CampaignDashboardResponse> GetCampaignAsync(Guid campaignId, DashboardFilters filters, CancellationToken ct);
    Task<NetworkDashboardResponse> GetNetworkAsync(Guid? networkId, string? status, DashboardFilters filters, CancellationToken ct);
    Task<CampaignAuditResponse> GetAuditAsync(Guid campaignId, CancellationToken ct);
    Task<CsvFile> ExportNetworkAsync(Guid? networkId, string? status, DashboardFilters filters, CancellationToken ct);
    Task<CsvFile> ExportAuditAsync(Guid campaignId, CancellationToken ct);
}

public record CsvFile(string FileName, byte[] Content);   // UTF-8 com BOM

public interface IDashboardRepository
{
    /// <summary>Uma linha por campanha da rede com as contagens por balde, já com o status efetivo do voucher calculado no SQL (D-4).</summary>
    Task<IReadOnlyList<CampaignCounts>> CountVouchersAsync(Guid networkId, Guid? campaignId, DashboardFilters filters, DateTime utcNow, CancellationToken ct);
    Task<IReadOnlyList<AuditEntry>> GetCampaignAuditAsync(Guid campaignId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, (string Name, string Role)>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

public record CampaignCounts(Guid CampaignId, int Issued, int Reserved, int Redeemed, int ExpiredUnused, int CancelledUnused, int Available, int Redemptions, decimal DiscountTotal);
```

Payloads novos de auditoria (D-8):

```csharp
// CampaignCreated
{ "name": "...", "startsAt": "...", "endsAt": "...", "networkId": "..." }
// CampaignUpdated
{ "section": "items", "before": { ...seção do CampaignResponse... }, "after": { ... } }
```

Erros:

| Código | HTTP | Quando |
|---|---|---|
| `VALIDATION_ERROR` | 400 | `from > to`, data inválida, `audience` ou `status` desconhecido, loja/região/categoria de outra rede. |
| `NOT_FOUND` | 404 | Campanha, loja, região ou categoria inexistente. |
| `SCOPE_FORBIDDEN` | 403 | Campanha ou rede fora do token. |
| `EXPORT_EMPTY` | 422 | "Não há dados para exportar." |

---

## 3. Especificação dos Endpoints

Policy `ManageCampaigns` (analista e gestor; `401` sem token, `403 AUTH_FORBIDDEN_ROLE` para o administrador).

| Rota | Parâmetros | Resposta |
|---|---|---|
| `GET /api/v1/campaigns/{id}/dashboard` | `from`, `to`, `establishmentId`, `regionId`, `categoryId` | `200 CampaignDashboardResponse` |
| `GET /api/v1/dashboard` | `networkId` (obrigatório), `status`, mais os filtros e `audience` | `200 NetworkDashboardResponse`, campanhas por `endsAt` desc e nome; vazio quando não há campanha (US-03.3 cenário 4) |
| `GET /api/v1/campaigns/{id}/audit` | — | `200 CampaignAuditResponse`, cronológica crescente |
| `GET /api/v1/dashboard/export` | os mesmos de `GET /dashboard` | `200 text/csv` ou `422 EXPORT_EMPTY` |
| `GET /api/v1/campaigns/{id}/audit/export` | — | `200 text/csv` (nunca vazio: sempre há a criação) |

---

## 4. Checklist de Implementação

- [ ] 1. Auditoria de criação e edição (D-8): `AuditEventTypes.CampaignCreated`/`CampaignUpdated`, `AppendAudit` em `CreateAsync` e nos oito `PUT` do `CampaignService`, só quando a seção mudou; testes de integração conferindo o payload.
- [ ] 2. `IDashboardRepository`/`DashboardRepository` com a agregação por campanha (D-3, D-4) e os filtros (1.3); teste que compara as contagens do SQL com `Voucher.EffectiveStatus` em memória.
- [ ] 3. `DashboardService`: validação dos filtros, escopo de rede, indicadores, taxa, totais, trilha com responsável e evento sintético (D-9), CSV (D-10, D-11).
- [ ] 4. `DashboardController` e `CampaignAuditController`; registro em `Program.cs`/`DependencyInjection`.
- [ ] 5. Testes de integração: cenários da seção 5, com `MotorScenario` para emitir, confirmar, resgatar, estornar e vencer vouchers.
- [ ] 6. `scripts/smoke/smoke.py`: dashboard, trilha e exportação da campanha do roteiro.
- [ ] 7. README (seção "Acompanhamento"), `agents.md` (skill), `ROADMAP.md`, `PROJECT_STATE.md`; spec para `completed/`.

---

## 5. Casos de Teste Essenciais

- [ ] Campanha com 5 emitidos: 2 resgatados (um multiuso parcial), 1 expirado sem uso (relógio depois da validade), 2 disponíveis → `issued 5`, `redeemed 2`, `expiredUnused 1`, `available 2`, `utilizationRate 0.4000`, soma fechando (US-03.1 cenário 1). Provisório pendente aparece em `reserved` e não em `issued`.
- [ ] Campanha sem movimentação → zeros e `utilizationRate: null` (cenário 3). Depois de novas emissões e resgates, a mesma consulta reflete os totais (cenário 4).
- [ ] Filtro por loja, por região, por período e por categoria, isolados e combinados (US-03.2 cenários 1 e 2); recorte vazio → zeros com `200` (cenário 3); sem filtros → consolidado (cenário 4). `from > to` → `400`; loja de outra rede → `400`; loja inexistente → `404`.
- [ ] `GET /dashboard` lista rascunho, agendada, ativa, pausada, encerrada e esgotada com o status efetivo e os indicadores; `status=` filtra; rede sem campanha → lista vazia e totais zerados (US-03.3).
- [ ] Trilha: criar, editar itens duas vezes (a segunda sem mudança não gera evento), submeter, aprovar, pausar → eventos em ordem com `actor.name` e o `before/after` da edição; campanha antiga sem `CampaignCreated` → evento sintético (US-03.4 cenários 1, 2 e 4). Eventos de voucher não aparecem (cenário 3).
- [ ] Exportação: CSV com BOM, `;`, cabeçalho, linhas de filtros e uma linha por campanha, valores iguais ao JSON; sem dados → `422 EXPORT_EMPTY` (US-03.5).
- [ ] Analista e gestor leem; administrador → `403`; gestor de outra rede → `403 SCOPE_FORBIDDEN`; sem token → `401`.

---

## 6. Pontos em aberto

| # | Ponto | Motivo de adiar | Gatilho |
|---|---|---|---|
| PA-1 | **Papel "auditor"** (US-03.4 fala em auditor ou gestor). | Só existem administrador, analista e gestor (US-02.5). | O PO criar o papel. |
| PA-2 | **Tipo de cliente** além de "todos". | Público restrito é a #47. | Issue #47. |
| PA-3 | **Período pelo dia do resgate** (ex.: "quanto foi resgatado em outubro"). | O recorte adotado é pela emissão (D-5), que mantém a soma fechando. | Pedido do negócio por relatório de resgates por dia. |
| PA-4 | **Formato do CSV igual ao das telas** (D8). | O repositório do frontend não está nesta máquina. | Conferir com o frontend antes do merge; ajuste é só no gerador. |
| PA-5 | **Paginação da trilha e da lista.** | Volume pequeno nesta fase. | Campanhas com centenas de eventos ou redes com centenas de campanhas. |
| PA-6 | **Histórico do voucher pelo portal** (RF21, EP-05). | Fora do EP-03 por decisão do backlog. | Tela de voucher no portal. |
| PA-7 | **Indicadores por loja ou por dia** (série temporal para gráficos). | O backlog pede totais com filtros; a série é derivada. | O frontend pedir gráfico de evolução. |
| PA-8 | **Responsável da criação em campanhas antigas** (D-9). | Não há coluna de autor em `campaigns`. | Se o auditor precisar, migração com `created_by_user_id` preenchido a partir da submissão. |
