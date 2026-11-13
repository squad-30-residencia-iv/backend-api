"""Verificação de rotas da API com ela no ar (smoke test), sem dependências além da biblioteca padrão.

Uso como comando: python -I scripts/smoke/smoke.py http://localhost:5091
    Roda o roteiro base (health, login, satélite, catálogo, hierarquia e o ciclo da campanha) e
    imprime OK/FAIL por rota. Sai com código 1 se alguma rota falhar.

Uso como biblioteca (roteiros específicos de uma branch):
    import sys; sys.path.insert(0, "D:/residenciaIV/backend-api/scripts/smoke")
    from smoke import Client, Demo, check, local_minute
    api = Client("http://localhost:5091"); analista = api.login("analista@empresa.com")
    status, body = api.call("GET", "/api/v1/campaigns?networkId=" + Demo.NETWORK, token=analista)
    check("GET /campaigns", status, 200, body)
"""

from __future__ import annotations

import json
import sys
import urllib.error
import urllib.request
from datetime import datetime, timedelta
from zoneinfo import ZoneInfo


class Demo:
    """Ids fixos do DemoDataSeeder (appsettings.Development.json)."""

    NETWORK = "0199e3a0-0000-7000-8000-000000000001"
    REGION = "0199e3a0-0000-7000-8000-000000000101"
    STORE_CENTER = "0199e3a0-0000-7000-8000-000000000201"
    STORE_SHOPPING = "0199e3a0-0000-7000-8000-000000000202"
    STORE_ONLINE = "0199e3a0-0000-7000-8000-000000000203"
    CATEGORY_FASHION = "0199e3a0-0000-7000-8000-000000000301"
    CATEGORY_SHOES = "0199e3a0-0000-7000-8000-000000000302"
    CATEGORY_HOME = "0199e3a0-0000-7000-8000-000000000303"
    OTHER_NETWORK = "0199e3a0-0000-7000-8000-0000000000ff"
    TIME_ZONE = "America/Sao_Paulo"
    PASSWORD = "Senha123"
    SATELLITE_CLIENT = "pdv-demo"
    SATELLITE_SECRET = "segredo-local-do-pdv-demo-nao-usar-em-producao"


FAILURES: list[str] = []


def check(name: str, status: int, expected: int, body=None, code: str | None = None) -> bool:
    """Imprime OK/FAIL e guarda a falha. `code` confere a extensão `code` do ProblemDetails."""
    ok = status == expected
    got_code = body.get("code") if isinstance(body, dict) else None
    if ok and code is not None and got_code != code:
        ok = False
    if ok:
        print(f"OK    {name} -> {status}" + (f" {got_code}" if got_code else ""))
    else:
        detail = json.dumps(body, ensure_ascii=False)[:400] if body is not None else ""
        expected_text = f"{expected}" + (f" {code}" if code else "")
        print(f"FAIL  {name} -> esperado {expected_text}, obtido {status} {got_code or ''} {detail}")
        FAILURES.append(name)
    return ok


def local_minute(offset_minutes: int = 0, days: int = 0) -> str:
    """Hora local da rede (sem fuso, truncada ao minuto), no formato que a API aceita."""
    now = datetime.now(ZoneInfo(Demo.TIME_ZONE)).replace(second=0, microsecond=0, tzinfo=None)
    return (now + timedelta(minutes=offset_minutes, days=days)).strftime("%Y-%m-%dT%H:%M:%S")


class Client:
    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip("/")

    def call(self, method: str, path: str, body=None, token: str | None = None):
        """Devolve (status, corpo). O corpo é JSON decodificado, texto, ou None."""
        data = None
        headers = {"Accept": "application/json"}
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if token:
            headers["Authorization"] = f"Bearer {token}"
        request = urllib.request.Request(self.base_url + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                return response.status, _decode(response.read())
        except urllib.error.HTTPError as error:
            return error.code, _decode(error.read())

    def login(self, email: str, password: str = Demo.PASSWORD) -> str:
        status, body = self.call("POST", "/api/v1/auth/login", {"email": email, "password": password})
        if status != 200:
            raise RuntimeError(f"login de {email} falhou: {status} {body}")
        return body["token"]

    def satellite_token(self, client_id: str = Demo.SATELLITE_CLIENT, secret: str = Demo.SATELLITE_SECRET) -> str:
        status, body = self.call("POST", "/api/v1/satellite/token", {"clientId": client_id, "clientSecret": secret})
        if status != 200:
            raise RuntimeError(f"token de satélite falhou: {status} {body}")
        return body["accessToken"]

    def create_campaign(self, token: str, name: str = "Smoke", start_offset_minutes: int = 0, days: int = 30):
        """Rascunho com início no minuto atual (aceito pela RN02) e fim em `days` dias; devolve (status, corpo)."""
        return self.call("POST", "/api/v1/campaigns", {
            "networkId": Demo.NETWORK,
            "name": f"{name} {datetime.now():%H%M%S%f}",
            "description": "smoke test",
            "startsAt": local_minute(start_offset_minutes),
            "endsAt": local_minute(start_offset_minutes, days=days)[:10] + "T23:59:00",
        }, token)

    def ready_campaign(self, analista: str, **kwargs) -> str:
        """Rascunho com tudo o que a submissão exige. Devolve o id; lança se algum passo falhar."""
        status, body = self.create_campaign(analista, **kwargs)
        if status != 201 and status != 200:
            raise RuntimeError(f"POST /campaigns: {status} {body}")
        campaign_id = body["id"]
        steps = [
            ("items", {"items": [{"targetType": "All", "benefitType": "Percentage", "benefitValue": 10}]}),
            ("quantities", {"totalVoucherLimit": 100}),
            ("audience", {"audienceType": "AllCustomers"}),
            ("voucher-validity", {"type": "Relative", "amount": 30, "unit": "Days"}),
            ("usage-restrictions", {"maxUsesPerVoucher": 1}),
            ("conditions", {"conditions": [], "allowUseInSamePurchase": False}),
            ("scopes", {"scopes": [{"channels": ["Physical", "Virtual"]}]}),   # rede inteira (US-02.3); 404 em branches antigas
        ]
        for route, payload in steps:
            status, body = self.call("PUT", f"/api/v1/campaigns/{campaign_id}/{route}", payload, analista)
            if route == "scopes" and status == 404 and not isinstance(body, dict):
                continue
            if status != 200:
                raise RuntimeError(f"PUT /campaigns/{{id}}/{route}: {status} {body}")
        return campaign_id


def _decode(raw: bytes):
    if not raw:
        return None
    text = raw.decode("utf-8", errors="replace")
    try:
        return json.loads(text)
    except ValueError:
        return text


def run_base(base_url: str) -> int:
    api = Client(base_url)

    status, body = api.call("GET", "/health")
    check("GET /health", status, 200, body)
    status, body = api.call("GET", "/api/v1/system/status")
    check("GET /api/v1/system/status (público)", status, 200, body)

    tokens = {}
    for role, email in (("analista", "analista@empresa.com"), ("gestor", "gestor@empresa.com"), ("admin", "admin@empresa.com")):
        status, body = api.call("POST", "/api/v1/auth/login", {"email": email, "password": Demo.PASSWORD})
        if check(f"POST /auth/login ({role})", status, 200, body):
            tokens[role] = body["token"]
    status, body = api.call("POST", "/api/v1/auth/login", {"email": "analista@empresa.com", "password": "errada"})
    check("POST /auth/login (senha errada)", status, 401, body, "AUTH_INVALID_CREDENTIALS")

    if len(tokens) < 3:
        print("Sem os três tokens: o restante do roteiro base não roda.")
        return 1
    analista, gestor, admin = tokens["analista"], tokens["gestor"], tokens["admin"]

    status, body = api.call("POST", "/api/v1/satellite/token", {"clientId": Demo.SATELLITE_CLIENT, "clientSecret": Demo.SATELLITE_SECRET})
    satellite = body.get("accessToken") if check("POST /satellite/token", status, 200, body) else None
    if satellite:
        status, body = api.call("GET", "/api/v1/satellite/me", token=satellite)
        check("GET /satellite/me", status, 200, body)
        status, body = api.call("GET", f"/api/v1/campaigns?networkId={Demo.NETWORK}", token=satellite)
        check("GET /campaigns (token de satélite)", status, 401, body)
    status, body = api.call("POST", "/api/v1/satellite/token", {"clientId": Demo.SATELLITE_CLIENT, "clientSecret": "x" * 40})
    check("POST /satellite/token (segredo errado)", status, 401, body, "AUTH_INVALID_CLIENT")

    status, body = api.call("GET", "/api/v1/categories", token=analista)
    check("GET /categories", status, 200, body)
    status, body = api.call("GET", "/api/v1/products", token=analista)
    check("GET /products", status, 200, body)
    status, body = api.call("GET", f"/api/v1/regions?networkId={Demo.NETWORK}", token=analista)
    check("GET /regions", status, 200, body)
    status, body = api.call("GET", f"/api/v1/establishments?regionId={Demo.REGION}", token=analista)
    check("GET /establishments", status, 200, body)
    status, body = api.call("GET", f"/api/v1/regions?networkId={Demo.OTHER_NETWORK}", token=analista)
    check("GET /regions (outra rede)", status, 403, body, "SCOPE_FORBIDDEN")

    status, body = api.call("GET", f"/api/v1/campaigns?networkId={Demo.NETWORK}", token=analista)
    check("GET /campaigns", status, 200, body)

    status, body = api.create_campaign(analista, "Smoke base")
    if not check("POST /campaigns", status, 201, body):
        return 1
    campaign_id = body["id"]
    status, body = api.call("POST", "/api/v1/campaigns", {"networkId": Demo.NETWORK, "name": "x", "startsAt": local_minute(60), "endsAt": local_minute(30)}, analista)
    check("POST /campaigns (fim antes do início)", status, 422, body, "CAMPAIGN_INVALID_PERIOD")

    status, body = api.call("GET", f"/api/v1/campaigns/{campaign_id}", token=analista)
    check("GET /campaigns/{id}", status, 200, body)
    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/submit", None, analista)
    check("POST /campaigns/{id}/submit (rascunho incompleto)", status, 422, body)

    steps = [
        ("items", {"items": [{"targetType": "Category", "categoryId": Demo.CATEGORY_FASHION, "benefitType": "Percentage", "benefitValue": 10, "voucherLimit": 40}, {"targetType": "All", "benefitType": "FixedAmount", "benefitValue": 15}]}),
        ("quantities", {"totalVoucherLimit": 100, "perCustomerVoucherLimit": 2}),
        ("audience", {"audienceType": "AllCustomers"}),
        ("voucher-validity", {"type": "Relative", "amount": 30, "unit": "Days"}),
        ("usage-restrictions", {"minPurchaseAmount": 50, "maxUsesPerVoucher": 1}),
        ("conditions", {"conditions": [{"type": "PurchaseTotal", "minAmount": 150}], "allowUseInSamePurchase": True}),
    ]
    for route, payload in steps:
        status, body = api.call("PUT", f"/api/v1/campaigns/{campaign_id}/{route}", payload, analista)
        check(f"PUT /campaigns/{{id}}/{route}", status, 200, body)

    # US-02.3 (PR #51): a submissão passa a exigir escopo. Em branches sem a rota, ela responde 404 e o passo é pulado.
    scopes = {"scopes": [{"establishmentId": Demo.STORE_CENTER, "channels": ["Physical"]}, {"establishmentId": Demo.STORE_ONLINE, "channels": ["Virtual"]}]}
    status, body = api.call("PUT", f"/api/v1/campaigns/{campaign_id}/scopes", scopes, analista)
    if status == 404 and not isinstance(body, dict):
        print("      PUT /campaigns/{id}/scopes não existe nesta branch (anterior à US-02.3): passo pulado")
    else:
        check("PUT /campaigns/{id}/scopes", status, 200, body)

    status, body = api.call("PUT", f"/api/v1/campaigns/{campaign_id}/conditions", {"conditions": [{"type": "ItemQuantity", "minQuantity": 0}], "allowUseInSamePurchase": False}, analista)
    check("PUT /campaigns/{id}/conditions (minQuantity 0)", status, 422, body, "CAMPAIGN_CONDITION_MIN_QUANTITY_NOT_POSITIVE")

    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/submit", None, gestor)
    check("POST /campaigns/{id}/submit (gestor)", status, 403, body, "AUTH_FORBIDDEN_ROLE")
    status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/submit", None, analista)
    submitted = check("POST /campaigns/{id}/submit", status, 200, body)
    if submitted:
        status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/approve", None, analista)
        check("POST /campaigns/{id}/approve (analista)", status, 403, body, "AUTH_FORBIDDEN_ROLE")
        status, body = api.call("POST", f"/api/v1/campaigns/{campaign_id}/approve", None, gestor)
        if check("POST /campaigns/{id}/approve", status, 200, body):
            print(f"      status efetivo após aprovar: {body.get('status')}")
        status, body = api.call("PUT", f"/api/v1/campaigns/{campaign_id}/quantities", {"totalVoucherLimit": 10}, analista)
        check("PUT /campaigns/{id}/quantities (fora de rascunho)", status, 422, body, "CAMPAIGN_NOT_DRAFT")

    status, body = api.create_campaign(analista, "Smoke reprovar")
    rejected_id = body["id"] if status == 201 else None
    if rejected_id:
        for route, payload in steps:
            api.call("PUT", f"/api/v1/campaigns/{rejected_id}/{route}", payload, analista)
        api.call("PUT", f"/api/v1/campaigns/{rejected_id}/scopes", scopes, analista)
        api.call("POST", f"/api/v1/campaigns/{rejected_id}/submit", None, analista)
        status, body = api.call("POST", f"/api/v1/campaigns/{rejected_id}/reject", {"reason": "Fora do calendário"}, gestor)
        check("POST /campaigns/{id}/reject", status, 200, body)

    status, body = api.call("GET", f"/api/v1/campaigns?networkId={Demo.NETWORK}&status=Active", token=analista)
    check("GET /campaigns?status=Active", status, 200, body)

    # Acompanhamento (spec GH-050): indicadores, lista, trilha e CSV
    status, body = api.call("GET", f"/api/v1/campaigns/{campaign_id}/dashboard", token=analista)
    check("GET /campaigns/{id}/dashboard", status, 200, body)
    if isinstance(body, dict):
        ind = body.get("indicators", {})
        print(f"      emitidos {ind.get('issued')}, resgatados {ind.get('redeemed')}, taxa {ind.get('utilizationRate')}, status {body.get('status')}")
    status, body = api.call("GET", f"/api/v1/campaigns/{campaign_id}/dashboard?from=2020-01-01&to=2019-12-31", token=analista)
    check("GET /campaigns/{id}/dashboard (período invertido)", status, 400, body, "VALIDATION_ERROR")
    status, body = api.call("GET", f"/api/v1/dashboard?networkId={Demo.NETWORK}&establishmentId={Demo.STORE_CENTER}", token=analista)
    check("GET /dashboard?networkId&establishmentId", status, 200, body)
    if isinstance(body, dict) and not any(c.get("campaignId") == campaign_id for c in body.get("campaigns", [])):
        print("FAIL  a campanha do roteiro não apareceu na lista do dashboard")
        FAILURES.append("dashboard lista")
    status, body = api.call("GET", f"/api/v1/dashboard?networkId={Demo.OTHER_NETWORK}", token=analista)
    check("GET /dashboard (outra rede)", status, 403, body, "SCOPE_FORBIDDEN")
    status, body = api.call("GET", f"/api/v1/campaigns/{campaign_id}/audit", token=gestor)
    check("GET /campaigns/{id}/audit", status, 200, body)
    if isinstance(body, dict):
        kinds = [e.get("eventType") for e in body.get("entries", [])]
        print(f"      trilha: {', '.join(kinds)}")
        if not kinds or kinds[0] != "CampaignCreated" or "CampaignApproved" not in kinds:
            print("FAIL  a trilha deveria começar pela criação e conter a aprovação")
            FAILURES.append("trilha")
    status, body = api.call("GET", f"/api/v1/dashboard/export?networkId={Demo.NETWORK}", token=analista)
    check("GET /dashboard/export (CSV)", status, 200, body)
    status, body = api.call("GET", f"/api/v1/dashboard/export?networkId={Demo.NETWORK}&status=Cancelled", token=analista)
    check("GET /dashboard/export (sem dados)", status, 422, body, "EXPORT_EMPTY")
    status, body = api.call("GET", f"/api/v1/campaigns/{campaign_id}/audit/export", token=analista)
    check("GET /campaigns/{id}/audit/export (CSV)", status, 200, body)

    print()
    if FAILURES:
        print(f"{len(FAILURES)} rota(s) com falha: " + ", ".join(FAILURES))
        return 1
    print("Roteiro base: todas as rotas OK.")
    return 0


if __name__ == "__main__":
    # Acentos legíveis no console do Windows.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(run_base(sys.argv[1]))
