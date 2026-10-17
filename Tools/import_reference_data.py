"""Import ONLY declarative level/atlas/sprite data and media, never game scripts.
Usage: python3 Tools/import_reference_data.py /path/to/reference
The resulting project has no runtime dependency on the reference checkout.
"""
from pathlib import Path
import base64, collections, json, re, shutil, struct, sys
src=Path(sys.argv[1]).resolve(); dst=Path(__file__).resolve().parents[1]
assets=dst/'Assets'; data=dst/'Data'; data.mkdir(parents=True,exist_ok=True)
def sections(text):
    headers=list(re.finditer(r'^\[([^\n]+)\]\s*$',text,re.M))
    return [(m[1],text[m.end():headers[i+1].start() if i+1<len(headers) else len(text)]) for i,m in enumerate(headers)]
def attrs(header): return dict(re.findall(r'(\w+)="([^"]*)"',header))
def resources(text):
    return {attrs(h)['id']:attrs(h)['path'] for h,b in sections(text) if h.startswith('ext_resource') and 'path=' in h}
def value(v,res):
    v=v.strip()
    if v.startswith('ExtResource('):return res.get(v.split('"')[1],'')
    if v.startswith('Vector2'):return [float(x) for x in re.findall(r'-?\d+(?:\.\d+)?',v[v.index('(')+1:])]
    if v.startswith('PackedVector2Array'):return [float(x) for x in re.findall(r'-?\d+(?:\.\d+)?',v[v.index('(')+1:])]
    if v.startswith('Color('):return [float(x) for x in re.findall(r'-?\d+(?:\.\d+)?',v[v.index('(')+1:])]
    if v in ('true','false'):return v=='true'
    try:return json.loads(v)
    except:return v

def props(body,res):
    return {m[1]:value(m[2],res) for m in re.finditer(r'^([\w/]+) = (.*)$',body,re.M)}
cache={}
def prefab(path):
    if not path.startswith('res://') or not path.endswith('.tscn'):return {}
    if path in cache:return cache[path].copy()
    f=src/path[6:]
    if not f.exists():return {}
    t=f.read_text();r=resources(t);nodes=[(h,b) for h,b in sections(t) if h.startswith('node ')]
    if not nodes:return {}
    h,b=nodes[0];d={}
    inh=re.search(r'instance=ExtResource\("([^"]+)',h)
    if inh:d.update(prefab(r[inh[1]]))
    d.update(props(b,r));d.pop('script',None)
    cache[path]=d
    return d.copy()

# Import tileset layout: atlas coordinates + collision and scene-tile identity.
t= (src/'Scenes/Parts/Tiles.tscn').read_text();r=resources(t);subs={};mapping={}
for h,b in sections(t):
    a=attrs(h)
    if h.startswith('sub_resource'):
        if a.get('type')=='TileSetAtlasSource':
            cells={}
            for m in re.finditer(r'^(\d+):(\d+)/0 =',b,re.M):
                x,y=map(int,m.groups());prefix=f'{x}:{y}/0/'
                solid=bool(re.search(r'^'+re.escape(prefix)+r'physics_layer_0/polygon_\d+/points',b,re.M))
                cells[f'{x},{y}']={'solid':solid}
            tex=re.search(r'^texture = ExtResource\("([^"]+)',b,re.M)
            subs[a['id']]={'type':'atlas','texture':r.get(tex[1],'') if tex else '', 'cells':cells}
        elif a.get('type')=='TileSetScenesCollectionSource':
            entries={}
            for m in re.finditer(r'^scenes/(\d+)/scene = ExtResource\("([^"]+)',b,re.M):entries[m[1]]=r[m[2]]
            subs[a['id']]={'type':'scenes','scenes':entries}
        elif a.get('type')=='TileSet':
            for m in re.finditer(r'^sources/(\d+) = SubResource\("([^"]+)',b,re.M):mapping[m[1]]=m[2]
atlas={k:subs[v] for k,v in mapping.items() if v in subs}
(data/'atlas.json').write_text(json.dumps(atlas,ensure_ascii=False,separators=(',',':')))

# Flatten declarative scene inheritance and parent transforms into plain data.
scene_cache={}
def scene_nodes(path):
    path=Path(path)
    if path in scene_cache:return {k:v.copy() for k,v in scene_cache[path].items()}
    text=path.read_text();r=resources(text);ns=[(h,b) for h,b in sections(text) if h.startswith('node ')]
    result={}
    for i,(h,b) in enumerate(ns):
        a=attrs(h);parent=a.get('parent','.');key='.' if i==0 else (a['name'] if parent=='.' else parent+'/'+a['name'])
        ins=re.search(r'instance=ExtResource\("([^\"]+)',h)
        ref=r.get(ins[1],'') if ins else ''
        if i==0 and (ref.startswith('res://Scenes/Levels/') or ref.startswith('res://') and Path(ref).stem.endswith('Visual')):
            result=scene_nodes(src/ref[6:])
        old=result.get(key,{}).copy()
        if ins:
            old.update(prefab(ref));old['_ref']=ref
        old.update(props(b,r));old['_name']=a['name'];old['_parent']=parent
        result[key]=old
    scene_cache[path]=result
    return {k:v.copy() for k,v in result.items()}
def absolute_pos(key,nodes):
    node=nodes[key];p=node.get('position',[0,0]);parent=node.get('_parent','.')
    if key!='.' and parent!='.' and parent in nodes:
        q=absolute_pos(parent,nodes);return [p[0]+q[0],p[1]+q[1]]
    return p

def castle_visual_cells(ref, offset, depth=0):
    if depth>4 or not ref.startswith('res://'):return []
    ns=scene_nodes(src/ref[6:]);result=[]
    # BG tile layers declared behind their parent must precede castle masonry.
    for key,n in sorted(ns.items(),key=lambda pair:(pair[1].get('z_index',0),not pair[1].get('show_behind_parent',False))):
        if n.get('visible') is False or 'SPCastle' in key:continue
        local=absolute_pos(key,ns);p=[local[0]+offset[0],local[1]+offset[1]]
        if 'tile_map_data' in n and n['_name']!='Overlay':
            raw=base64.b64decode(re.search(r'PackedByteArray\("([^"]+)',n['tile_map_data'])[1])
            for i in range(2,len(raw),12):
                x,y,s,ax,ay,alt=struct.unpack_from('<hhHHHH',raw,i)
                result.append([x+p[0]/16,y+p[1]/16,-1,ax,ay,alt])
        child=n.get('_ref','')
        if key!='.' and child and Path(child).stem.endswith('Visual'):
            result+=castle_visual_cells(child,p,depth+1)
    return result

# Stable room IDs and uid links, including bonus rooms and castle sections.
files=sorted((src/'Scenes/Levels/SMB1').rglob('*.tscn'))
files=[p for p in files if 'YouVSBoo' not in str(p) and 'World-1' not in str(p)]
files += [src/'Scenes/Levels/UndergroundExit.tscn',src/'Scenes/Levels/UnderwaterExit.tscn']
uids={re.search(r'uid="([^"]+)',p.read_text())[1]:p.stem for p in files}
rooms=[];kinds=collections.Counter()
for path in files:
    text=path.read_text();r=resources(text);nodes=[(h,b) for h,b in sections(text) if h.startswith('node ')]
    root=props(nodes[0][1],r);rid=path.stem
    if path.parent.name=='CoinHeavens':rid='cloud-'+rid
    uid=re.search(r'uid="([^"]+)',text)[1];uids[uid]=rid
# Second pass resolves every uid consistently.
for path in files:
    text=path.read_text();ns=scene_nodes(path)
    root=ns['.'];rid=uids[re.search(r'uid="([^"]+)',text)[1]]
    room={'id':rid,'theme':root.get('theme','Overworld'),'time':root.get('time_limit',400),'tiles':[],'deco':[],'entities':[],'spawn':[-208,0]}
    for key,node in list(ns.items())[1:]:
        name=node['_name'];parent=node['_parent']
        if any(v in parent or v in name for v in ('Challenge','BooRace','RedCoins','SecondQuest')):continue
        p={k:v for k,v in node.items() if not k.startswith('_')};pos=absolute_pos(key,ns);p.pop('position',None)
        ref=node.get('_ref','');kind=Path(ref).stem if ref else name
        if 'tile_map_data' in p:
            raw=base64.b64decode(re.search(r'PackedByteArray\("([^"]+)',p['tile_map_data'])[1])
            cells=[list(struct.unpack_from('<hhHHHH',raw,i)) for i in range(2,len(raw),12)]
            target=room['deco'] if 'Deco' in name else room['tiles']
            for x,y,s,ax,ay,alt in cells:
                if target is room['tiles'] and atlas.get(str(s),{}).get('type')=='scenes':
                    resource=atlas[str(s)]['scenes'].get(str(alt),'')
                    sk=Path(resource).stem;sp=prefab(resource)
                    room['entities'].append({'kind':sk,'name':f'tile-{x}-{y}','x':x*16+8,'y':y*16+8,'p':sp})
                    kinds[sk]+=1
                else:target.append([x+pos[0]/16,y+pos[1]/16,s,ax,ay,alt])
            continue
        if name in ('Player','Player2'):
            room['spawn']=pos;continue
        if not ref and kind not in ('PickAPathPoint','PickAPathTeleport','CoinHeavenWarpPoint','CameraRightLimit','WarpZoneCameraLimit','HardCameraRightLimit'):continue
        if kind in ('Tiles','DecoTiles','LevelBG','DropShadowRenderer','CheckpointFlag'):continue
        defaults=prefab(ref);defaults.update(p);defaults.pop('script',None)
        if kind in ('EndSmallCastle','EndFinalCastle','StartCastle','LargeStartCastle'):
            room['deco']+=castle_visual_cells(ref,pos)
        if kind in ('PickAPathPoint','PickAPathTeleport'):
            defaults['_maze_group']=key.split('/')[0]
            defaults['_path']=key
        for k,v in list(defaults.items()):
            if isinstance(v,str) and v.startswith('uid://'):defaults[k]=uids.get(v,v)
            if isinstance(v,str) and v.startswith('res://Scenes/Levels/SMB1/'):defaults[k]=Path(v).stem
        room['entities'].append({'kind':kind,'name':name,'x':pos[0],'y':pos[1],'p':defaults});kinds[kind]+=1
    room['width']=max([256]+[c[0]*16+16 for c in room['tiles']]+[e['x']+64 for e in room['entities']])
    rooms.append(room)
(data/'rooms.json').write_text(json.dumps(rooms,ensure_ascii=False,separators=(',',':')))
(data/'entity-catalog.json').write_text(json.dumps(dict(kinds),indent=2))
if '--data-only' in sys.argv:
    print(f'Imported {len(rooms)} rooms; empty rooms: {[r["id"] for r in rooms if not r["tiles"]]}')
    sys.exit(0)
# These are data files and media only. No gd/tscn/tres sources are copied.
for folder in ('Assets/Sprites','Assets/Audio','Resources/AssetRipper/Sprites','Resources/AssetRipper/Palettes'):
    for f in (src/folder).rglob('*'):
        if f.is_file() and f.suffix.lower() in ('.png','.json','.wav','.mp3','.ogg'):
            target=dst/('Assets/Recipe/'+str(f.relative_to(src/'Resources/AssetRipper')) if folder.startswith('Resources') else str(f.relative_to(src)))
            target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,target)
shutil.copyfile(src/'Study/inputs/smb1.chr',assets/'smb1.chr')
print(f'Imported {len(rooms)} rooms, {len(kinds)} entity kinds, {sum(len(r["tiles"]) for r in rooms)} map cells; no game code copied.')
