# Especificação Técnica: GH-050 — Consulta de vouchers no portal (Fatia 1)

> Status: Completed  
> História de Usuário Relacionada: issue #50 / EP-03 (US-03.3)  
> Responsável: Danilo Araujo  
> Data: 2026-11-16  
> Branch: `feat/EP-03-acompanhamento-portal`

---

## 1. Contexto & Regras de Negócio

- O portal precisa listar vouchers da rede e abrir o histórico de um voucher, hoje só em mock.
- Fonte: `vouchers` e `voucher_events` do motor (#45/#49). Rotas **satélite** não são reutilizadas.
- Papéis: analista e gestor (`ManageCampaigns` + `ValidateTenantScope`). Administrador sem rede não lista.
- Status devolvido é o **efetivo** (`Voucher.EffectiveStatus`). `Discarded` fica de fora da listagem padrão.
- CPF nunca completo: só mascarado (`***.XXX.XXX-**` a partir dos 11 dígitos). Sem nome de cliente no banco → `customerName` nulo.
- Sem paginação envelope nesta fatia (lista completa da rede filtrada); ordenação por `issuedAt` decrescente.

### Decisões

| # | Decisão |
|---|---|
| D-1 | `GET /api/v1/vouchers?networkId=` com filtros opcionais `campaignId`, `status`, `search`, `issuedFrom`, `issuedTo` (datas locais da rede, interpretadas como dia inteiro no fuso). |
| D-2 | `GET /api/v1/vouchers/{id}` devolve resumo + benefício do alvo + escopo (loja/canal) + eventos (mais recentes primeiro). |
| D-3 | Filtro `status`: enum efetivo (`Available`, `Redeemed`, `Expired`, `Cancelled`, `Provisional`). Desconhecido → `400`. Sem filtro: todos exceto `Discarded`. |
| D-4 | Busca `search`: código (parcial, case-insensitive) ou o CPF inteiro (11 dígitos, com ou sem pontuação). **Revisado no PR #65 (2026-11-18):** trechos do CPF deixaram de valer; junto com os 6 dígitos que a máscara mostra, permitiriam descobrir o CPF por tentativa (D4; retenção e base legal no PA-4 da spec GH-047). O portal hoje filtra no navegador e não manda `search`. |
| D-5 | Voucher de outra rede → `403 SCOPE_FORBIDDEN`; inexistente → `404 NOT_FOUND`. |

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
public record PortalVoucherListItemResponse(
    Guid Id,
    string Code,
    Guid CampaignId,
    string CampaignName,
    string? CustomerName,
    string CustomerDocumentMasked,
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    int UsesCount,
    int MaxUses,
    string Status);

public record PortalVoucherScopeItemResponse(string Label, string Value);

public record PortalVoucherEventResponse(
    Guid Id,
    string Type,
    DateTimeOffset OccurredAt,
    string Origin,
    string Place,
    string Result);

public record PortalVoucherDetailResponse(
    PortalVoucherListItemResponse Voucher,
    string Benefit,
    IReadOnlyList<PortalVoucherScopeItemResponse> Scope,
    IReadOnlyList<PortalVoucherEventResponse> Events);
```

---

## 3. Especificação do Endpoint / Serviço

- `GET /api/v1/vouchers?networkId=` — `ManageCampaigns` + `ValidateTenantScope` — 200 lista
- `GET /api/v1/vouchers/{id}` — `ManageCampaigns` — 200 detalhe
- Erros: 400 / 401 / 403 / 404 (padrão do projeto)

Porta: `IPortalVoucherQueryService` + `IPortalVoucherReadRepository` (somente leitura).

---

## 4. Checklist de Implementação

- [ ] 1. DTOs / records de resposta
- [ ] 2. Repositório de leitura e serviço
- [ ] 3. Controller `PortalVouchersController`
- [ ] 4. Registro DI em `Program.cs` / Infrastructure
- [ ] 5. Testes de integração (lista, filtro, detalhe, escopo, auth)
- [ ] 6. `dotnet test`

---

## 5. Casos de Teste Essenciais

- [ ] Lista vouchers confirmados da rede; descartados não aparecem sem filtro
- [ ] Filtro por campanha e por status efetivo
- [ ] Detalhe traz eventos e CPF mascarado
- [ ] Outra rede → 403; id inexistente → 404; sem token → 401

---

## 6. Revisão (PR #65, 2026-11-18)

- O evento `Cancelled` gravado pelo cancelamento da campanha (spec `GH-061`, payload com a causa `CampaignCancelled`) aparece no histórico como "Campanha cancelada"; o desfazimento pelo PDV continua "Concessão desfeita".
- Pontos em aberto: a listagem continua sem paginação (o portal filtra no navegador) e carrega os vouchers da rede inteira; o gatilho para paginar é a rede passar de alguns milhares de vouchers. As rotas `/reports` e `/audit-entries` cobrem parte do que o PR #59 entregou em `/dashboard` e `/campaigns/{id}/audit`; conciliar as duas superfícies quando o portal passar a usar uma só.

