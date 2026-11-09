"""Removes every #if P140_DIAG ... #endif block injected into StackContentSync.cs.

Run after the P140 diagnostic round is closed, before building the fixed release.
Keeps the file's original newline style and encoding. Prints the number of removed blocks.
"""
import io
import sys

PATH = r'D:/code/Csharp/The_Long_dark/StackManager/VisualStudio/Utilities/StackContentSync.cs'

raw = io.open(PATH, 'r', encoding='utf-8-sig', newline='').read()
lines = raw.splitlines(keepends=True)

out = []
skipping = False
blocks = 0

for line in lines:
    stripped = line.strip()

    if not skipping and stripped == '#if P140_DIAG':
        skipping = True
        blocks += 1
        continue

    if skipping:
        if stripped == '#endif':
            skipping = False
        continue

    out.append(line)

if skipping:
    print('ERROR: unbalanced #if P140_DIAG block; file left untouched')
    sys.exit(1)

if blocks == 0:
    print('No P140 blocks found; file left untouched')
    sys.exit(0)

io.open(PATH, 'w', encoding='utf-8', newline='').write(''.join(out))
print(f'Removed {blocks} P140 blocks from {PATH}')
