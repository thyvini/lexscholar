#!/usr/bin/env python3
"""Owner-operated disposable Docs OAuth experiment; never persist tokens."""

import getpass
import contextlib
import io
import sys
from urllib.parse import quote

import requests
from google.auth.transport.requests import Request
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import InstalledAppFlow

SCOPE = "https://www.googleapis.com/auth/documents.readonly"
MARKERS = ("S001_TOP_ONE", "S001_TOP_TWO", "S001_CHILD")


def body_text(value):
    if isinstance(value, dict):
        return "".join(
            item if key == "content" and isinstance(item, str) else body_text(item)
            for key, item in value.items()
        )
    if isinstance(value, list):
        return "".join(map(body_text, value))
    return ""


def check_document(document):
    tabs = document.get("tabs", [])
    if len(tabs) < 2:
        return False
    children = tabs[0].get("childTabs", [])
    if not children:
        return False

    def visit(tab):
        yield body_text(tab.get("documentTab", {}).get("body", {}))
        for child in tab.get("childTabs", []):
            yield from visit(child)

    all_text = [text for tab in tabs for text in visit(tab)]
    first = body_text(tabs[0].get("documentTab", {}).get("body", {}))
    second = body_text(tabs[1].get("documentTab", {}).get("body", {}))
    descendants = list(visit(tabs[0]))[1:]
    return (
        MARKERS[0] in first
        and MARKERS[1] in second
        and any(MARKERS[2] in text for text in descendants)
        and all(any(marker in text for text in all_text) for marker in MARKERS)
    )


def read_document(credentials, document_id):
    response = requests.get(
        "https://docs.googleapis.com/v1/documents/"
        + quote(document_id, safe="")
        + "?includeTabsContent=true",
        headers={"Authorization": "Bearer " + credentials.token},
        timeout=30,
        allow_redirects=False,
    )
    if response.status_code != 200:
        print("FAIL: document read HTTP", response.status_code)
        return False
    data = response.json()
    return data.get("documentId") == document_id and check_document(data)


def main():
    client_path = input("Absolute path to disposable desktop client JSON: ").strip()
    document_id = getpass.getpass("Disposable document ID (hidden): ").strip()
    if not document_id:
        raise ValueError("Empty document ID")
    flow = InstalledAppFlow.from_client_secrets_file(client_path, scopes=[SCOPE])
    print("Opening your browser for consent; return here afterward.")
    with contextlib.redirect_stdout(io.StringIO()):
        credentials = flow.run_local_server(host="127.0.0.1", port=0, open_browser=True)
    if set(credentials.granted_scopes or []) != {SCOPE} or not credentials.refresh_token:
        print("FAIL: scope mismatch or no refresh token; revoke the project grant manually.")
        return 1
    print("PASS: requested scope and refresh token received")
    if not read_document(credentials, document_id):
        print("FAIL: initial document or tab markers; revoke the project grant manually.")
        return 1
    print("PASS: private document and tab markers")
    credentials.refresh(Request())
    if not read_document(credentials, document_id):
        print("FAIL: document after refresh; revoke the project grant manually.")
        return 1
    print("PASS: immediate refresh and repeated read")
    if input("Revoke all this account's grants for this project? Type REVOKE: ") != "REVOKE":
        print("INCONCLUSIVE: not revoked; revoke the project grant manually.")
        return 1
    response = requests.post(
        "https://oauth2.googleapis.com/revoke",
        data={"token": credentials.refresh_token},
        timeout=30,
        allow_redirects=False,
    )
    if response.status_code != 200:
        print("INCONCLUSIVE: revocation HTTP", response.status_code)
        return 1
    print("PASS: Google acknowledged revocation")
    try:
        Credentials(
            token=None,
            refresh_token=credentials.refresh_token,
            token_uri=credentials.token_uri,
            client_id=credentials.client_id,
            client_secret=credentials.client_secret,
            scopes=[SCOPE],
        ).refresh(Request())
    except Exception as exc:
        # Google can take time to enforce revocation; only invalid_grant is evidence.
        if "invalid_grant" in str(exc):
            print("PASS: refresh rejected with invalid_grant after revocation")
            return 0
        print("INCONCLUSIVE: refresh failed with", type(exc).__name__)
        return 1
    print("INCONCLUSIVE: refresh still works; revocation may not have propagated")
    return 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (Exception, KeyboardInterrupt) as exc:
        # Never print exception messages: providers may include sensitive values.
        print("INCONCLUSIVE:", type(exc).__name__, "- revoke the project grant manually.")
        sys.exit(1)
