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
| D-2 | ~~Contagem: Redeemed = status efetivo `Redeemed`; Expired = efetivo `Expired`.~~ **Revisada no PR #65 (2026-11-18):** a mesma regra do dashboard (spec `GH-050-acompanhamento-da-campanha.md`, D-3): Redeemed = emitido com ao menos uma utilização (o usado em parte conta); Expired = vencido sem nenhuma utilização. Assim o detalhe e o dashboard mostram o mesmo número. |
| D-3 | Sem migração: agregação no SQL por `IDashboardRepository.CountVouchersAsync` (uma consulta para a lista, uma por campanha nas demais respostas), sem carregar os vouchers em memória. Toda resposta de campanha já gravada (inclusive pausar, encerrar e cancelar) traz os contadores; a criação do rascunho devolve 0. |

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

- [x] 1. Estender `CampaignResponse` e `Map`
- [x] 2. Contagem por campanha(s) no repositório/serviço
- [x] 3. Testes de integração com vouchers emitidos/resgatados/expirados
- [x] 4. `dotnet test`

---

## 5. Casos de Teste

- [ ] Campanha sem vouchers: redeemed/expired = 0
- [ ] Após confirmação e resgate: issued e redeemed coerentes
