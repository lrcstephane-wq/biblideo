import concurrent.futures
import json
import os
from pathlib import Path
import socket
import sqlite3
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


def request(base_url, route, method="GET", payload=None):
    data = None if payload is None else json.dumps(payload).encode()
    query = urllib.request.Request(
        base_url + route, data, {"Content-Type": "application/json"}, method=method
    )
    try:
        with urllib.request.urlopen(query, timeout=10) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        content = error.read()
        return error.code, json.loads(content) if content else None


def wait_for(check, timeout=15):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            value = check()
            if value:
                return value
        except (OSError, ValueError):
            pass
        time.sleep(0.1)
    raise AssertionError("La condition attendue n’a pas été observée.")


def start_service(binary, environment):
    return subprocess.Popen(
        [os.getenv("NEXUS_DOTNET", "dotnet"), str(binary)],
        env=environment,
        cwd=binary.parent,
    )


def stop_service(process):
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)


def assert_conflict_and_snapshot(binary, directory, environment, base_url):
    process = start_service(binary, environment)
    try:
        wait_for(lambda: request(base_url, "/clients")[1])
        assert request(base_url, "/clients", "POST", {"name": " "})[0] == 400
        status, original = request(base_url, "/clients", "POST", {"name": " Atelier Démo "})
        assert status == 201 and original["name"] == "Atelier Démo" and original["revision"] == 1
        route = "/clients/" + original["id"]
        assert request(base_url, route)[1] == original

        def rename(name):
            return request(base_url, route, "PUT", {"name": name, "revision": 1})

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            results = list(executor.map(rename, ["Atelier Démo A", "Atelier Démo B"]))
        assert sorted(status for status, _ in results) == [200, 409], results
        updated = next(body for status, body in results if status == 200)
        conflict = next(body for status, body in results if status == 409)
        assert updated["id"] == original["id"] and updated["revision"] == 2
        assert conflict["current"] == updated
        assert request(base_url, route, "PUT", updated) == (200, updated)

        def exported_update():
            snapshot = json.loads((directory / "clients.snapshot.json").read_text(encoding="utf-8"))
            return snapshot if snapshot["revision"] == 2 else None

        snapshot = wait_for(exported_update)
        assert snapshot["clients"] == [updated] and snapshot["generatedAtUtc"]
    finally:
        stop_service(process)

    # Le fichier reste lisible sans aucune connexion au service.
    assert exported_update() == snapshot
    assert list(directory.glob("*.tmp")) == []
    with sqlite3.connect(directory / "nexus-dev.sqlite") as database:
        events = database.execute(
            "SELECT previous_state, current_state FROM client_events ORDER BY sequence"
        ).fetchall()
    assert len(events) == 2
    assert events[0][0] is None
    assert json.loads(events[1][0])["Name"] == original["name"]
    assert json.loads(events[1][1])["Name"] == updated["name"]
    return route, updated


def main():
    root = Path(__file__).resolve().parents[2]
    binary = root / "src/Nexus.Service/bin/Release/net10.0/Nexus.Service.dll"
    assert binary.is_file(), "Compiler Nexus.Service en Release avant les tests."

    with tempfile.TemporaryDirectory(prefix="nexus-service-test-") as temporary:
        directory = Path(temporary)
        with socket.socket() as local:
            local.bind(("127.0.0.1", 0))
            port = local.getsockname()[1]
        base_url = f"http://127.0.0.1:{port}"
        environment = {
            **os.environ,
            "IDEO_NEXUS_DEV_DIRECTORY": str(directory),
            "IDEO_NEXUS_DEV_PORT": str(port),
        }
        route, updated = assert_conflict_and_snapshot(binary, directory, environment, base_url)
        restarted = start_service(binary, environment)
        try:
            assert wait_for(lambda: request(base_url, route)[1]) == updated
        finally:
            stop_service(restarted)
    print("Nexus Service. Concurrence, historique, export hors connexion et persistance vérifiés.")


if __name__ == "__main__":
    main()
