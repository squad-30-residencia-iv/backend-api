"""Roteiro das restrições no resgate (spec GH-070) com a API no ar.

Uso: python -I scripts/smoke/restricoes_resgate.py http://localhost:5091

Confere o contrato das rotas novas (`redemption-targets` e `redemption-windows`) e, na loja online, duas campanhas:
- 97%, com janela de resgate num dia que não é hoje: o voucher aparece como fora da janela;
- 98%, com resgate só em calçados e janela o dia todo: a compra só com moda é recusada, e a compra com moda e
  calçados resgata com o desconto calculado só sobre os calçados.

As campanhas de 97% e 98% ficam no banco e ganham das outras na loja online (D1). Rode este roteiro por último ou
numa API nova.
"""

import random
import sys
import uuid
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

sys.path.insert(0, str(Path(__file__).resolve().parent))
from smoke import FAILURES, Client, Demo, check  # noqa: E402

CHECK = "/api/v1/satellite/vouchers/check"
CONCESSIONS = "/api/v1/satellite/concessions"
REDEMPTIONS = "/api/v1/satellite/redemptions"
DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]
FASHION = {"productCode": "MOD-001", "quantity": 1, "unitPrice": 200.0}
SHOES = {"productCode": "CAL-001", "quantity": 1, "unitPrice": 300.0}


def document():
    return "".join(str(random.randint(0, 9)) for _ in range(11))


def purchase(customer, items, voucher_id=None):
    body = {
        "transactionId": f"TRX-{uuid.uuid4().hex[:10]}",
        "customer": {"document": customer},
        "establishmentId": Demo.STORE_ONLINE,
        "channel": "Virtual",
        "totalAmount": sum(item["quantity"] * item["unitPrice"] for item in items),
        "items": items,
    }
    if voucher_id:
        body["voucherId"] = voucher_id
    return body


def run(base_url):
    api = Client(base_url)
    analista = api.login("analista@empresa.com")
    gestor = api.login("gestor@empresa.com")
    satellite = api.satellite_token()
    today = DAYS[datetime.now(ZoneInfo(Demo.TIME_ZONE)).weekday()]
    other_day = DAYS[(DAYS.index(today) + 3) % 7]

    def approved(value, configure):
        campaign = api.ready_campaign(analista, name=f"Resgate {value}%")
        for route, payload in [
            ("items", {"items": [{"targetType": "All", "benefitType": "Percentage", "benefitValue": value}]}),
            ("scopes", {"scopes": [{"establishmentId": Demo.STORE_ONLINE, "channels": ["Virtual"]}]}),
            *configure,
        ]:
            status, body = api.call("PUT", f"/api/v1/campaigns/{campaign}/{route}", payload, analista)
            check(f"PUT /campaigns/{{id}}/{route} ({value}%)", status, 200, body)
        for route, token in (("submit", analista), ("approve", gestor)):
            status, body = api.call("POST", f"/api/v1/campaigns/{campaign}/{route}", None, token)
            check(f"POST /campaigns/{{id}}/{route} ({value}%)", status, 200, body)
        return campaign

    def voucher_for(customer):
        status, body = api.call("POST", CONCESSIONS, purchase(customer, [FASHION]), token=satellite)
        if not check("POST /satellite/concessions", status, 200, body) or not body.get("voucher"):
            return None
        status, confirmed = api.call("POST", f"{CONCESSIONS}/{body['concessionId']}/confirm", None, satellite)
        check("POST /satellite/concessions/{id}/confirm", status, 200, confirmed)
        return body["voucher"]

    def reason(customer, items):
        status, body = api.call("POST", CHECK, purchase(customer, items), token=satellite)
        if not check("POST /satellite/vouchers/check", status, 200, body):
            return None
        refused = body.get("notApplicable") or []
        return refused[0]["reason"] if refused else None

    # Contrato das rotas novas num rascunho.
    draft = api.ready_campaign(analista, name="Contrato resgate")
    for name, route, payload, expected, code in [
        ("alvo All", "redemption-targets", {"targets": [{"targetType": "All"}]}, 400, "VALIDATION_ERROR"),
        ("código inexistente", "redemption-targets", {"targets": [{"targetType": "Product", "productCode": "NAO-EXISTE-GH70"}]}, 422, "CAMPAIGN_ITEM_PRODUCT_CODE_NOT_FOUND"),
        ("alvo repetido", "redemption-targets", {"targets": [{"targetType": "Product", "productCode": "CAL-001"}, {"targetType": "Product", "productCode": "cal-001"}]}, 422, "CAMPAIGN_REDEMPTION_TARGET_DUPLICATED"),
        ("hora inválida", "redemption-windows", {"windows": [{"daysOfWeek": ["Monday"], "start": "24:00", "end": "09:00"}]}, 400, "VALIDATION_ERROR"),
        ("dia inválido", "redemption-windows", {"windows": [{"daysOfWeek": ["Segunda"], "start": "08:00", "end": "09:00"}]}, 400, "VALIDATION_ERROR"),
        ("janela que vira a noite", "redemption-windows", {"windows": [{"daysOfWeek": ["Friday"], "start": "22:00", "end": "01:59"}]}, 200, None),
    ]:
        status, body = api.call("PUT", f"/api/v1/campaigns/{draft}/{route}", payload, analista)
        check(f"PUT /{route} ({name})", status, expected, body, code)

    # 97%: só resgata noutro dia da semana.
    approved(97, [("redemption-windows", {"windows": [{"daysOfWeek": [other_day], "start": "00:00", "end": "23:59"}]})])
    customer = document()
    if voucher_for(customer):
        got = reason(customer, [FASHION])
        print(f"      fora da janela ({other_day}): {got}")
        if got != "OutsideRedemptionWindow":
            FAILURES.append("janela de resgate")

    # 98%: resgate só em calçados, o dia todo.
    best = approved(98, [
        ("redemption-targets", {"targets": [{"targetType": "Category", "categoryId": Demo.CATEGORY_SHOES}]}),
        ("redemption-windows", {"windows": [{"daysOfWeek": DAYS, "start": "00:00", "end": "23:59"}]}),
    ])
    status, body = api.call("GET", f"/api/v1/campaigns/{best}", token=analista)
    if check("GET /campaigns/{id}", status, 200, body):
        print(f"      redemptionTargets: {len(body.get('redemptionTargets') or [])}, redemptionWindows: {len(body.get('redemptionWindows') or [])}")
    customer = document()
    voucher = voucher_for(customer)
    if voucher:
        got = reason(customer, [FASHION])
        print(f"      só moda: {got}")
        if got != "TargetNotInPurchase":
            FAILURES.append("alvo de resgate")
        status, body = api.call("POST", REDEMPTIONS, purchase(customer, [FASHION, SHOES], voucher["id"]), token=satellite)
        if check("POST /satellite/redemptions (moda + calçados)", status, 200, body):
            print(f"      desconto: {body.get('discountAmount')} (98% de 300,00 dos calçados)")
            if body.get("discountAmount") != 294.0:
                FAILURES.append("base do desconto")

    print()
    if FAILURES:
        print(f"{len(FAILURES)} ponto(s) com falha: " + ", ".join(FAILURES))
        return 1
    print("Roteiro das restrições no resgate: todas as rotas OK.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(run(sys.argv[1]))
