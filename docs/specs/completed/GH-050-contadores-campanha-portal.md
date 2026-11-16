# Especificação Técnica: GH-050 — Contadores e lista no detalhe da campanha (Fatia 2)

> Status: Completed  
> História de Usuário Relacionada: issue #50 / EP-03 (PA-4 GH-049)  
> Responsável: Danilo Araujo  
> Data: 2026-11-16  
> Branch: `feat/EP-03-acompanhamento-portal`

---

## 1. Contexto & Regras de Negócio

- O detalhe do portal mostra confirmados/reservas, mas utilizados e lista de vouchers ficam zerados.
- Contadores `redeemedVouchers` e `expiredVouchers` saem de consulta em `vouchers` com status efetivo (GH-049 D-18), sem novas colunas em `campaigns`.
- Lista recente: reutilizar `GET /api/v1/vouchers?networkId=&campaignId=` (Fatia 1); sem rota filha obrigatória.

### Decisões

| # | Decisão |
|---|---|
| D-1 | Acrescentar `RedeemedVouchers` e `ExpiredVouchers` ao `CampaignResponse` (sempre presentes; 0 se nenhum). |
| D-2 | Contagem: Redeemed = status efetivo `Redeemed`; Expired = efetivo `Expired` (inclui Available vencido pela validade). |
| D-3 | Sem migração: agregação em leitura no serviço/repositório. |

---

## 2. Contratos

```csharp
// Campos novos ao final de CampaignResponse:
int RedeemedVouchers,
int ExpiredVouchers
```

---

## 3. Endpoint

Sem rota nova. `GET /api/v1/campaigns` e `GET /api/v1/campaigns/{id}` passam a devolver os campos.

---

## 4. Checklist

- [ ] 1. Estender `CampaignResponse` e `Map`
- [ ] 2. Contagem por campanha(s) no repositório/serviço
- [ ] 3. Testes de integração com vouchers emitidos/resgatados/expirados
- [ ] 4. `dotnet test`

---

## 5. Casos de Teste

- [ ] Campanha sem vouchers: redeemed/expired = 0
- [ ] Após confirmação e resgate: issued e redeemed coerentes
