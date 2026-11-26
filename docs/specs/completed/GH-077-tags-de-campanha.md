# 📋 Especificação Técnica: GH-077 - Tags de campanha

> Status: Completed
> História de Usuário Relacionada: issue #77 (front + back)
> Responsável: @iagovrocha
> Data: 2026-11-26

---

## 1. Contexto & Regras de Negócio

O cliente quer agrupar campanhas por ocasião (Black Friday, Dia das Mães...). Hoje isso não existe no domínio, na API nem no portal. A issue pede para fechar com o PO se a lista é fechada ou livre; enquanto o PO não responde, esta spec segue a opção recomendada (PA-1), escolhida para o filtro funcionar sem ambiguidade.

### Decisões

| # | Decisão |
|---|---|
| D-1 | **Lista fechada por rede.** O administrador cadastra as tags da rede (`/api/v1/campaign-tags`), como faz com os grupos de clientes (GH-072): criar, renomear, inativar e reativar. O nome tem de 1 a 40 caracteres e é único na rede, sem diferenciar maiúsculas (`422 CAMPAIGN_TAG_NAME_TAKEN`). Analista e gestor leem as tags da rede para o assistente. Tag não se apaga: o `DELETE` inativa, para as campanhas que já usam a tag continuarem mostrando o nome. |
| D-2 | **Várias tags por campanha, até 5.** `PUT /api/v1/campaigns/{id}/tags` com `{ "tagIds": [...] }` substitui a lista inteira; a lista vazia tira todas. As tags precisam ser da rede da campanha (outra rede: `422 CAMPAIGN_TAG_OUTSIDE_NETWORK`), existir e estar ativas (`422 CAMPAIGN_TAG_NOT_FOUND` e `422 CAMPAIGN_TAG_INACTIVE`, com `tagIds` dos recusados). Repetida na lista vira uma. |
| D-3 | **A tag muda em qualquer status.** A tag é só agrupamento: não muda o que o voucher concede nem onde vale. Por isso não segue a regra do rascunho (PR-01). Uma campanha já ativa pode entrar no grupo "Black Friday". A gravação usa a mesma proteção de edição concorrente das outras seções (`422 CAMPAIGN_CONCURRENT_UPDATE`). Campanha cancelada também aceita, pelo mesmo motivo. |
| D-4 | **Leitura e filtro.** `CampaignResponse` ganha `tags` (`id`, `name`, `isActive`), na ordem do nome. `GET /api/v1/campaigns` aceita `tagId` (uma tag) e devolve só as campanhas com ela; combina com `status` e com a paginação da GH-071, no banco. Tag inativa continua aparecendo nas campanhas que já a tinham, com `isActive: false`. Campanhas antigas ficam sem tag (`tags: []`). |
| D-5 | **Trilha e duplicação.** O `PUT` grava `CampaignUpdated` com a seção `tags` (os nomes, antes e depois), só quando a lista muda. Duplicar copia as tags ativas. |
| D-6 | **Fora do PDV.** O satélite não vê as tags: `GET /satellite/campaigns` não muda. |
| D-7 | **Persistência.** `campaign_tags` (`id`, `network_id`, `name`, `normalized_name`, `is_active`, `created_at`), com índice único em (`network_id`, `normalized_name`); `campaign_tag_links` (`id`, `campaign_id`, `tag_id`), com índice único em (`campaign_id`, `tag_id`), cascata a partir da campanha e restrição a partir da tag. O vínculo tem id próprio, como os grupos da GH-072; a troca de tags mantém os vínculos que continuam e só apaga e cria o que mudou. Migrações nos dois bancos. |

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
// Application/Campaigns/CampaignTagContracts.cs
public record CreateCampaignTagRequest(Guid? NetworkId, string? Name) : INetworkScopedRequest;
public record UpdateCampaignTagRequest(string? Name, bool? IsActive);
public record CampaignTagResponse(Guid Id, Guid NetworkId, string Name, bool IsActive, int CampaignCount, DateTime CreatedAt);

public record ReplaceCampaignTagsRequest(IReadOnlyList<Guid?>? TagIds);   // até 5
public record CampaignTagRefResponse(Guid Id, string Name, bool IsActive);

// CampaignResponse ganha, no fim:
//   IReadOnlyList<CampaignTagRefResponse>? Tags
```

Erros novos (`CampaignErrors`):

| Código | HTTP | Quando |
|---|---|---|
| `CAMPAIGN_TAG_NAME_TAKEN` | 422 | Nome já usado por outra tag da rede |
| `CAMPAIGN_TAG_NOT_FOUND` | 422 | Tag da lista não existe (`tagIds`) |
| `CAMPAIGN_TAG_INACTIVE` | 422 | Tag da lista está inativa (`tagIds`) |
| `CAMPAIGN_TAG_OUTSIDE_NETWORK` | 422 | Tag de outra rede |

Contrato (400): nome vazio ou com mais de 40 caracteres; mais de 5 tags; `tagIds` ausente; `tagId` do filtro que não é Guid.

---

## 3. Especificação do Endpoint / Serviço

| Rota | Quem | Resposta |
|---|---|---|
| `GET /api/v1/campaign-tags?networkId=&activeOnly=` | administrador, analista, gestor (`ReadHierarchy`, `[ValidateTenantScope]`) | 200, lista por nome |
| `GET /api/v1/campaign-tags/{id}` | idem | 200 ou 404 |
| `POST /api/v1/campaign-tags` | administrador (`ManageHierarchy`) | 201 |
| `PUT /api/v1/campaign-tags/{id}` | administrador | 200; renomear ou `isActive` |
| `DELETE /api/v1/campaign-tags/{id}` | administrador | 204; inativa |
| `PUT /api/v1/campaigns/{id}/tags` | analista e gestor (`ManageCampaigns`) | 200 com a campanha |
| `GET /api/v1/campaigns?networkId=&tagId=` | como hoje | 200; filtro novo |

---

## 4. Checklist de Implementação

- [x] 1. Domínio: `CampaignTag`, `CampaignTagLink`; `Campaign.ReplaceTags` (até 5, sem a regra do rascunho); duplicação.
- [x] 2. Configurações EF, `DbSet`s, migrações SQLite e PostgreSQL.
- [x] 3. Serviço e rotas das tags; `PUT /campaigns/{id}/tags`; trilha; `tags` no `CampaignResponse`; filtro `tagId` no banco.
- [x] 4. Testes de unidade e integração (seção 5); `dotnet test` no SQLite e no PostgreSQL; smoke.
- [x] 5. `agents.md`, `docs/PROJECT_STATE.md`, `docs/ROADMAP.md`; avisos ao frontend num documento fora do repositório.

---

## 5. Casos de Teste Essenciais

- [x] Administrador cria, renomeia, inativa e reativa; nome repetido na rede (ignorando maiúsculas) → 422; o mesmo nome em outra rede é aceito; analista e gestor leem e não escrevem.
- [x] `PUT /campaigns/{id}/tags` com 2 tags → 200 com `tags`; 6 tags → 400; tag inexistente, inativa e de outra rede → 422 com `tagIds`; repetida vira uma; lista vazia tira todas.
- [x] Campanha aprovada e ativa aceita a troca de tags (D-3); edição concorrente → 422.
- [x] `GET /campaigns?tagId=` filtra, combina com `status` e com `page`/`pageSize`, e o `X-Total-Count` bate.
- [x] Tag inativada continua na campanha com `isActive: false` e não pode ser adicionada de novo.
- [x] Trilha grava a seção `tags` só quando muda; duplicar copia as tags ativas; o satélite não recebe tags.

---

## 6. Pontos em aberto

| # | Ponto | Motivo | Gatilho |
|---|---|---|---|
| PA-1 | Lista fechada (escolhida) ou texto livre; uma ou várias tags | A issue pede para fechar com o PO, que ainda não respondeu. A lista fechada evita variações ("black friday", "Black Friday") que quebram o filtro | Resposta do PO. Se for texto livre, o `PUT` passa a aceitar nomes e cria as tags que faltarem; o resto da spec continua valendo |
| PA-2 | Filtrar o dashboard e os relatórios por tag | A issue só pede a listagem e o gerenciamento | Pedido do portal ou do PO |
| PA-3 | Várias tags no mesmo filtro (`tagId` repetido, E ou OU) | Uma tag por filtro cobre o caso da Black Friday | Pedido do portal |
