"""Recorrido HTTP con datos ficticios. Requiere API local recién iniciada y Python 3.10+."""
import json
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:5088/api/"
urllib.request.install_opener(urllib.request.build_opener(urllib.request.ProxyHandler({})))


def request(path, data=None, expected=200):
    body = None if data is None else json.dumps(data).encode()
    req = urllib.request.Request(BASE + path, data=body, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            status, result = response.status, json.load(response)
    except urllib.error.HTTPError as error:
        status, result = error.code, json.load(error)
    assert status == expected, (path, status, result)
    return result


def apply(data):
    pending = request("proposals", data)
    return request(f"proposals/{pending['id']}/approve", {})


assert request("state")["revision"] == 0, "Reinicia la API antes de ejecutar esta prueba."
rows = [{"id": "DEMO-V001", "folder": "DEMO-C001", "offense": "DESAPARICION"},
        {"id": "DEMO-V002", "folder": "DEMO-C001", "offense": "DESAPARICION"}]


def monthly(data, complements=None):
    return {"kind": "MONTHLY", "period": "2026-09", "rows": data, "complements": complements}


apply(monthly(rows))
apply({"kind": "REGISTRY", "period": "2026-09", "complements": [
    {"id": r["id"], "status": "NO_LOCALIZADA"} for r in rows]})
apply({"kind": "RECLASSIFY", "period": "2026-09", "victimId": "DEMO-V001",
       "newOffense": "NO_DELITO", "reason": "Motivo ficticio"})
request("proposals", monthly(rows), 400)
assert request("state")["pending"] is None
added = {"id": "DEMO-V003", "folder": "DEMO-C002", "offense": "DESAPARICION"}
request("proposals", monthly([rows[1], added]), 400)
result = apply(monthly([rows[1], added], [{"id": added["id"], "status": "NO_LOCALIZADA"}]))
assert len(result["cuts"]["2026-09"]["registry"]) == 3
apply({"kind": "REVERT", "period": "2026-09", "victimId": "DEMO-V001", "reason": "Reversión ficticia"})
request("proposals", monthly([rows[1], added]), 400)
result = apply(monthly(rows + [added]))
assert result["revision"] == 6
assert all(v["offense"] == "DESAPARICION" for v in result["cuts"]["2026-09"]["registry"])
pending = request("proposals", monthly(rows + [added]))
request(f"proposals/{pending['id']}/reject", {})
request(f"proposals/{pending['id']}/approve", {}, 409)
print("OK: recorrido HTTP completo, tres bloqueos de validación y rechazo sin aprobación duplicada.")
