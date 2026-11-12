# 📋 Especificação Técnica: GH-028 - Gestão de Aplicações Satélite e Travas Operacionais

> Status: Completed  
> História de Usuário Relacionada: Issue #28 (Trava append-only), Issue #48 (Aplicações Satélite e Operação), US-07.6  
> Responsável: @squad-30  
> Data: 2026-11-11  

---

## 1. Contexto & Regras de Negócio

Esta especificação cobre a governança completa de **Aplicações Satélite** pelo Administrador da Plataforma e duas travas operacionais críticas de banco e segurança para a API.

### 1.1 Gestão de Aplicações Satélite (CRUD Admin)
- As aplicações satélites (PDVs, totens, e-commerces) consomem os serviços de concessão e resgate de vouchers via credenciais próprias (`POST /api/v1/satellite/token`, US-07.5).
- **Acesso Restrito:** Apenas o `administrador` da plataforma tem permissão para criar, listar, detalhar, alterar, inativar e rotacionar segredos de aplicações. Analistas e gestores recebem `403 FORBIDDEN` (`AUTH_FORBIDDEN_ROLE`). Requisições sem token recebem `401 UNAUTHORIZED`.
- **Vínculo com Rede:** Cada aplicação pertence a uma rede (`NetworkId`), sem foreign key no banco para a tabela `networks` (padrão idêntico ao adotado na tabela `users`). A rede não pode ser vazia (`Guid.Empty`), resultando em `400 VALIDATION_ERROR`.
- **Identificador (`clientId`):**
  - Armazenado sempre em minúsculas e sem espaços laterais (`Trim().ToLowerInvariant()`).
  - Único no sistema (`HasIndex(app => app.ClientId).IsUnique()`).
  - Tentativa de cadastro com `clientId` já existente retorna erro de negócio `422` com `code: "SATELLITE_CLIENT_ID_TAKEN"` e mensagem clara.
- **Escopos:**
  - Permitidos exclusivamente os três escopos oficiais definidos no sistema:
    - Leitura de campanhas: `campaigns:read` (ou alias `campaign:read`)
    - Concessão de vouchers: `vouchers:issue` (ou alias `voucher:issue`, `voucher:read`)
    - Resgate de vouchers: `vouchers:redeem` (ou alias `voucher:redeem`)
  - Qualquer escopo desconhecido resulta em `400 VALIDATION_ERROR` ("Escopo informado inválido.").
- **Geração e Armazenamento de Segredo:**
  - O segredo (`clientSecret`) é gerado criptograficamente pela API (24 bytes aleatórios em hexadecimal, 52 caracteres com o prefixo `sec_`).
  - O segredo **nunca** é gravado em texto claro, apenas seu hash PBKDF2 (`PasswordHasher.Hash(secret)`).
  - O segredo em texto claro é retornado **uma única vez** na resposta de criação (`201 Created`) ou de rotação de segredo (`200 OK`).
  - Respostas de consulta e listagem (`GET`) **nunca** incluem o segredo nem o hash.
- **Alteração:**
  - Permite atualizar `Name` e `Scopes`.
  - `ClientId` e `NetworkId` são imutáveis após a criação.
- **Inativação:**
  - Marca `IsActive = false`.
  - A aplicação inativada deixa de receber novos tokens imediatamente em `POST /satellite/token` (respondendo `401 AUTH_INVALID_CLIENT`).
  - Tokens já emitidos permanecem válidos até o término de sua expiração original (sem lista de revogação em tempo real, conforme D-6 da US-07.5).
  - Disponível via `DELETE /api/v1/satellite/apps/{id}` e `PATCH /api/v1/satellite/apps/{id}/deactivate` (paridade com frontend).
- **Rotação de Segredo:**
  - Gera um novo segredo criptográfico e substitui `SecretHash` imediatamente no banco.
  - O segredo anterior deixa de autenticar no mesmo instante para novas emissões de token.
  - Tokens emitidos anteriormente continuam válidos até expirarem.
  - Retorna o novo segredo uma única vez na resposta.

---

### 1.2 Trava de Tabelas `IAppendOnly` no Banco (Issue #28)
- Atualmente, a proteção de integridade da trilha de auditoria (`audit_entries`) e dos eventos de status de campanha (`campaign_status_events`) é garantida pelo interceptor do `AppDbContext` (`ChangeTracker.Entries<IAppendOnly>()`), que lança `InvalidOperationException` em operações de `Modified` ou `Deleted`.
- Operações de `ExecuteUpdate`, `ExecuteDelete` ou scripts manuais executados diretamente no banco de dados passam por fora do `ChangeTracker`.
- **Solução no Banco de Dados:**
  - **PostgreSQL:** Trigger function `prevent_append_only_modification()` disparando exceção `RAISE EXCEPTION` em eventos `BEFORE UPDATE OR DELETE` nas tabelas `audit_entries` e `campaign_status_events`.
  - **SQLite:** Triggers dedicadas `BEFORE UPDATE` e `BEFORE DELETE` utilizando `SELECT RAISE(ABORT, 'Updates and deletes are not allowed on append-only tables.')`.
  - A tabela `voucher_events` (issue #45, também `IAppendOnly`) ficou de fora desta migração; a trigger dela veio na `VoucherEventsAppendOnlyTrigger` (spec `GH-028-ajustes-pos-verificacao.md`).
  - Criada migração `AppendOnlyTableTriggers` em ambos os assemblies:
    - `VoucherCampaigns.Infrastructure.Migrations.Sqlite`
    - `VoucherCampaigns.Infrastructure.Migrations.Postgres`

---

### 1.3 Limite de Tentativas em `POST /satellite/token` (Rate Limiting por `clientId`)
- Prevenção contra ataques de força bruta no endpoint de emissão de tokens.
- O endpoint continua respondendo `401 AUTH_INVALID_CLIENT` ("Credenciais da aplicação inválidas.") de forma uniforme para segredo errado, cliente inexistente ou aplicação inativa.
- Após exceder o limite de 5 tentativas falhas consecutivas, a rota bloqueia novas tentativas daquele `clientId` por 1 minuto, respondendo `429 Too Many Requests` (`code: "SATELLITE_TOKEN_RATE_LIMIT_EXCEEDED"`).
- Implementado em memória com thread-safety via `ISatelliteTokenRateLimiter` (`SatelliteTokenRateLimiter`).

---

### 1.4 Banco Oficial PostgreSQL
- Quando o banco de dados PostgreSQL oficial for provisionado, o apontamento de configuração será realizado exclusivamente via variável de ambiente:
  `Database__ConnectionString="Host=...;Database=...;Username=...;Password=...;SSL Mode=Require"`
- Nenhum código de aplicação ou ajuste de tela é necessário, pois as migrações do assembly `Postgres` já atendem integralmente ao esquema.

---

## 2. Contratos & Schemas (DTOs / Records)

```csharp
namespace VoucherCampaigns.Application.Identity;

/// <summary>Criação de aplicação satélite.</summary>
public record CreateSatelliteAppRequest(
    [Required(ErrorMessage = "Nome é obrigatório.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Nome deve ter entre 1 e 150 caracteres.")]
    string Name,

    [Required(ErrorMessage = "ClientId é obrigatório.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "ClientId deve ter entre 1 e 100 caracteres.")]
    string ClientId,

    [Required(ErrorMessage = "Rede é obrigatória.")]
    Guid? NetworkId,

    [Required(ErrorMessage = "Escopos são obrigatórios.")]
    IReadOnlyList<string> Scopes);

/// <summary>Atualização de dados cadastrais da aplicação.</summary>
public record UpdateSatelliteAppRequest(
    [Required(ErrorMessage = "Nome é obrigatório.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Nome deve ter entre 1 e 150 caracteres.")]
    string Name,

    [Required(ErrorMessage = "Escopos são obrigatórios.")]
    IReadOnlyList<string> Scopes);

/// <summary>Dados públicos da aplicação (nunca expõe segredo nem hash).</summary>
public record SatelliteAppItemResponse(
    Guid Id,
    string Name,
    string ClientId,
    Guid NetworkId,
    IReadOnlyList<string> Scopes,
    bool IsActive,
    DateTime CreatedAt)
{
    public bool Active => IsActive;
}

/// <summary>Resposta da criação com exibição do segredo gerado uma única vez.</summary>
public record CreateSatelliteAppResponse(
    SatelliteAppItemResponse App,
    string ClientSecret);

/// <summary>Resposta da rotação com exibição do novo segredo uma única vez.</summary>
public record RotateSecretResponse(
    string ClientSecret)
{
    public string NewSecret => ClientSecret;
}
```

---

## 3. Especificação dos Endpoints

Prefixo base: `/api/v1/satellite/apps`  
Autorização: Exclusiva para `administrador` (`[Authorize(Policy = AuthorizationPolicies.ManageSatelliteApps)]`).

### 3.1 `POST /api/v1/satellite/apps`
- **Descrição:** Cria uma aplicação satélite e gera sua credencial de acesso.
- **Respostas:**
  - `201 Created`: Retorna `CreateSatelliteAppResponse` com cabeçalho `Location: /api/v1/satellite/apps/{id}`.
  - `400 Bad Request`: `VALIDATION_ERROR` (campos obrigatórios ausentes, `NetworkId` vazio, escopos inválidos/desconhecidos).
  - `403 Forbidden`: `AUTH_FORBIDDEN_ROLE` (quando chamado por analista ou gestor).
  - `422 Unprocessable Entity`: `SATELLITE_CLIENT_ID_TAKEN` ("Já existe aplicação cadastrada com este identificador.").

### 3.2 `GET /api/v1/satellite/apps?networkId={guid}`
- **Descrição:** Lista as aplicações vinculadas à rede especificada.
- **Respostas:**
  - `200 OK`: Lista `IReadOnlyList<SatelliteAppItemResponse>`.
  - `400 Bad Request`: `VALIDATION_ERROR` (`networkId` ausente ou vazio).
  - `403 Forbidden`: `AUTH_FORBIDDEN_ROLE`.

### 3.3 `GET /api/v1/satellite/apps/{id}`
- **Descrição:** Consulta uma aplicação pelo seu identificador único.
- **Respostas:**
  - `200 OK`: Retorna `SatelliteAppItemResponse`.
  - `403 Forbidden`: `AUTH_FORBIDDEN_ROLE`.
  - `404 Not Found`: `NOT_FOUND` ("Aplicação satélite não encontrada."), como todo recurso inexistente da API (`ResourceNotFoundException`).

### 3.4 `PUT /api/v1/satellite/apps/{id}`
- **Descrição:** Altera nome e escopos da aplicação satélite. `clientId` e `networkId` não são modificados.
- **Respostas:**
  - `200 OK`: Retorna `SatelliteAppItemResponse` atualizado.
  - `400 Bad Request`: `VALIDATION_ERROR` (nome inválido ou escopos desconhecidos).
  - `403 Forbidden`: `AUTH_FORBIDDEN_ROLE`.
  - `404 Not Found`: `NOT_FOUND`.

### 3.5 `DELETE /api/v1/satellite/apps/{id}` & `PATCH /api/v1/satellite/apps/{id}/deactivate`
- **Descrição:** Inativa a aplicação (`IsActive = false`).
- **Respostas:**
  - `200 OK`: Retorna `SatelliteAppItemResponse` com `isActive = false`.
  - `403 Forbidden`: `AUTH_FORBIDDEN_ROLE`.
  - `404 Not Found`: `NOT_FOUND`.

### 3.6 `POST /api/v1/satellite/apps/{id}/rotate-secret`
- **Descrição:** Gera um novo segredo, substitui o hash no banco e devolve o segredo novo uma única vez.
- **Respostas:**
  - `200 OK`: Retorna `RotateSecretResponse(ClientSecret: "sec_...")`.
  - `403 Forbidden`: `AUTH_FORBIDDEN_ROLE`.
  - `404 Not Found`: `NOT_FOUND`.

---

## 4. Checklist de Implementação

- [x] 1. **Domínio & Aplicação:**
  - Criar DTOs/Records em `src/VoucherCampaigns.Application/Identity/`.
  - Normalização e validação de escopos (`SatelliteScopes`) com suporte a aliases (`campaigns:read` / `campaign:read`, `vouchers:issue` / `voucher:issue`, `vouchers:redeem` / `voucher:redeem`).
  - Criar interface de serviço `ISatelliteAppManagementService`.
- [x] 2. **Infraestrutura:**
  - Implementar `SatelliteAppManagementService` com geração criptográfica segura de segredo e hash PBKDF2 (`PasswordHasher`).
  - Implementar `ISatelliteTokenRateLimiter` para controle de tentativas em `POST /satellite/token`.
  - Integrar rate limiter em `SatelliteController.Token`.
- [x] 3. **API & Endpoints:**
  - Criar `SatelliteAppsController` com `[Authorize(Policy = AuthorizationPolicies.ManageSatelliteApps)]`.
  - Endpoints `POST`, `GET (por rede)`, `GET (por ID)`, `PUT`, `DELETE` / `PATCH (deactivate)` e `POST (rotate-secret)`.
  - Configurar mapeamento de erro `SATELLITE_CLIENT_ID_TAKEN` em `ApiProblems`.
- [x] 4. **Triggers de Banco (Issue #28):**
  - Escrever migração `AddAppendOnlyTableTriggers` para SQLite (triggers `BEFORE UPDATE` e `BEFORE DELETE` em `audit_entries` e `campaign_status_events`).
  - Escrever migração `AddAppendOnlyTableTriggers` para PostgreSQL (trigger function e triggers `BEFORE UPDATE OR DELETE`).
  - Testar integridade com `MigrationsModelTests`.
- [x] 5. **Testes:**
  - Testes unitários para `SatelliteAppManagementService` e `SatelliteTokenRateLimiter`.
  - Testes de integração para `SatelliteAppsController` cobrindo sucesso, 400 (rede vazia, escopo inválido), 422 (clientId duplicado), 403 (analista/gestor) e 404 (inexistente).
  - Teste de integração para bloqueio de brute-force em `POST /satellite/token` (resposta 429 após 5 tentativas).
  - Teste de integração para verificação da trava no banco (tentativa de `ExecuteUpdate` ou `ExecuteDelete` em `audit_entries` e `campaign_status_events` falhando via trigger de banco).
- [x] 6. **Validação:**
  - `dotnet test` com 100% de aprovação (685 testes: 262 unitários + 423 de integração).

---

## 5. Casos de Teste Essenciais

- [x] **Criar aplicação (Sucesso):** Cria com `clientId` em minúsculas, rede e escopos válidos, grava hash PBKDF2 e retorna `201 Created` com segredo em texto claro.
- [x] **Criar aplicação (`clientId` Duplicado):** Retorna `422 Unprocessable Entity` com código `SATELLITE_CLIENT_ID_TAKEN`.
- [x] **Criar aplicação (Escopo Inválido / Rede Vazia):** Retorna `400 VALIDATION_ERROR`.
- [x] **Controle de Acesso:** Analista e gestor recebem `403 AUTH_FORBIDDEN_ROLE` em todas as rotas administrativas; requisição anônima recebe `401`.
- [x] **Listagem e Consulta:** Retornam dados cadastrais sem expor segredo nem hash.
- [x] **Alteração:** Permite atualizar nome e escopos; `clientId` e rede não sofrem alteração.
- [x] **Inativação:** Marca `isActive = false`; aplicação inativa deixa de conseguir novo token em `POST /satellite/token`.
- [x] **Rotação de Segredo:** Gera novo segredo; hash anterior não autentica mais; novo segredo autentica normalmente.
- [x] **Proteção de Tabelas Append-Only:** Tentativa de `ExecuteUpdateAsync` ou `ExecuteDeleteAsync` direto no banco em `audit_entries` ou `campaign_status_events` é abortada pela trigger.
- [x] **Rate Limiting em Token:** 5 tentativas inválidas consecutivas para o mesmo `clientId` resultam em `429 Too Many Requests`.
