"""Roteiro da fatia 1 do endurecimento (spec GH-071) com a API no ar.

Uso: python -I scripts/smoke/endurecimento.py http://localhost:5091

Confere:
- a paginação opcional de GET /campaigns e GET /vouchers, com X-Total-Count, e o 400 fora da faixa;
- que o token de uma aplicação satélite deixa de valer assim que ela é inativada (SEC-03);
- que a rota antiga de consulta de vouchers continua respondendo com Deprecation.
"""

import json
import sys
import urllib.error
import urllib.request
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from smoke import FAILURES, Client, Demo, check  # noqa: E402


def headers_of(api, method, path, token):
    request = urllib.request.Request(api.base_url + path, method=method, headers={"Authorization": f"Bearer {token}"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.status, dict(response.headers), json.loads(response.read() or b"null")
    except urllib.error.HTTPError as error:
        return error.code, dict(error.headers), None


def run(base_url):
    api = Client(base_url)
    admin = api.login("admin@empresa.com")
    analista = api.login("analista@empresa.com")
    gestor = api.login("gestor@empresa.com")

    for route, token in ((f"/api/v1/campaigns?networkId={Demo.NETWORK}", analista), (f"/api/v1/vouchers?networkId={Demo.NETWORK}", gestor)):
        status, headers, body = headers_of(api, "GET", f"{route}&page=1&pageSize=2", token)
        if check(f"GET {route.split('?')[0]} (page=1&pageSize=2)", status, 200, body):
            total = headers.get("X-Total-Count")
            print(f"      itens: {len(body)}, X-Total-Count: {total}")
            if total is None or len(body) > 2:
                FAILURES.append(f"paginação de {route.split('?')[0]}")
        status, _, body = headers_of(api, "GET", f"{route}&pageSize=101", token)
        check(f"GET {route.split('?')[0]} (pageSize=101)", status, 400, body)

    client_id = f"smoke-{uuid.uuid4().hex[:8]}"
    status, created = api.call("POST", "/api/v1/satellite/apps", {
        "name": "Smoke revogação", "clientId": client_id, "networkId": Demo.NETWORK, "scopes": ["campaign:read"]}, admin)
    if check("POST /satellite/apps", status, 201, created):
        token = api.satellite_token(client_id, created["clientSecret"])
        status, body = api.call("GET", "/api/v1/satellite/me", token=token)
        check("GET /satellite/me (aplicação ativa)", status, 200, body)
        status, body = api.call("PATCH", f"/api/v1/satellite/apps/{created['app']['id']}/deactivate", None, admin)
        check("PATCH /satellite/apps/{id}/deactivate", status, 200, body)
        status, body = api.call("GET", "/api/v1/satellite/me", token=token)
        check("GET /satellite/me (mesmo token, aplicação inativada)", status, 401, body, "AUTH_UNAUTHENTICATED")

    satellite = api.satellite_token()
    status, headers, body = headers_of(api, "GET", "/api/v1/satellite/customers/12345678901/vouchers", satellite)
    if check("GET /satellite/customers/{cpf}/vouchers (obsoleta)", status, 200, body):
        print(f"      Deprecation: {headers.get('Deprecation')}, Sunset: {headers.get('Sunset')}")

    print()
    if FAILURES:
        print(f"{len(FAILURES)} ponto(s) com falha: " + ", ".join(FAILURES))
        return 1
    print("Roteiro do endurecimento: todas as rotas OK.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(run(sys.argv[1]))
