# Integração do satélite: público elegível

Contrato para o PDV e o e-commerce (spec `GH-066`, D-10; ponto 5 do cliente). O público elegível é avaliado no momento da emissão, com os dados que o satélite manda na própria concessão. A plataforma não guarda cadastro nem histórico de compras dos clientes (decisões D2 e D4).

## 1. Descobrir o que cada campanha pede

`GET /api/v1/satellite/campaigns` (escopo `campaigns:read`) traz, para cada campanha vigente:

- `requiredAttributes`: os dados do cliente que a campanha usa. Os valores possíveis estão na tabela abaixo. A lista vem vazia quando o público é "todos os clientes".
- `historyWindows`: as janelas, em dias, sobre as quais o satélite deve somar o histórico de compras do cliente. Exemplo: `[30, 90]`.

| `requiredAttributes` | Critério da campanha | Campo em `customer` |
|---|---|---|
| `birthDate` | Faixa etária | `birthDate` (`AAAA-MM-DD`) |
| `state` | UF | `state` (sigla, ex.: `SP`) |
| `cityCode` | Município | `cityCode` (código IBGE de 7 dígitos) |
| `loyaltyMember` | Clube de vantagens | `loyaltyMember` (`true`/`false`) |
| `history.totalSpent` | Valor gasto na janela | `history[].totalSpent` |
| `history.purchaseCount` | Quantidade de compras na janela | `history[].purchaseCount` |
| `history.productCodes` | Categoria comprada na janela | `history[].productCodes` (códigos do catálogo) |

A lista de CPFs específicos não pede atributo: basta o `customer.document` que toda concessão já manda.

## 2. Mandar os dados na concessão

```json
POST /api/v1/satellite/concessions
{
  "transactionId": "PDV-0042-20261118-0001",
  "establishmentId": "0199e3a0-0000-7000-8000-000000000201",
  "channel": "Physical",
  "totalAmount": 250.00,
  "items": [{ "productCode": "MOD-001", "quantity": 1, "unitPrice": 250.00 }],
  "customer": {
    "document": "123.456.789-09",
    "birthDate": "1990-05-20",
    "state": "SP",
    "cityCode": "3550308",
    "loyaltyMember": true,
    "history": [
      { "windowDays": 30, "totalSpent": 820.50, "purchaseCount": 4, "productCodes": ["MOD-001", "CAL-010"] },
      { "windowDays": 90, "totalSpent": 1910.00, "purchaseCount": 9, "productCodes": ["MOD-001"] }
    ]
  }
}
```

- Mande os atributos de `requiredAttributes` de todas as campanhas da loja. Mandar a mais não atrapalha.
- Um atributo que a campanha pede e não veio conta como não atendido.
- O histórico de cada janela é a soma das compras do cliente nos últimos `windowDays` dias, sem contar a compra atual.

## 3. Ler a resposta

- Se o cliente não se encaixa no público e nenhuma outra campanha serve, a concessão volta `NotGranted` com `reason: "CustomerNotEligible"`. Esse caso não reserva saldo.
- Se outra campanha apta servir, ela concede normalmente.
- A concessão guarda os atributos usados e o resultado de cada critério, para auditoria (RN11a).
- O resgate não reavalia o público.

## 4. Consultar os vouchers do cliente

Use `POST /api/v1/satellite/customers/vouchers/search` com `{ "document": "...", "status": "Available" }` no corpo. A rota antiga `GET /satellite/customers/{cpf}/vouchers` continua funcionando, mas está obsoleta (cabeçalho `Deprecation`), porque deixa o CPF em logs de acesso.

## Pontos a confirmar com o PDV

- **Histórico somado:** se o PDV consegue mandar o histórico somado por janela (PA-7 da spec GH-047).
- **Município:** se o PDV tem o código IBGE do município (PA-8 da spec GH-047).
- **Rota de consulta:** prazo para migrar para a consulta por `POST` (PA-9 da spec GH-066).
