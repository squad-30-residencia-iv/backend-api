"""Roteiro das tags de campanha (spec GH-077) com a API no ar.

Uso: python -I scripts/smoke/tags.py http://localhost:5091

O administrador cria uma tag, o analista aplica a tag num rascunho, filtra a listagem por ela e confere os erros de
nome repetido, tag inexistente e mais de 5 tags.
"""

import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from smoke import FAILURES, Client, Demo, check  # noqa: E402


def run(base_url):
    api = Client(base_url)
    admin = api.login("admin@empresa.com")
    analista = api.login("analista@empresa.com")
    name = f"Black Friday {uuid.uuid4().hex[:6]}"

    status, tag = api.call("POST", "/api/v1/campaign-tags", {"networkId": Demo.NETWORK, "name": name}, admin)
    if not check("POST /campaign-tags", status, 201, tag):
        return 1
    status, body = api.call("POST", "/api/v1/campaign-tags", {"networkId": Demo.NETWORK, "name": name.upper()}, admin)
    check("POST /campaign-tags (nome repetido)", status, 422, body, "CAMPAIGN_TAG_NAME_TAKEN")
    status, body = api.call("POST", "/api/v1/campaign-tags", {"networkId": Demo.NETWORK, "name": "Natal"}, analista)
    check("POST /campaign-tags (analista)", status, 403, body)
    status, body = api.call("GET", f"/api/v1/campaign-tags?networkId={Demo.NETWORK}", token=analista)
    check("GET /campaign-tags (analista)", status, 200, body)

    campaign = api.ready_campaign(analista, name="Tags")
    status, body = api.call("PUT", f"/api/v1/campaigns/{campaign}/tags", {"tagIds": [tag["id"]]}, analista)
    if check("PUT /campaigns/{id}/tags", status, 200, body):
        print(f"      tags: {[item['name'] for item in body.get('tags') or []]}")
    status, body = api.call("PUT", f"/api/v1/campaigns/{campaign}/tags", {"tagIds": [str(uuid.uuid4())]}, analista)
    check("PUT /campaigns/{id}/tags (inexistente)", status, 422, body, "CAMPAIGN_TAG_NOT_FOUND")
    status, body = api.call("PUT", f"/api/v1/campaigns/{campaign}/tags", {"tagIds": [str(uuid.uuid4()) for _ in range(6)]}, analista)
    check("PUT /campaigns/{id}/tags (6 tags)", status, 400, body, "VALIDATION_ERROR")

    status, body = api.call("GET", f"/api/v1/campaigns?networkId={Demo.NETWORK}&tagId={tag['id']}", token=analista)
    if check("GET /campaigns?tagId=", status, 200, body):
        ids = [item["id"] for item in body]
        print(f"      campanhas com a tag: {len(ids)}")
        if ids != [campaign]:
            FAILURES.append("filtro por tag")

    print()
    if FAILURES:
        print(f"{len(FAILURES)} ponto(s) com falha: " + ", ".join(FAILURES))
        return 1
    print("Roteiro das tags: todas as rotas OK.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(run(sys.argv[1]))
