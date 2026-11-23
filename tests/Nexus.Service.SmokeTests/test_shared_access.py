import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import ssl
import subprocess
import tempfile
import urllib.error
import urllib.request
import uuid

from test_registry import start_service, stop_service, wait_for


def run(directory, binary):
    pem = directory / "certificate.pem"
    pfx = directory / "certificate.pfx"
    key = directory / "test.key"
    password = secrets.token_hex(16)
    env = {**os.environ, "IDEO_NEXUS_TLS_PASSWORD": password}
    subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-keyout", str(key),
                    "-out", str(pem), "-days", "1", "-subj", "/CN=localhost",
                    "-addext", "subjectAltName=DNS:localhost"], check=True, capture_output=True)
    subprocess.run(["openssl", "pkcs12", "-export", "-in", str(pem), "-inkey", str(key),
                    "-out", str(pfx), "-passout", "env:IDEO_NEXUS_TLS_PASSWORD"],
                   env=env, check=True, capture_output=True)

    tokens = {role: secrets.token_hex(32) for role in ("Reader", "Editor", "Administrator")}
    accounts = directory / "accounts.json"
    accounts.write_text(json.dumps({"accounts": [
        {"name": role, "role": role, "tokenSha256": hashlib.sha256(token.encode()).hexdigest()}
        for role, token in tokens.items()
    ]}), encoding="utf-8")

    with socket.socket() as local:
        local.bind(("127.0.0.1", 0))
        port = local.getsockname()[1]
    env.update(IDEO_NEXUS_ACCESS_FILE=str(accounts), IDEO_NEXUS_TLS_PFX=str(pfx),
               IDEO_NEXUS_DATA_DIRECTORY=str(directory / "data"), IDEO_NEXUS_PORT=str(port))
    tls = ssl.create_default_context(cafile=str(pem))

    def request(path, role=None, method="GET", payload=None):
        headers = {"Content-Type": "application/json"}
        if role:
            headers["Authorization"] = "Bearer " + tokens.get(role, role)
        data = None if payload is None else json.dumps(payload).encode()
        query = urllib.request.Request(f"https://localhost:{port}{path}", data, headers, method=method)
        try:
            with urllib.request.urlopen(query, context=tls, timeout=5) as response:
                return response.status, json.load(response), response.headers
        except urllib.error.HTTPError as error:
            return error.code, None, error.headers

    process = start_service(binary, env)
    try:
        assert wait_for(lambda: request("/session")[0]) == 401
        assert request("/clients")[0] == 401
        assert request("/clients", secrets.token_hex(32))[0] == 401
        assert request("/session", "Reader")[1] == {"name": "Reader", "role": "Reader"}
        assert request("/clients", "Reader")[2]["Cache-Control"] == "no-store"
        assert request("/clients", "Reader", "POST", {"name": "Refusé"})[0] == 403
        assert request("/clients", "Editor", "POST", {"name": "Autorisé"})[0] == 201
        sample = {"id": str(uuid.uuid4()), "name": "Import de test"}
        assert request("/clients/import-biblideo/", "Editor", "POST", [sample])[0] == 403
        assert request("/clients/import-biblideo", "Administrator", "POST", [sample])[0] == 200
        assert request("/atlas-licenses", "Reader")[0] == 200
        assert request("/atlas-licenses/test/File/", "Reader")[0] == 403
        assert request("/atlas-licenses/test/file", "Editor")[0] == 403
        assert request("/atlas-licenses/test/file", "Administrator")[0] == 404
        assert request("/atlas-licenses/test", "Editor", "PUT", {})[0] == 403
        assert request("/atlas-licenses/test", "Administrator", "PUT", {})[0] == 400
    finally:
        stop_service(process)

    assert (directory / "data" / "nexus.sqlite").exists()
    assert not (directory / "data" / "clients.snapshot.json").exists()
    print("HTTPS Nexus. Accès anonymes refusés, lecteurs en lecture seule, éditeurs limités et opérations Atlas réservées à l’administration.")


if __name__ == "__main__":
    project = Path(__file__).resolve().parents[2]
    with tempfile.TemporaryDirectory(prefix="nexus-shared-test-") as temporary:
        run(Path(temporary), project / "src/Nexus.Service/bin/Release/net10.0/Nexus.Service.dll")
