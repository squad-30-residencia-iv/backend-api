"""Roteiro do público restrito (spec GH-047) com a API no ar.

Uso: python -I scripts/smoke/publico.py http://localhost:5091

Cria uma campanha só na loja online (canal virtual), para não disputar com a do roteiro do motor, com público
restrito ao clube de vantagens, maiores de 18 anos e uma lista de CPFs. Confere a configuração, a lista paginada,
a simulação, as campanhas vigentes do satélite, a concessão ao elegível, a recusa ao inelegível sem gastar saldo,
o resgate sem reavaliar o público e o 400 de critérios inválidos.
"""

import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from smoke import FAILURES, Client, Demo, check  # noqa: E402

CONCESSIONS = "/api/v1/satellite/concessions"
REDEMPTIONS = "/api/v1/satellite/redemptions"


def cpf():
    """CPF aleatório com os dígitos verificadores certos."""
    digits = [random.randint(0, 9) for _ in range(9)]
    for _ in range(2):
        total = sum(digit * (len(digits) + 1 - index) for index, digit in enumerate(digits))
        rest = total % 11
        digits.append(0 if rest < 2 else 11 - rest)
    value = "".join(map(str, digits))
    return cpf() if len(set(value)) == 1 else value


def purchase(transaction_id, customer, voucher_id=None):
    body = {
        "transactionId": transaction_id,
        "customer": customer,
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
    member, outsider, listed_other = cpf(), cpf(), cpf()

    campaign_id = api.ready_campaign(analista, name="Público restrito")
    steps = [
        ("items", {"items": [{"targetType": "All", "benefitType": "Percentage", "benefitValue": 50}]}),
        ("scopes", {"scopes": [{"establishmentId": Demo.STORE_ONLINE, "channels": ["Virtual"]}]}),
    ]
    for route, payload in steps:
        status, body = api.call("PUT", f"/api/v1/campaigns/{campaign_id}/{route}", payload, analista)
        check(f"PUT /campaigns/{{id}}/{route}", status, 200, body)

    invalid = {"audienceType": "Restricted", "criteria": [{"type": "AgeRange", "minAge": 40, "maxAge": 30}]}
    status, body = api.call("PUT", f"/api/v1/campaigns/{campaign_id}/audience", invalid, analista)
    check("PUT /campaigns/{id}/audience (idade mínima maior que a máxima)", status, 400, body, "VALIDATION_ERROR")

    audience = {"audienceType": "Restricted", "criteria": [
        {"type": "LoyaltyMember"},
        {"type": "AgeRange", "minAge": 18},
        {"type": "SpecificCustomers", "documents": [member, outsider, listed_other]},
    ]}
    status, body = api.call("PUT", f"/api/v1/campaigns/{campaign_id}/audience", audience, analista)
    if check("PUT /campaigns/{id}/audience (restrito)", status, 200, body):
        criteria = (body.get("eligibility") or {}).get("criteria") or []
        print(f"      critérios: {[item.get('type') for item in criteria]}, CPFs na lista: {criteria[-1].get('customerCount') if criteria else None}")

    status, body = api.call("GET", f"/api/v1/campaigns/{campaign_id}/audience/customers?pageSize=2", token=analista)
    if check("GET /campaigns/{id}/audience/customers", status, 200, body):
        print(f"      total={body.get('total')}, página com {len(body.get('documents') or [])}")

    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/eligibility/simulate",
                            {"customer": {"document": member, "loyaltyMember": True, "birthDate": "2000-01-01"}}, analista)
    check("POST /campaigns/{id}/eligibility/simulate (elegível)", status, 200, body)
    if isinstance(body, dict) and body.get("eligible") is not True:
        FAILURES.append("simulação elegível")
    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/eligibility/simulate",
                            {"customer": {"document": outsider}}, analista)
    check("POST /campaigns/{id}/eligibility/simulate (atributos ausentes)", status, 200, body)
    if isinstance(body, dict):
        print(f"      eligible={body.get('eligible')}, resultados={[item.get('outcome') for item in body.get('criteria') or []]}")

    for route, token in (("submit", analista), ("approve", gestor)):
        status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/{route}", None, token)
        check(f"POST /campaigns/{{id}}/{route}", status, 200, body)
    if not isinstance(body, dict) or body.get("status") != "Active":
        print(f"      campanha não está ativa ({body.get('status') if isinstance(body, dict) else body}); a concessão não acontece")
        return 1

    status, body = api.call("GET", f"/api/v1/satellite/campaigns?establishmentId={Demo.STORE_ONLINE}&channel=Virtual", token=satellite)
    if check("GET /satellite/campaigns (o que mandar)", status, 200, body):
        mine = next((item for item in body if item.get("id") == campaign_id), {})
        print(f"      audience={mine.get('audience')}, requiredAttributes={mine.get('requiredAttributes')}")

    status, body = api.call("POST", CONCESSIONS, purchase(f"TRX-{tag}-1", {"document": outsider, "loyaltyMember": False, "birthDate": "1990-05-05"}), token=satellite)
    check("POST /satellite/concessions (inelegível)", status, 200, body)
    # Sem outra campanha apta, a recusa é CustomerNotEligible; com uma da rede inteira (roteiros base e do motor), ela ganha (D-14).
    other = (body.get("voucher") or {}).get("campaignId") if isinstance(body, dict) else None
    if not isinstance(body, dict) or other == campaign_id or (other is None and body.get("reason") != "CustomerNotEligible"):
        print(f"      o inelegível não pode receber da campanha restrita: {body}")
        FAILURES.append("recusa por público")
    else:
        print(f"      {'recusada: CustomerNotEligible' if other is None else 'voucher de outra campanha apta (D-14)'}")
        if other is not None:
            api.call("POST", f"{CONCESSIONS}/{body['concessionId']}/cancel", None, satellite)

    status, before = api.call("GET", f"/api/v1/campaigns/{campaign_id}", token=analista)
    status, body = api.call("POST", CONCESSIONS, purchase(f"TRX-{tag}-2", {"document": member, "loyaltyMember": True, "birthDate": "1995-03-10"}), token=satellite)
    granted = check("POST /satellite/concessions (elegível)", status, 200, body) and isinstance(body, dict) and body.get("status") == "Provisional"
    if not granted:
        FAILURES.append("concessão ao elegível")
        return 1
    if isinstance(before, dict) and before.get("reservedVouchers") != 0:
        print(f"      a recusa não deveria reservar saldo: reservedVouchers={before.get('reservedVouchers')}")
        FAILURES.append("saldo da recusa")

    concession_id = body["concessionId"]
    status, body = api.call("POST", f"{CONCESSIONS}/{concession_id}/confirm", None, satellite)
    check("POST /satellite/concessions/{id}/confirm", status, 200, body)
    voucher_id = body["voucher"]["id"]

    # O cliente saiu do clube: o resgate não reavalia o público (RN13).
    status, body = api.call("POST", REDEMPTIONS, purchase(f"TRX-{tag}-3", {"document": member, "loyaltyMember": False}, voucher_id), token=satellite)
    check("POST /satellite/redemptions (sem reavaliar o público)", status, 200, body)

    print()
    if FAILURES:
        print(f"{len(FAILURES)} ponto(s) com falha: " + ", ".join(FAILURES))
        return 1
    print("Roteiro do público restrito: todas as rotas OK.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(run(sys.argv[1]))
