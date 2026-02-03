import re

FORBIDDEN_PATTERNS = [
    r";",
    r"\{", r"\}",
    r"\bnew\b",
    r"\btypeof\b",
    r"\bSystem\.",
    r"\bdefault\b",
    r"\bthrow\b",
    r"\btry\b",
    r"\bcatch\b",
    r"\bwhile\b",
    r"\bfor\b",
    r"\bforeach\b",
    r"\breturn\b",
]

IDENTIFIER_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")

STRING_LITERAL_RE = re.compile(
    r"""
    (?:'[^'\\]*(?:\\.[^'\\]*)*')
  | (?:"[^"\\]*(?:\\.[^"\\]*)*")
    """,
    re.VERBOSE
)