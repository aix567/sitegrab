#!/usr/bin/env python3
"""
SiteGrabber v3.0 — Findings Analyzer
Recursively scans the crawled output directory for secrets, configs,
endpoints, and path disclosures. Emits one JSON object per line (JSONL).
Usage: python analyzer.py <grabbed_dir>
"""

import json
import os
import re
import sys
from pathlib import Path

# ── Pattern registry ──────────────────────────────────────────────────────────
PATTERNS = [
    # Stripe
    {"severity": "Critical", "category": "Secret",
     "pattern": r"sk_live_[0-9A-Za-z]{24,}", "label": "Stripe Live Secret Key"},
    {"severity": "High", "category": "Secret",
     "pattern": r"sk_test_[0-9A-Za-z]{24,}", "label": "Stripe Test Secret Key"},
    # AWS
    {"severity": "Critical", "category": "Secret",
     "pattern": r"AKIA[0-9A-Z]{16}", "label": "AWS Access Key ID"},
    {"severity": "Critical", "category": "Secret",
     "pattern": r"(?i)aws_secret_access_key\s*[=:]\s*[\"']?([A-Za-z0-9+/]{40})",
     "label": "AWS Secret Access Key"},
    # Google
    {"severity": "High", "category": "Secret",
     "pattern": r"AIza[0-9A-Za-z\-_]{35}", "label": "Google API Key"},
    # GitHub
    {"severity": "Critical", "category": "Secret",
     "pattern": r"ghp_[A-Za-z0-9]{36}", "label": "GitHub Personal Access Token"},
    {"severity": "Critical", "category": "Secret",
     "pattern": r"github_pat_[A-Za-z0-9_]{82}", "label": "GitHub Fine-grained PAT"},
    # Private keys
    {"severity": "Critical", "category": "Secret",
     "pattern": r"-----BEGIN (?:RSA |EC |DSA )?PRIVATE KEY-----", "label": "Private Key"},
    # Database URLs
    {"severity": "High", "category": "Config",
     "pattern": r"(?i)(postgres|mysql|mongodb|redis|mssql)://[^/\s\"']{8,}",
     "label": "Database Connection String"},
    # Passwords
    {"severity": "High", "category": "Config",
     "pattern": r"(?i)(?:password|passwd|pwd)\s*[=:]\s*[\"']?([^\s\"'&]{6,})",
     "label": "Hardcoded Password"},
    {"severity": "High", "category": "Config",
     "pattern": r"(?i)DATABASE_PASSWORD\s*[=:]\s*[\"']?([^\s\"'&]{6,})",
     "label": "Database Password"},
    # API keys (generic)
    {"severity": "Medium", "category": "Secret",
     "pattern": r"(?i)api[_\-]?key\s*[=:]\s*[\"']?([A-Za-z0-9\-_]{16,})",
     "label": "Generic API Key"},
    # JWT
    {"severity": "Medium", "category": "Secret",
     "pattern": r"eyJ[A-Za-z0-9\-_]+\.eyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+",
     "label": "JWT Token"},
    # Bearer tokens
    {"severity": "Medium", "category": "Secret",
     "pattern": r"(?i)bearer\s+([A-Za-z0-9\-_.~+/]{20,})",
     "label": "Bearer Token"},
    # Config files exposed
    {"severity": "High", "category": "Config",
     "pattern": r"(?i)(\.env|config\.php|wp-config\.php|\.htpasswd|\.git/config)",
     "label": "Sensitive Config File"},
    # Path disclosure
    {"severity": "Medium", "category": "PathDisclosure",
     "pattern": r"(?:/var|/etc|/home|/usr|/opt|/srv|C:\\[A-Za-z]|D:\\[A-Za-z])[/\\][^\s\"'<>]{4,}",
     "label": "Server Path Disclosure"},
    # Admin/API endpoints
    {"severity": "Low", "category": "Endpoint",
     "pattern": r"(?i)(?:href|src|action|url)\s*=\s*[\"'](/(?:admin|api|internal|debug|test|console|phpmyadmin|swagger|graphql)[^\"']*)",
     "label": "Sensitive Endpoint"},
    # Debug mode
    {"severity": "Medium", "category": "Config",
     "pattern": r"(?i)debug\s*[=:]\s*(?:true|1|on|yes)", "label": "Debug Mode Enabled"},
    # CORS wildcard
    {"severity": "Low", "category": "Config",
     "pattern": r"(?i)access-control-allow-origin:\s*\*", "label": "CORS Wildcard"},
    # Internal IP disclosure
    {"severity": "Info", "category": "Information",
     "pattern": r"\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b",
     "label": "Internal IP Address"},
    # Email addresses
    {"severity": "Info", "category": "Information",
     "pattern": r"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
     "label": "Email Address"},
]

# Compiled patterns
_COMPILED = [
    {**p, "_rx": re.compile(p["pattern"], re.MULTILINE)}
    for p in PATTERNS
]

# File extensions to scan
SCANNABLE_EXT = {
    ".html", ".htm", ".js", ".json", ".xml", ".txt",
    ".css", ".php", ".py", ".rb", ".env", ".cfg",
    ".conf", ".ini", ".yaml", ".yml", ".toml", ".sql",
}

# ── Dedup set ─────────────────────────────────────────────────────────────────
_seen_proofs: set[str] = set()

def emit(severity: str, category: str, target: str, proof: str, location: str):
    key = f"{category}:{proof[:60]}"
    if key in _seen_proofs:
        return
    _seen_proofs.add(key)
    print(json.dumps({
        "severity": severity,
        "category": category,
        "target": target,
        "proof": proof[:200],
        "location": location,
    }), flush=True)

def scan_file(path: Path, grabbed_dir: Path):
    if path.suffix.lower() not in SCANNABLE_EXT:
        return

    try:
        text = path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return

    rel = str(path.relative_to(grabbed_dir))

    for p in _COMPILED:
        for m in p["_rx"].finditer(text):
            proof = m.group(0)[:120]
            line_no = text[:m.start()].count("\n") + 1
            emit(
                severity=p["severity"],
                category=p["category"],
                target=rel,
                proof=proof,
                location=f"{rel} (Line {line_no})",
            )

def main():
    if len(sys.argv) < 2:
        print("[ERROR] Usage: analyzer.py <grabbed_dir>", file=sys.stderr)
        sys.exit(1)

    grabbed_dir = Path(sys.argv[1])
    if not grabbed_dir.exists():
        print(f"[ERROR] Directory not found: {grabbed_dir}", file=sys.stderr)
        sys.exit(1)

    count = 0
    for fp in grabbed_dir.rglob("*"):
        if fp.is_file():
            scan_file(fp, grabbed_dir)
            count += 1

    print(json.dumps({
        "severity": "Info",
        "category": "Information",
        "target": str(grabbed_dir),
        "proof": f"Scanned {count} files, {len(_seen_proofs)} unique findings",
        "location": "analyzer summary",
    }), flush=True)

if __name__ == "__main__":
    main()
