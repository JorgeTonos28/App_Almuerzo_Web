"""Optional local safeguard. Reads a workbook privately; emits counts only."""
import pathlib
import subprocess
import sys
import openpyxl

if len(sys.argv) != 2:
    raise SystemExit('Usage: check-private-staging.py <private source workbook>')
book = openpyxl.load_workbook(sys.argv[1], read_only=True, data_only=True)
emails = {str(row[0]).strip().lower() for row in list(book['Usuarios'].values)[1:] if row[0]}
secrets = []
for row in list(book['Config'].values)[1:]:
    if row[0] and any(word in str(row[0]).upper() for word in ('TOKEN', 'SECRET', 'PASSWORD', 'CREDENTIAL')) and row[1] and len(str(row[1])) >= 8:
        secrets.append(str(row[1]).encode())
book.close()
paths = subprocess.check_output(['git', 'diff', '--cached', '--name-only', '-z']).split(b'\0')
hits = 0
for raw in filter(None, paths):
    name = raw.decode()
    if pathlib.PurePosixPath(name).suffix.lower() in {'.db', '.xlsx', '.xls', '.csv', '.tsv', '.pfx', '.pem'} or 'App_Data/' in name:
        hits += 1
        continue
    data = subprocess.check_output(['git', 'show', ':' + name])
    if any(secret in data for secret in secrets):
        hits += 1
    try:
        text = data.decode('utf8').lower()
    except UnicodeDecodeError:
        continue
    if any(email in text for email in emails):
        hits += 1
print(f'Private staging check: {hits} files with sensitive-source matches.')
raise SystemExit(1 if hits else 0)
