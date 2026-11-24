#!/usr/bin/env python3
"""Opt-in live check: create two synthetic users, test duplicates, then delete only those users."""
import json
import secrets
import sys
import urllib.error
import urllib.request
from pathlib import Path


def main():
    env = {}
    for line in Path("HeartLog.Api/.env").read_text().splitlines():
        if "=" in line and not line.lstrip().startswith("#"):
            key, value = line.split("=", 1)
            env[key.strip()] = value.strip().strip("\"'")
    base = env["Supabase__ProjectUrl"].rstrip("/")
    public = env["Supabase__PublishableKey"]
    secret = env["Supabase__SecretKey"]
    created = []

    def request(method, path, key, payload=None):
        headers = {"apikey": key, "Content-Type": "application/json"}
        if key.startswith("eyJ"):
            headers["Authorization"] = "Bearer " + key
        data = json.dumps(payload).encode() if payload is not None else None
        req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=20) as response:
                raw = response.read()
                return response.status, json.loads(raw) if raw else {}
        except urllib.error.HTTPError as error:
            return error.code, {}

    ok = True
    try:
        for confirmed in (False, True):
            email = "heartlog-agreement-test-" + secrets.token_hex(12) + "@example.com"
            original = secrets.token_hex(32)
            status, user = request("POST", "/auth/v1/admin/users", secret, {
                "email": email, "password": secrets.token_urlsafe(24),
                "email_confirm": confirmed, "user_metadata": {"heartlog_registration_intent": original},
            })
            if status not in (200, 201) or not user.get("id"):
                print(f"Could not create {'confirmed' if confirmed else 'unconfirmed'} test user (HTTP {status}).")
                ok = False
                continue
            user_id = user["id"]
            created.append(user_id)
            status, _ = request("POST", "/auth/v1/signup", public, {
                "email": email, "password": secrets.token_urlsafe(24),
                "data": {"heartlog_registration_intent": secrets.token_hex(32)},
            })
            after_status, after = request("GET", "/auth/v1/admin/users/" + user_id, secret)
            preserved = after_status == 200 and after.get("user_metadata", {}).get("heartlog_registration_intent") == original
            print(f"{'Confirmed' if confirmed else 'Unconfirmed'} duplicate: signup HTTP {status}; metadata {'preserved' if preserved else 'NOT VERIFIED' }.")
            ok = ok and preserved and status in (200, 400, 422)
    finally:
        for user_id in created:
            try:
                status, _ = request("DELETE", "/auth/v1/admin/users/" + user_id, secret)
                if status not in (200, 204, 404):
                    print(f"Cleanup failed for test user {user_id} (HTTP {status}).", file=sys.stderr)
                    ok = False
            except Exception:
                print(f"Cleanup failed for test user {user_id}.", file=sys.stderr)
                ok = False
    return 0 if ok and len(created) == 2 else 1


if __name__ == "__main__":
    sys.exit(main())
