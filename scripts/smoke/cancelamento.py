"""Roteiro do cancelamento da campanha (spec GH-061) com a API no ar.

Uso: python -I scripts/smoke/cancelamento.py http://localhost:5091

Cria uma campanha de 90% só na loja online (canal virtual), para ganhar das outras pelo maior benefício (D1), e
emite três vouchers: um disponível, um resgatado e um provisório. Confere o 400 sem motivo, o cancelamento, a
recusa do disponível na verificação e no resgate, o 422 da confirmação do provisório, o dashboard, a trilha e o
422 de um segundo cancelamento.
"""

import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from smoke import FAILURES, Client, Demo, check  # noqa: E402

CONCESSIONS = "/api/v1/satellite/concessions"
REDEMPTIONS = "/api/v1/satellite/redemptions"
CHECK = "/api/v1/satellite/vouchers/check"


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


def run(base_url):
    api = Client(base_url)
    analista = api.login("analista@empresa.com")
    gestor = api.login("gestor@empresa.com")
    satellite = api.satellite_token()
    tag = __import__("datetime").datetime.now().strftime("%H%M%S%f")
    available_doc, redeemed_doc, provisional_doc = document(), document(), document()

    campaign_id = api.ready_campaign(analista, name="Cancelamento")
    steps = [
        ("items", {"items": [{"targetType": "All", "benefitType": "Percentage", "benefitValue": 90}]}),
        ("scopes", {"scopes": [{"establishmentId": Demo.STORE_ONLINE, "channels": ["Virtual"]}]}),
    ]
    for route, payload in steps:
        status, body = api.call("PUT", f"/api/v1/campaigns/{campaign_id}/{route}", payload, analista)
        check(f"PUT /campaigns/{{id}}/{route}", status, 200, body)
    for route, token in (("submit", analista), ("approve", gestor)):
        status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/{route}", None, token)
        check(f"POST /campaigns/{{id}}/{route}", status, 200, body)
    if not isinstance(body, dict) or body.get("status") != "Active":
        print(f"      campanha não está ativa ({body.get('status') if isinstance(body, dict) else body}); o roteiro para aqui")
        return 1

    def grant(customer, suffix):
        status, body = api.call("POST", CONCESSIONS, purchase(f"TRX-{tag}-{suffix}", customer), token=satellite)
        ok = check(f"POST /satellite/concessions ({suffix})", status, 200, body) and isinstance(body, dict)
        voucher = (body.get("voucher") or {}) if ok else {}
        if voucher.get("campaignId") != campaign_id:
            print(f"      o voucher deveria ser desta campanha: {body}")
            FAILURES.append(f"concessão {suffix}")
            return None
        return body

    available = grant(available_doc, "disponivel")
    redeemed = grant(redeemed_doc, "resgatado")
    provisional = grant(provisional_doc, "provisorio")
    if not (available and redeemed and provisional):
        return 1
    for concession in (available, redeemed):
        status, body = api.call("POST", f"{CONCESSIONS}/{concession['concessionId']}/confirm", None, satellite)
        check("POST /satellite/concessions/{id}/confirm", status, 200, body)
    status, body = api.call("POST", REDEMPTIONS, purchase(f"TRX-{tag}-r", redeemed_doc, redeemed["voucher"]["id"]), token=satellite)
    check("POST /satellite/redemptions", status, 200, body)

    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/cancel", {"reason": "  "}, analista)
    check("POST /campaigns/{id}/cancel (sem motivo)", status, 400, body, "VALIDATION_ERROR")

    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/cancel", {"reason": "Erro de configuração no desconto"}, analista)
    if check("POST /campaigns/{id}/cancel", status, 200, body) and body.get("status") != "Cancelled":
        print(f"      status deveria ser Cancelled: {body.get('status')}")
        FAILURES.append("status cancelada")

    status, body = api.call("POST", CHECK, purchase(f"TRX-{tag}-c", available_doc), token=satellite)
    if check("POST /satellite/vouchers/check (depois do cancelamento)", status, 200, body):
        reasons = [item.get("reason") for item in body.get("notApplicable") or [] if item["voucher"]["id"] == available["voucher"]["id"]]
        print(f"      motivo do voucher disponível: {reasons}")
        if reasons != ["Cancelled"]:
            FAILURES.append("verificação do cancelado")

    status, body = api.call("POST", REDEMPTIONS, purchase(f"TRX-{tag}-x", available_doc, available["voucher"]["id"]), token=satellite)
    check("POST /satellite/redemptions (voucher cancelado)", status, 422, body, "REDEMPTION_CANCELLED")

    status, body = api.call("POST", f"{CONCESSIONS}/{provisional['concessionId']}/confirm", None, satellite)
    check("POST /satellite/concessions/{id}/confirm (provisório descartado)", status, 422, body, "CONCESSION_CAMPAIGN_CANCELLED")

    status, body = api.call("GET", f"/api/v1/campaigns/{campaign_id}/dashboard", token=analista)
    if check("GET /campaigns/{id}/dashboard", status, 200, body):
        indicators = body.get("indicators") or {}
        print(f"      issued={indicators.get('issued')}, redeemed={indicators.get('redeemed')}, "
              f"cancelledUnused={indicators.get('cancelledUnused')}, reserved={indicators.get('reserved')}")
        if (indicators.get("issued"), indicators.get("redeemed"), indicators.get("cancelledUnused"), indicators.get("reserved")) != (2, 1, 1, 0):
            FAILURES.append("dashboard do cancelamento")

    status, body = api.call("GET", f"/api/v1/campaigns/{campaign_id}/audit", token=gestor)
    if check("GET /campaigns/{id}/audit", status, 200, body):
        cancelled = [entry for entry in body.get("entries", []) if entry.get("eventType") == "CampaignCancelled"]
        print(f"      CampaignCancelled na trilha: {len(cancelled)}")
        if len(cancelled) != 1:
            FAILURES.append("trilha do cancelamento")

    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/cancel", {"reason": "De novo"}, analista)
    check("POST /campaigns/{id}/cancel (já cancelada)", status, 422, body, "CAMPAIGN_CANCEL_NOT_ALLOWED")

    print()
    if FAILURES:
        print(f"{len(FAILURES)} ponto(s) com falha: " + ", ".join(FAILURES))
        return 1
    print("Roteiro do cancelamento: todas as rotas OK.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(run(sys.argv[1]))
