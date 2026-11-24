# Builds the puzzle review page (the artifact "Winter Isle Puzzle Variety") from template.html and puzzles.json: the
# data goes into the script, and every count quoted in the page's prose is filled in from the same data, so the text
# cannot disagree with the grid. Writes shots\puzzle-variety\puzzle-variety.html (not committed; publish that file).
# Its thumbnails, shots\puzzle-variety\img\<Name>.jpg, were made from shots\puzzles\<Name>-s.png and are already
# on the published page.
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
P = json.load(open(os.path.join(HERE, 'puzzles.json'), encoding='utf-8'))
html = open(os.path.join(HERE, 'template.html'), encoding='utf-8').read()
by_id = {p['id']: p for p in P}


def n(tag):
    return sum(1 for p in P if tag in p['tags'])


def pair(a, b):
    A, B = set(by_id[a]['tags']), set(by_id[b]['tags'])
    return '%d of %d' % (len(A & B), len(A | B))


fam = {f: sum(1 for p in P if p['fam'] == f) for f in ('lit', 'hold', 'match', 'tune')}
lo, hi = sum(p['time'][0] for p in P), sum(p['time'][1] for p in P)
coded = sum(1 for p in P if p['code'])
html = html.replace('/*DATA*/[]', json.dumps(P, ensure_ascii=False, separators=(',', ':')))
html = html.replace('{{hours}}', '%d to %d' % (round(lo / 60), round(hi / 60)))
html = html.replace('{{n_code}}', str(coded)).replace('{{n_nocode}}', str(len(P) - coded))
html = html.replace('{{n_pio}}', str(fam['lit'] + fam['hold'])).replace('{{n_board}}', str(fam['match'] + fam['tune']))
html = re.sub(r'\{\{n:(\w+)\}\}', lambda m: str(n(m.group(1))), html)
html = re.sub(r'\{\{fam:(\w+)\}\}', lambda m: str(fam[m.group(1)]), html)
html = re.sub(r'\{\{pair:(\w+):(\w+)\}\}', lambda m: pair(m.group(1), m.group(2)), html)
left = re.findall(r'\{\{[^}]*\}\}', html)
if left:
    sys.exit('unfilled: %s' % left)

# what the prose claims in words, checked against the data
assert len(P) == 29
assert fam == {'lit': 13, 'hold': 3, 'match': 11, 'tune': 2}, fam
assert n('pad') + n('ski') + n('throw') + 1 == fam['lit']                         # the "(1)" is Snowman
assert [p['id'] for p in P if 'haul' in p['tags'] and p['fam'] == 'lit' and not ({'pad', 'ski', 'throw'} & set(p['tags']))] == ['Snowman']
calm = sorted(p['id'] for p in P if not ({'streak', 'beat', 'clock'} & set(p['tags'])))
assert calm == ['BaubleHunt', 'SnowBridge', 'WhisperingChimney'], calm
assert min(p['time'][0] for p in P) == 8 and by_id['SleighRun']['time'] == [8, 15]
OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'shots', 'puzzle-variety'))
os.makedirs(OUT, exist_ok=True)
for p in P:
    assert p['chain'][0].get('r') and p['chain'][-1].get('r') and len(p['chain']) % 2 == 1, p['id']
    for k, c in enumerate(p['chain']):
        assert ('r' in c) == (k % 2 == 0), (p['id'], k)
        if 'via' in c:
            assert c['dir'] in ('fwd', 'back', 'both'), p['id']

out = os.path.join(OUT, 'puzzle-variety.html')
open(out, 'w', encoding='utf-8', newline='\n').write(html)
print('wrote', os.path.normpath(out), len(html.encode('utf-8')) // 1024, 'KB;', 'hours', lo / 60, hi / 60)
for a, b in (('AdventTrail', 'RadioSilence'), ('AdventTrail', 'StockingSort'), ('Footprints', 'ThinIce'), ('HoldTheStar', 'MidnightFinale'),
             ('BlizzardWalk', 'WhiteoutMaze'), ('Downhill', 'Slalom'), ('SnowballRange', 'SummitShot'), ('BlindCarols', 'WhisperingChimney'),
             ('BaubleHunt', 'SnowBridge'), ('BaubleHunt', 'BlindSculptor'), ('BlindSculptor', 'SnowBridge')):
    print('  ', a, b, pair(a, b))
