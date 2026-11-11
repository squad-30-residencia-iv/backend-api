# Especificação Técnica: GH-026 - Trilha de auditoria

> Status: Completed
> História de Usuário Relacionada: issue #26 (critério B), peça transversal "Trilha de auditoria" do `docs/ROADMAP.md` (2.2)
> Responsável: Gabriel Torres
> Data: 2026-11-09

---

## 1. Contexto & Regras de Negócio

- A auditoria registra as ações relevantes num só lugar, para que 01.11, 03.4, 04.7 e 05.4 não criem quatro trilhas diferentes (RF27a).
- Nesta entrega são gravados a **submissão**, a **aprovação** e a **reprovação** da campanha. A emissão e o resgate entram com a issue #27, usando o mesmo serviço.
- A trilha é **append-only**: a aplicação só inclui registros. Alterar ou apagar um registro é erro de programação e o `SaveChanges` falha antes de chegar ao banco.
- O registro entra no mesmo `SaveChanges` da operação auditada. Se a operação falhar (ex.: `422`), nada é gravado.
- O `campaign_status_events` da US-01.6 / US-01.11 continua sendo o histórico de status da campanha. A auditoria não o substitui: ela é transversal e vai receber eventos de voucher e de aplicação satélite.

### 1.1 Decisões

| # | Ponto | Decisão |
|---|---|---|
| D-1 | `AuditEntry` do #36 (tabela `AuditEntries`) fora do padrão: PascalCase, `Guid.NewGuid`, `DateTime.UtcNow`, `UserId` texto, `jsonb`, sem rede nem campanha (`PROJECT_STATE.md`, pendência de conformidade). | A entidade vai para `Domain/Audit`, a tabela vira `audit_entries` e a antiga é apagada na migração. Ninguém gravava nela. |
| D-2 | Quem fez a ação: usuário (analista, gestor) ou aplicação satélite (US-07.5). | `actor_type` (`User` ou `Application`) e `actor_id` (`Guid`): o `sub` do token do usuário ou o id da aplicação. |
| D-3 | Payload resumido. | JSON curto em texto (até 2.000 caracteres), em camelCase e sem campos nulos. Texto em vez de `jsonb` para valer igual em SQLite e PostgreSQL. |
| D-4 | Proteção contra alteração. | Guarda no `AppDbContext`: entidade marcada com `IAppendOnly` em estado `Modified` ou `Deleted` faz o `SaveChanges` lançar `InvalidOperationException`. Vale para `AuditEntry` e `CampaignStatusEvent`. Não há rota de alteração nem de exclusão. Trigger ou permissão no banco fica para o deploy (PA-2). |
| D-5 | Chave estrangeira para campanha. | Sem FK: o registro precisa sobreviver a qualquer limpeza da campanha e vai apontar também para voucher. Índices por campanha e por rede. |

---

## 2. Contratos & Schemas

```csharp
namespace VoucherCampaigns.Domain.Audit;

public enum AuditActorType { User, Application }

public static class AuditEventTypes
{
    public const string CampaignSubmitted = "CampaignSubmitted";
    public const string CampaignApproved = "CampaignApproved";
    public const string CampaignRejected = "CampaignRejected";
}

public class AuditEntry : IAppendOnly
{
    public Guid Id { get; set; }               // Guid v7 do instante
    public string EventType { get; set; }      // AuditEventTypes
    public AuditActorType ActorType { get; set; }
    public Guid ActorId { get; set; }
    public Guid? NetworkId { get; set; }
    public Guid? CampaignId { get; set; }
    public string EntityType { get; set; }     // "Campaign"; depois "Voucher"
    public Guid EntityId { get; set; }
    public DateTime OccurredAt { get; set; }   // UTC, via TimeProvider
    public string? Payload { get; set; }       // JSON resumido
}

namespace VoucherCampaigns.Application.Audit;

/// Só inclui; a gravação acontece no SaveChanges de quem chamou.
public interface IAuditTrail
{
    void Append(AuditEntry entry);
}
```

Payload dos eventos de campanha: `{"fromStatus":"PendingApproval","toStatus":"Rejected","reason":"Fora do calendário"}` (`reason` só na reprovação com motivo).

Tabela `audit_entries`: `id`, `event_type` (50), `actor_type` (20), `actor_id`, `network_id`, `campaign_id`, `entity_type` (50), `entity_id`, `occurred_at`, `payload` (2.000). Índices `(campaign_id, occurred_at)` e `(network_id, occurred_at)`.

---

## 3. Especificação do Serviço

- Sem rota nova. A leitura da trilha é da US-03.4.
- `CampaignService.SubmitAsync`, `ApproveAsync` e `RejectAsync` chamam `IAuditTrail.Append` antes do `SaveChanges`, com o mesmo instante e o mesmo usuário do `CampaignStatusEvent`.
- Evento por transição: `Draft → PendingApproval` = `CampaignSubmitted`; `PendingApproval → Scheduled/Active` = `CampaignApproved`; `PendingApproval → Rejected` = `CampaignRejected`.

---

## 4. Checklist de Implementação

- [x] 1. `IAppendOnly`, `AuditEntry`, `AuditActorType` e `AuditEventTypes` no domínio; remover `Domain/Entities/AuditEntry.cs`
- [x] 2. `AuditEntryConfiguration` e guarda append-only no `AppDbContext`
- [x] 3. `IAuditTrail` e `AuditTrail`
- [x] 4. Gravar os três eventos no `CampaignService`
- [x] 5. Migração nos dois bancos (junto com a da US-07.5)
- [x] 6. Testes e `dotnet test`

---

## 5. Casos de Teste Essenciais

- [x] Submissão grava `CampaignSubmitted` com o usuário do token, a rede, a campanha, o instante do relógio fixo e o payload.
- [x] Aprovação grava `CampaignApproved` com `toStatus` `Scheduled`; reprovação grava `CampaignRejected` com o motivo.
- [x] Submissão recusada (`422`) não grava auditoria.
- [x] Alterar ou apagar um `AuditEntry` ou um `CampaignStatusEvent` pelo `AppDbContext` lança exceção e o registro fica igual no banco.
- [x] Operações seguintes pela API (aprovar depois de submeter) não mudam o registro anterior.

---

## 6. Pontos em Aberto

| # | Ponto | Onde retomar |
|---|---|---|
| PA-1 | Consulta da trilha (tela de auditoria). | US-03.4. |
| PA-2 | Proteção no banco (trigger ou usuário sem `UPDATE`/`DELETE`). `ExecuteUpdate`/`ExecuteDelete` passam por fora da guarda do EF. | Deploy (issue #28). |
| PA-3 | Auditoria das edições de rascunho (antes e depois). | Spec da US-01.7 após o vigor (PA-4 dela). |
| PA-4 | Eventos de emissão e resgate, com `actor_type` `Application`. | Issue #27. |
