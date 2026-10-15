# 📋 Especificação Técnica: GH-008 - Banco de Dados (Núcleo Inicial)

> Status: Completed
> História de Usuário Relacionada: GH-008 (task de engenharia, fora das US)
> Responsável: Gabriel Torres (@GabTorres7)
> Data: 2026-10-14

---

## 1. Contexto & Regras de Negócio

- **Objetivo:** materializar no banco as tabelas do núcleo inicial (Hierarquia Organizacional, Catálogo e Campanhas), com chaves primárias, estrangeiras e índices, deixando o banco pronto para as US de CRUD (#12, #13) e para a construção da API (#9).
- **Banco local:** PostgreSQL 17 via `docker-compose.yml` na raiz. O SQLite continua sendo o padrão quando não há `.env` (GH-010).
- **Hospedagem:** como o Supabase é um PostgreSQL gerenciado, o mesmo conjunto de migrações `Postgres` serve para ele; a troca é só de `Database__ConnectionString`.
- **Base das tabelas:** `docs/requisitos.md` (repositório `.github`) e os campos usados nas telas de criação de campanha do `frontend-web`. O DER da issue #4 não estava disponível no repositório; a estrutura deve ser conferida com ele.

### 1.1 Tabelas

| Tabela | Conteúdo | Relacionamentos |
|---|---|---|
| `networks` | Rede | — |
| `regions` | Região | `network_id` → `networks` |
| `establishments` | Estabelecimento (loja) | `region_id` → `regions` |
| `channels` | Canal do estabelecimento (`Physical` ou `Virtual`) | `establishment_id` → `establishments` |
| `categories` | Categoria de produto (RF06b) | — |
| `products` | Produto (RF04, RF06b) | `category_id` → `categories` |
| `campaigns` | Campanha, vigência, status e validade do voucher | `network_id` → `networks` |
| `campaign_items` | Item de campanha: alvo, benefício e quantidade | `campaign_id`, `category_id`, `product_id` |
| `campaign_scopes` | Escopo da campanha: rede, região ou estabelecimento + tipo de canal (RF07) | `campaign_id`, `region_id`, `establishment_id` |

### 1.2 Regras garantidas pelo banco

- Chaves primárias `uuid` geradas pela aplicação (`Guid.CreateVersion7()`).
- Nomes de tabelas, colunas, chaves e índices em `snake_case`.
- Enums gravados como texto (`Draft`, `Percentage`, `Physical`...).
- Hierarquia e catálogo usam `ON DELETE RESTRICT`: registros são inativados (`is_active`), não apagados (issue #12). Itens e escopos de campanha são apagados junto com a campanha (`CASCADE`).
- Unicidade: nome da rede; nome da região dentro da rede; código do estabelecimento dentro da região; tipo de canal dentro do estabelecimento; nome da categoria; código do produto.
- `CHECK` constraints:
  - `campaigns`: `ends_at > starts_at` (RN02); limites e dias de validade maiores que zero quando informados.
  - `campaign_items`: alvo coerente com o tipo (`All` sem categoria/produto, `Category` com categoria, `Product` com produto); benefício maior que zero e, se percentual, até 100 (RN05); limite do item maior que zero quando informado.
  - `campaign_scopes`: região e estabelecimento não podem ser informados ao mesmo tempo (os dois nulos = rede inteira).
- Regras que dependem de data atual ou de outras linhas (ex.: RN01, RN03, RN06) ficam para as US, no domínio.

### 1.3 Dados iniciais (seed)

- Aplicados apenas quando `Database:SeedDemoData = true` (ligado em `appsettings.Development.json`), via `UseSeeding`/`UseAsyncSeeding` do EF Core, logo após as migrações. Idempotente: não roda se já existir alguma rede.
- Conteúdo: 1 rede (Supermercados Norte), 1 região (Região Norte), 3 estabelecimentos (001 Loja Centro, 002 Loja Shopping, 003 Loja Online, os mesmos do mock do frontend) com seus canais, 3 categorias (Moda, Calçados, Casa) e 3 produtos (um por categoria).
- IDs fixos, para que frontend e testes manuais possam referenciá-los.

### Pacotes NuGet

Nenhum pacote novo.

---

## 2. Contratos & Schemas

Entidades (camada `Domain`), mapeadas em `Infrastructure/Persistence/Configurations`:

```csharp
// Organization
public class Network { Guid Id; string Name; bool IsActive; DateTime CreatedAt; ICollection<Region> Regions; }
public class Region { Guid Id; Guid NetworkId; string Name; bool IsActive; DateTime CreatedAt; ... }
public class Establishment { Guid Id; Guid RegionId; string Code; string Name; bool IsActive; DateTime CreatedAt; ... }
public class Channel { Guid Id; Guid EstablishmentId; ChannelType Type; bool IsActive; DateTime CreatedAt; }
public enum ChannelType { Physical, Virtual }

// Catalog
public class Category { Guid Id; string Name; bool IsActive; DateTime CreatedAt; ICollection<Product> Products; }
public class Product { Guid Id; Guid CategoryId; string Code; string Name; bool IsActive; DateTime CreatedAt; }

// Campaigns
public class Campaign
{
    Guid Id; Guid NetworkId; string Name; string? Description;
    DateTime StartsAt; DateTime EndsAt; CampaignStatus Status;
    int? TotalVoucherLimit;
    VoucherValidityType? VoucherValidityType; int? VoucherValidityDays; DateTime? VoucherValidUntil; DateTime? VoucherValidityCap;
    DateTime CreatedAt; DateTime UpdatedAt;
    ICollection<CampaignItem> Items; ICollection<CampaignScope> Scopes;
}
public class CampaignItem { Guid Id; Guid CampaignId; TargetType TargetType; Guid? CategoryId; Guid? ProductId; BenefitType BenefitType; decimal BenefitValue; int? VoucherLimit; }
public class CampaignScope { Guid Id; Guid CampaignId; Guid? RegionId; Guid? EstablishmentId; ChannelType ChannelType; }

public enum CampaignStatus { Draft, Scheduled, Active, Paused, Ended, Cancelled, SoldOut }
public enum TargetType { All, Category, Product }
public enum BenefitType { Percentage, FixedAmount }
public enum VoucherValidityType { Relative, FixedDate }
```

Configuração (`DatabaseOptions`): nova chave opcional `Database:SeedDemoData` (`bool`, padrão `false`).

---

## 3. Especificação do Endpoint / Serviço

Sem novos endpoints. `GET /api/v1/system/status` passa a listar a nova migração.

---

## 4. Checklist de Implementação

- [x] 1. Criar as entidades e enums no `Domain`
- [x] 2. Criar as configurações EF Core (tabelas, chaves, índices, `CHECK`) e registrar os `DbSet`
- [x] 3. Aplicar a convenção `snake_case` no `AppDbContext`
- [x] 4. Adicionar `SeedDemoData` ao `DatabaseOptions` e o seed via `UseSeeding`/`UseAsyncSeeding`
- [x] 5. Gerar a migração nos dois conjuntos (`sh scripts/add-migration.sh CoreTables`)
- [x] 6. Criar `docker-compose.yml` (PostgreSQL 17) e atualizar `.env.example`
- [x] 7. Escrever testes unitários e de integração
- [x] 8. Validar contra PostgreSQL 17 real (migração, seed e `GET /api/v1/system/status`)
- [x] 9. Atualizar `README.md`, `agents.md` e `docs/PROJECT_STATE.md`
- [x] 10. Validar localmente via `dotnet test`

---

## 5. Casos de Teste Essenciais

- [x] Modelo sincronizado: não há mudanças pendentes de modelo nos conjuntos SQLite e PostgreSQL.
- [x] Startup aplica `InitialCreate` e `CoreTables`; reiniciar não reaplica nem duplica o seed.
- [x] Seed ligado: existem 1 rede, 1 região, 3 estabelecimentos, 3 categorias e 3 produtos.
- [x] Seed desligado: as tabelas ficam vazias.
- [x] `CHECK`: campanha com fim antes do início é recusada.
- [x] `CHECK`: item percentual acima de 100 é recusado; percentual de 5 é aceito.
- [x] `CHECK`: item com alvo `Category` sem categoria é recusado.
- [x] Unicidade: código de produto repetido é recusado.

### Validação com PostgreSQL

- Validado em 2026-10-14 contra um PostgreSQL 17.10 local (binários portáteis, porta 5433), alterando só `Database__Provider` e `Database__ConnectionString`:
  - `InitialCreate` e `CoreTables` aplicadas no startup; `GET /health` e `GET /api/v1/system/status` retornando 200 com `provider = Postgres`.
  - Seed inserido (1 rede, 1 região, 3 lojas e 3 canais, 3 categorias, 3 produtos), com acentuação gravada em UTF-8.
  - `CHECK`s e FK testados direto no banco: fim antes do início, percentual de 150 %, alvo `Category` sem categoria e rede inexistente foram recusados; percentual de 5 % e campanha válida foram aceitos.
  - Reinício sobre o mesmo banco não reaplicou migrações nem duplicou o seed.
- Validado também com o `docker-compose.yml` (Docker 29.8.2, imagem `postgres:17-alpine`, PostgreSQL 17.11): container `healthy`, API conectada com a connection string do `.env.example`, as duas migrações aplicadas, as 9 tabelas criadas, seed inserido e `ck_campaigns_period` recusando campanha com fim antes do início.
