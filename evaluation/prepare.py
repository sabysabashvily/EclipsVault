#!/usr/bin/env python3
"""Create local dummy-only settings without overwriting an existing configuration."""

import argparse
import base64
import json
import os
from pathlib import Path
import secrets


def write_private(path: Path, content: str) -> None:
    with os.fdopen(os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as stream:
        stream.write(content)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("candidate", type=Path, help="Checkout containing the patched application")
    args = parser.parse_args()
    candidate = args.candidate.resolve()
    lab = Path(__file__).resolve().parent
    settings_path = candidate / "EclipsVault.Web/appsettings.Development.json"
    if not (candidate / "EclipsVault.slnx").is_file():
        parser.error("Candidate must contain EclipsVault.slnx")
    if settings_path.exists() or (lab / ".env").exists():
        parser.error("Local settings already exist; refusing to overwrite credentials or encryption keys")

    os.umask(0o077)
    state = lab / ".local"
    state.mkdir(mode=0o700, exist_ok=True)
    keyring = state / "keyring"
    keyring.mkdir(mode=0o700, exist_ok=True)
    database_password = secrets.token_hex(24)
    admin_password = "Eval-" + secrets.token_hex(12) + "!"
    member_password = "Eval-" + secrets.token_hex(12) + "!"
    settings = {
        "ConnectionStrings": {"DefaultConnection":
            "Host=127.0.0.1;Port=55432;Database=eclipsvault_eval;Username=postgres;"
            f"Password={database_password}"},
        "Database": {"Provider": "Postgres"},
        "AllowedHosts": "localhost;127.0.0.1;[::1]",
        "Crypto": {"Engine": "AesGcmLocal", "AllowDevelopmentKekFallback": True,
                   "DevelopmentKekBase64": base64.b64encode(secrets.token_bytes(32)).decode()},
        "AuditSigning": {"AllowDevelopmentEphemeralKey": True},
        "DataProtection": {"KeyRingPath": str(keyring)},
        "Email": {"Sender": "Smtp", "Enabled": True,
                  "Smtp": {"Host": "127.0.0.1", "Port": 11025, "UseSsl": False}},
        "Redis": {"Enabled": False},
        "Seed": {"AdminPassword": admin_password, "DevPassword": member_password},
    }
    write_private(lab / ".env", f"POSTGRES_PASSWORD={database_password}\n")
    write_private(settings_path, json.dumps(settings, indent=2) + "\n")
    write_private(state / "credentials.txt",
                  f"LOCAL DUMMY-DATA ACCOUNTS ONLY\nvault-admin: {admin_password}\n"
                  f"dev-user: {member_password}\n")
    print(f"Prepared candidate: {candidate}")
    print(f"Test login details: {state / 'credentials.txt'} (open locally; do not share in chat)")
    print("Database uses RAM; stopping its container discards test data. No backups are created.")


if __name__ == "__main__":
    main()
