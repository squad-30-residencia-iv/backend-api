# Especificação Técnica: GH-026 - Status efetivo da campanha (PR-04)

> Status: Completed
> História de Usuário Relacionada: issue #26 (critério C), PR-04, PA-4 da `US-01.9-consultar-campanhas.md`
> Responsável: Gabriel Torres
> Data: 2026-11-09

---

## 1. Contexto & Regras de Negócio

- A aprovação (US-01.11) grava `Scheduled` ou `Active` só no instante da decisão. Sem job, uma campanha agendada não vira ativa quando chega o início, nem encerrada quando passa o fim.
- O status **efetivo** é calculado na leitura a partir do status gravado, de `StartsAt`, de `EndsAt` e do `TimeProvider`. O banco continua com o status gravado.
- A regra fica num só método do domínio (`Campaign.EffectiveStatus`), usado pela consulta e pela concessão (`CanIssueVouchers`, EP-04).

### 1.1 Regra

| Status gravado | Antes do início | Na vigência (`StartsAt <= agora < EndsAt`) | Depois do fim (`agora >= EndsAt`) |
|---|---|---|---|
| `Scheduled` | `Scheduled` | `Active` | `Ended` |
| `Active` | `Scheduled` | `Active` | `Ended` |
| Demais (`Draft`, `PendingApproval`, `Paused`, `Ended`, `Cancelled`, `SoldOut`, `Rejected`) | gravado | gravado | gravado |

### 1.2 Decisões

| # | Ponto | Decisão |
|---|---|---|
| D-1 | `Draft` e `PendingApproval` fora da vigência. | Ficam como estão: não foram aprovadas, então não viram encerradas. A aprovação depois do fim já responde `422 CAMPAIGN_APPROVE_PERIOD_ENDED`; a reprovação continua possível. |
| D-2 | `Active` antes do início. | Vira `Scheduled`. A aprovação só grava `Active` com a vigência começada, então é um caso de proteção. |
| D-3 | `Paused` depois do fim. | Continua `Paused` até a US-01.8 definir pausar e retomar. |
| D-4 | Fim da vigência. | Exclusivo, como na `CanIssueVouchers`: às 23:59:00 do fim a campanha já está encerrada. A decisão D9 continua aberta. |
| D-5 | Filtro `?status=` do `GET /campaigns`. | Passa a filtrar pelo status efetivo: `?status=Active` traz a agendada cuja vigência começou. O filtro é aplicado em memória sobre as campanhas da rede; a paginação da US-03.2 pode levá-lo para o banco. |
| D-6 | `CanIssueVouchers`. | Passa a usar o status efetivo: a agendada com vigência começada concede. |

---

## 2. Contratos & Schemas

Sem mudança de contrato. `CampaignResponse.status` passa a trazer o status efetivo (`POST`, `GET`, `PUT` e as ações de submeter, aprovar e reprovar).

```csharp
public CampaignStatus EffectiveStatus(DateTime utcNow);
public bool CanIssueVouchers(DateTime utcNow) => EffectiveStatus(utcNow) == CampaignStatus.Active;
```

---

## 3. Especificação do Endpoint / Serviço

- `GET /api/v1/campaigns?networkId=&status=` e `GET /api/v1/campaigns/{id}`: `status` efetivo, calculado com o "agora" do `TimeProvider`.
- `ICampaignRepository.GetByNetworkAsync` deixa de receber o status; o serviço filtra pelo efetivo.

---

## 4. Checklist de Implementação

- [x] 1. `Campaign.EffectiveStatus` e `CanIssueVouchers` com o efetivo
- [x] 2. `CampaignService.Map` com o status efetivo e filtro em memória
- [x] 3. Repositório sem filtro de status
- [x] 4. Testes unitários (tabela 1.1) e de integração com relógio fixo
- [x] 5. `dotnet test`

---

## 5. Casos de Teste Essenciais

- [x] Aprovada antes do início: `Scheduled`; no início: `Active`; no fim: `Ended`. O banco segue `Scheduled`.
- [x] `?status=Active` traz a agendada no início e `?status=Scheduled` deixa de trazê-la.
- [x] `Draft` e `PendingApproval` depois do fim mantêm o status.
- [x] `CanIssueVouchers` verdadeiro para agendada na vigência e falso depois do fim.

---

## 6. Pontos em Aberto

| # | Ponto | Onde retomar |
|---|---|---|
| PA-1 | Esgotada derivada (RN09). | EP-04 (US-04.5). |
| PA-2 | Relatórios que leem o banco direto precisam aplicar a mesma regra. | EP-03. |
| PA-3 | Pausada e encerrada antes do fim. | US-01.8. |
