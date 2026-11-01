#!/usr/bin/env python3
"""make-metainfo.py <version> <date> <template> <changelog> <output>

Fills the template's release list with the CHANGELOG.md section of <version>.
"""
import re
import sys
from xml.sax.saxutils import escape

version, date, template, changelog, output = sys.argv[1:6]

text = open(changelog, encoding="utf-8").read()
match = re.search(r"^## %s\s*\n(.*?)(?=^## |\Z)" % re.escape(version), text, re.S | re.M)
items = re.findall(r"^\* (.+)$", match.group(1), re.M) if match else []

if items:
    lis = "\n".join("          <li>%s</li>" % escape(i.replace("`", "")) for i in items)
    body = "      <description>\n        <ul>\n%s\n        </ul>\n      </description>\n" % lis
else:
    body = ""

release = '    <release version="%s" date="%s">\n%s    </release>' % (version, date, body)
open(output, "w", encoding="utf-8").write(open(template, encoding="utf-8").read().replace("@RELEASES@", release))
