# Especificação Técnica: GH-072 - Grupos de clientes na campanha em lote

> Status: Completed
> História de Usuário Relacionada: lote por grupos (emissão na vigência fica para depois)
> Responsável: @squad-2
> Data: 2026-11-23

---

## 1. Contexto & Regras de Negócio

Nova fatia. A concessão automática no PDV não muda. Esta entrega **não emite voucher** e **não** cria job na virada da vigência. Campanha com grupos continua candidata do motor até esse job existir.

- Grupo pertence a uma rede. CPF (11 dígitos e dígito verificador, máscara aceita) pode estar em N grupos. Repetido no mesmo grupo vira um só.
- Só o administrador cria, altera, inativa e troca membros. Analista e gestor leem os grupos da rede (sem a lista de CPFs) para o assistente.
- `PUT /campaigns/{id}/customer-groups` em rascunho substitui os vínculos. Lista vazia = sem lote. Grupo de outra rede → 403. Grupo inativo ou inexistente → 422.
- Teto: união dos CPFs × `perCustomerVoucherLimit`. Se os dois limites já existem e o produto passa de `totalVoucherLimit`, 422 com os dois números. Sem limite ainda, a checagem fica no submit.
- Duplicar copia os vínculos, não uma foto de CPFs.
- Auditoria de membros: só quantidade e quantos CPFs entraram e saíram. A resposta da campanha traz id, nome e `uniqueCustomerCount`, nunca os CPFs.
- `loyaltyMember` não vira grupo.

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
public record CreateCustomerGroupRequest(Guid? NetworkId, string Name) : INetworkScopedRequest;
public record UpdateCustomerGroupRequest(string Name, bool? IsActive);
public record CustomerGroupResponse(Guid Id, Guid NetworkId, string Name, bool IsActive, int MemberCount, DateTime CreatedAt);

public record ReplaceCustomerGroupMembersRequest(IReadOnlyList<string?>? Documents);
public record CustomerGroupMembersResponse(int Page, int PageSize, int Total, IReadOnlyList<string> Documents);

public record PreviewCustomerGroupsRequest(Guid? NetworkId, IReadOnlyList<Guid?>? GroupIds) : INetworkScopedRequest;
public record PreviewCustomerGroupsResponse(int UniqueCustomerCount);

public record ReplaceCampaignCustomerGroupsRequest(IReadOnlyList<Guid?>? GroupIds);
public record CampaignCustomerGroupResponse(Guid Id, string Name);
```

`CampaignResponse` ganha `customerGroups` e `uniqueCustomerCount`.

Tabelas: `customer_groups`, `customer_group_members` (chave grupo + document), `campaign_customer_groups`.

---

## 3. Especificação do Endpoint / Serviço

- `GET/POST /api/v1/customer-groups`, `PUT/DELETE /{id}` — leitura `ReadHierarchy`, escrita `ManageHierarchy`. `networkId` na query ou no corpo, `[ValidateTenantScope]`.
- `PUT /{id}/members` — 1 a 10.000, tudo ou nada. CPF inválido: 400 com as posições.
- `GET /{id}/members?page&pageSize` — até 500, `X-Total-Count`, só administrador.
- `POST /api/v1/customer-groups/preview` — união, só leitura, grupos ativos da rede.
- `PUT /api/v1/campaigns/{id}/customer-groups` — rascunho, analista/gestor (`ManageCampaigns`).

---

## 4. Checklist de Implementação

- [x] 1. Domínio, configurações e migração nos dois bancos
- [x] 2. Serviço e rotas de grupo
- [x] 3. Vínculo na campanha, teto, duplicação e resposta
- [x] 4. Testes de integração e `dotnet test`

---

## 5. Casos de Teste Essenciais

- [x] Admin cria grupo, grava CPFs, analista lista e não escreve.
- [x] CPF inválido não grava; repetido no corpo vira um.
- [x] Prévia conta a união.
- [x] Campanha com grupos acima do teto → 422; duplicar copia os vínculos.
