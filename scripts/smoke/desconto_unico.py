"""Roteiro do desconto único e do alvo por código de produto (spec GH-068) com a API no ar.

Uso: python -I scripts/smoke/desconto_unico.py http://localhost:5091

Importa 1.000 produtos e os usa como alvos de uma campanha pelo código. Confere:
- o 422 de descontos diferentes;
- o 422 com `codes` de um código que não existe;
- o 400 de id e código juntos;
- o `productCode` na resposta e o resumo da trilha.

Depois aprova duas campanhas sobre o MOD-001, de 91% e 92%, na loja online. Uma compra recebe um voucher só, o da
campanha de maior desconto, e o resgate desse voucher fecha a compra para um segundo voucher.

As campanhas de 91% e 92% ficam no banco e ganham das outras na loja online (D1). Rode este roteiro por último ou
numa API nova.
"""

import random
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from smoke import FAILURES, Client, Demo, check  # noqa: E402

CONCESSIONS = "/api/v1/satellite/concessions"
REDEMPTIONS = "/api/v1/satellite/redemptions"


def document():
    return "".join(str(random.randint(0, 9)) for _ in range(11))


def purchase(transaction_id, customer, voucher_id=None):
    body = {
        "transactionId": transaction_id,
        "customer": {"document": customer},
        "establishmentId": Demo.STORE_ONLINE,
        "channel": "Virtual",
        "totalAmount": 300.0,
        "items": [{"productCode": "MOD-001", "quantity": 1, "unitPrice": 300.0}],
    }
    if voucher_id:
        body["voucherId"] = voucher_id
    return body


def target(code, value=10, kind="Percentage"):
    return {"targetType": "Product", "productCode": code, "benefitType": kind, "benefitValue": value}


def run(base_url):
    api = Client(base_url)
    admin = api.login("admin@empresa.com")
    analista = api.login("analista@empresa.com")
    gestor = api.login("gestor@empresa.com")
    satellite = api.satellite_token()
    prefix = "S" + uuid.uuid4().hex[:6].upper()
    codes = [f"{prefix}{index:04d}" for index in range(1, 1001)]

    status, body = api.call("POST", "/api/v1/products/import", {
        "categoryId": Demo.CATEGORY_FASHION,
        "products": [{"code": code, "name": f"Produto {code}"} for code in codes],
    }, admin)
    check("POST /products/import (1.000)", status, 201, body)

    campaign_id = api.ready_campaign(analista, name="Desconto único")
    items_route = f"/api/v1/campaigns/{campaign_id}/items"

    status, body = api.call("PUT", items_route, {"items": [target(codes[0], 10), target(codes[1], 15)]}, analista)
    check("PUT /items (descontos diferentes)", status, 422, body, "CAMPAIGN_BENEFIT_NOT_UNIFORM")

    missing = prefix + "XXXX"
    status, body = api.call("PUT", items_route, {"items": [target(codes[0]), target(missing)]}, analista)
    if check("PUT /items (código inexistente)", status, 422, body, "CAMPAIGN_ITEM_PRODUCT_CODE_NOT_FOUND") and body.get("codes") != [missing]:
        print(f"      codes deveria ser [{missing}]: {body.get('codes')}")
        FAILURES.append("codes do inexistente")

    both = {"targetType": "Product", "productId": Demo.CATEGORY_FASHION, "productCode": codes[0], "benefitType": "Percentage", "benefitValue": 10}
    status, body = api.call("PUT", items_route, {"items": [both]}, analista)
    check("PUT /items (id e código juntos)", status, 400, body, "VALIDATION_ERROR")

    status, body = api.call("PUT", items_route, {"items": [target(code.lower()) for code in codes]}, analista)
    if check("PUT /items (1.000 códigos, minúsculos)", status, 200, body):
        returned = [item.get("productCode") for item in body.get("items", [])]
        print(f"      alvos: {len(returned)}, primeiro: {returned[:1]}, último: {returned[-1:]}")
        if returned != codes:
            FAILURES.append("productCode na resposta")

    status, body = api.call("GET", f"/api/v1/campaigns/{campaign_id}/audit", token=gestor)
    if check("GET /campaigns/{id}/audit", status, 200, body):
        entries = [entry for entry in body.get("entries", []) if entry.get("eventType") == "CampaignUpdated"]
        payloads = [entry.get("payload") or {} for entry in entries]
        summary = next((p.get("after") for p in payloads if p.get("section") == "items" and "count" in (p.get("after") or {})), None)
        print(f"      resumo dos alvos na trilha: {summary}")
        if not summary or summary.get("count") != 1000:
            FAILURES.append("resumo da trilha")

    def approved(value):
        campaign = api.ready_campaign(analista, name=f"MOD-001 {value}%")
        for route, payload in (
            ("items", {"items": [target("MOD-001", value)]}),
            ("scopes", {"scopes": [{"establishmentId": Demo.STORE_ONLINE, "channels": ["Virtual"]}]}),
        ):
            status, body = api.call("PUT", f"/api/v1/campaigns/{campaign}/{route}", payload, analista)
            check(f"PUT /campaigns/{{id}}/{route} ({value}%)", status, 200, body)
        for route, token in (("submit", analista), ("approve", gestor)):
            status, body = api.call("POST", f"/api/v1/campaigns/{campaign}/{route}", None, token)
            check(f"POST /campaigns/{{id}}/{route} ({value}%)", status, 200, body)
        return campaign

    approved(91)
    best = approved(92)
    customer, tag = document(), uuid.uuid4().hex[:8]

    status, body = api.call("POST", CONCESSIONS, purchase(f"TRX-{tag}-1", customer), token=satellite)
    if not check("POST /satellite/concessions", status, 200, body):
        return 1
    voucher = body.get("voucher") or {}
    print(f"      voucher da campanha de 92%: {voucher.get('campaignId') == best}")
    if voucher.get("campaignId") != best:
        FAILURES.append("campanha do voucher")
    status, body = api.call("POST", f"{CONCESSIONS}/{body['concessionId']}/confirm", None, satellite)
    check("POST /satellite/concessions/{id}/confirm", status, 200, body)

    status, body = api.call("POST", CONCESSIONS, purchase(f"TRX-{tag}-2", customer), token=satellite)
    check("POST /satellite/concessions (segunda compra)", status, 200, body)
    second = (body.get("voucher") or {}) if isinstance(body, dict) else {}
    if isinstance(body, dict) and body.get("concessionId"):
        status, body = api.call("POST", f"{CONCESSIONS}/{body['concessionId']}/confirm", None, satellite)
        check("POST /satellite/concessions/{id}/confirm (segunda)", status, 200, body)

    status, body = api.call("POST", REDEMPTIONS, purchase(f"TRX-{tag}-r", customer, voucher.get("id")), token=satellite)
    check("POST /satellite/redemptions", status, 200, body)
    status, body = api.call("POST", REDEMPTIONS, purchase(f"TRX-{tag}-r", customer, second.get("id")), token=satellite)
    check("POST /satellite/redemptions (segundo voucher na mesma compra)", status, 422, body, "REDEMPTION_TRANSACTION_ALREADY_REDEEMED")

    print()
    if FAILURES:
        print(f"{len(FAILURES)} ponto(s) com falha: " + ", ".join(FAILURES))
        return 1
    print("Roteiro do desconto único: todas as rotas OK.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(run(sys.argv[1]))
