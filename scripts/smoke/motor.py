"""Roteiro de rotas do motor de concessão (spec GH-045) com a API no ar.

Uso: python -I scripts/smoke/motor.py http://localhost:5091

Cria e aprova uma campanha de rede inteira (o relógio da API é o real, então o início é o minuto atual),
pede a concessão como o pdv-demo, confirma, desfaz outra, repete a transação e lista os vouchers do cliente.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from smoke import FAILURES, Client, Demo, check  # noqa: E402

ROUTE = "/api/v1/satellite/concessions"


def purchase(transaction_id, document="123.456.789-01", store=Demo.STORE_CENTER, channel="Physical", total=200.0, items=None, purchases=None):
    body = {
        "transactionId": transaction_id,
        "customer": {"document": document},
        "establishmentId": store,
        "channel": channel,
        "totalAmount": total,
        "items": items or [{"productCode": "MOD-001", "quantity": 1, "unitPrice": total}],
    }
    if purchases is not None:
        body["customerPurchaseCount"] = purchases
    return body


def run(base_url):
    api = Client(base_url)
    analista = api.login("analista@empresa.com")
    gestor = api.login("gestor@empresa.com")
    satellite = api.satellite_token()
    tag = __import__("datetime").datetime.now().strftime("%H%M%S%f")

    campaign_id = api.ready_campaign(analista, name="Motor")
    status, body = api.call("PUT", f"/api/v1/campaigns/{campaign_id}/conditions", {"conditions": [{"type": "PurchaseTotal", "minAmount": 150}], "allowUseInSamePurchase": False}, analista)
    check("PUT /campaigns/{id}/conditions (PurchaseTotal 150)", status, 200, body)
    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/submit", None, analista)
    check("POST /campaigns/{id}/submit", status, 200, body)
    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/approve", None, gestor)
    if not check("POST /campaigns/{id}/approve", status, 200, body) or body.get("status") != "Active":
        print(f"      campanha não está ativa ({body.get('status') if isinstance(body, dict) else body}); o motor não concede")
        return 1

    # Concessão provisória
    status, body = api.call("POST", ROUTE, purchase(f"TRX-{tag}-1"), token=satellite)
    granted = check("POST /satellite/concessions (concede)", status, 200, body) and body.get("status") == "Provisional" and body.get("voucher")
    if not granted:
        print(f"FAIL  concessão esperada Provisional com voucher, obtido {body}")
        FAILURES.append("concessão")
        return 1
    concession_id = body["concessionId"]
    code = body["voucher"]["code"]
    # Outra campanha ativa de rede inteira (ex.: a do roteiro base) pode vencer pelo desempate D1; os contadores são da escolhida.
    winner_id = body["voucher"]["campaignId"]
    print(f"      voucher {code}, campanha {'a deste roteiro' if winner_id == campaign_id else winner_id}, reserva até {body['reservedUntil']}, validade {body['voucher']['validUntil']}")
    status, before = api.call("GET", f"/api/v1/campaigns/{winner_id}", token=analista)
    check("GET /campaigns/{id} (reserva feita)", status, 200, before)

    # Idempotência
    status, body = api.call("POST", ROUTE, purchase(f"TRX-{tag}-1"), token=satellite)
    check("POST /satellite/concessions (mesma transação)", status, 200, body)
    if body.get("concessionId") != concession_id or body.get("voucher", {}).get("code") != code:
        print(f"FAIL  a repetição devolveu outra concessão: {body}")
        FAILURES.append("idempotência")

    # Não concedida
    status, body = api.call("POST", ROUTE, purchase(f"TRX-{tag}-2", total=149.99), token=satellite)
    check("POST /satellite/concessions (valor abaixo do mínimo)", status, 200, body)
    if body.get("status") != "NotGranted" or body.get("reason") != "NoCampaignMatched":
        print(f"FAIL  esperado NotGranted/NoCampaignMatched, obtido {body}")
        FAILURES.append("não concedida")

    # Confirmação (idempotente)
    status, body = api.call("POST", f"{ROUTE}/{concession_id}/confirm", None, token=satellite)
    check("POST /satellite/concessions/{id}/confirm", status, 200, body)
    status, body = api.call("POST", f"{ROUTE}/{concession_id}/confirm", None, token=satellite)
    check("POST /satellite/concessions/{id}/confirm (de novo)", status, 200, body)
    status, body = api.call("POST", f"{ROUTE}/{concession_id}/cancel", None, token=satellite)
    check("POST /satellite/concessions/{id}/cancel (já confirmada)", status, 422, body, "CONCESSION_ALREADY_CONFIRMED")

    # Desfazimento
    status, body = api.call("POST", ROUTE, purchase(f"TRX-{tag}-3", document="98765432100"), token=satellite)
    check("POST /satellite/concessions (segunda compra)", status, 200, body)
    second = body.get("concessionId")
    status, body = api.call("POST", f"{ROUTE}/{second}/cancel", None, token=satellite)
    check("POST /satellite/concessions/{id}/cancel", status, 200, body)
    status, body = api.call("POST", f"{ROUTE}/{second}/confirm", None, token=satellite)
    check("POST /satellite/concessions/{id}/confirm (desfeita)", status, 422, body, "CONCESSION_ALREADY_CANCELLED")

    # Consulta e vouchers do cliente
    status, body = api.call("GET", f"{ROUTE}/{concession_id}", token=satellite)
    check("GET /satellite/concessions/{id}", status, 200, body)
    status, body = api.call("GET", "/api/v1/satellite/customers/123.456.789-01/vouchers", token=satellite)
    check("GET /satellite/customers/{cpf}/vouchers", status, 200, body)
    if isinstance(body, list) and not any(v.get("code") == code and v.get("status") == "Available" for v in body):
        print(f"FAIL  o voucher confirmado não apareceu como Available: {body}")
        FAILURES.append("vouchers do cliente")
    status, body = api.call("GET", "/api/v1/satellite/customers/123/vouchers", token=satellite)
    check("GET /satellite/customers/{cpf}/vouchers (CPF inválido)", status, 400, body, "VALIDATION_ERROR")

    # Loja, canal e contadores
    status, body = api.call("POST", ROUTE, purchase(f"TRX-{tag}-4", channel="Virtual"), token=satellite)
    check("POST /satellite/concessions (canal que a loja não opera)", status, 422, body, "ISSUANCE_CHANNEL_NOT_ENABLED")
    status, body = api.call("POST", ROUTE, purchase(f"TRX-{tag}-5", store="0199e3a0-0000-7000-8000-0000000002ff"), token=satellite)
    check("POST /satellite/concessions (loja inexistente)", status, 422, body, "ISSUANCE_ESTABLISHMENT_NOT_FOUND")
    status, body = api.call("POST", ROUTE, purchase(f"TRX-{tag}-6", total=0), token=satellite)
    check("POST /satellite/concessions (total zero)", status, 400, body, "VALIDATION_ERROR")
    status, body = api.call("POST", ROUTE, purchase(f"TRX-{tag}-7"), token=analista)
    check("POST /satellite/concessions (token de usuário)", status, 401, body)
    status, body = api.call("GET", f"/api/v1/campaigns/{winner_id}", token=analista)
    check("GET /campaigns/{id} (contadores da campanha escolhida)", status, 200, body)
    if isinstance(body, dict) and isinstance(before, dict):
        print(f"      issuedVouchers {before.get('issuedVouchers')} -> {body.get('issuedVouchers')}, reservedVouchers {before.get('reservedVouchers')} -> {body.get('reservedVouchers')}, status={body.get('status')}")
        if body.get("issuedVouchers") != before.get("issuedVouchers") + 1 or body.get("reservedVouchers") != before.get("reservedVouchers") - 1:
            print("FAIL  a confirmação deveria mover 1 de reservedVouchers para issuedVouchers (a compra desfeita devolve a sua)")
            FAILURES.append("contadores")

    print()
    if FAILURES:
        print(f"{len(FAILURES)} ponto(s) com falha: " + ", ".join(FAILURES))
        return 1
    print("Roteiro do motor: todas as rotas OK.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(run(sys.argv[1]))
