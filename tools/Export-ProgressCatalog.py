"""Read shipped Unity assets without starting Unity. Requires UnityPy 1.25.4.

Emit quest counter dependencies, pickup evidence and exact personal-source exclusions.
The runtime follows host persistent state by default; this is an exception catalog,
not another incomplete allow-list of shared pickups.
"""
import argparse
import collections
import hashlib
import json
import struct
import sys
import warnings
from pathlib import Path
from ProgressAssetRules import fsm_dependencies

parser = argparse.ArgumentParser()
parser.add_argument('--game-root', default='D:/Game/Hollow Knight Silksong')
parser.add_argument('--output', default='assets/ProgressSyncCatalog.json')
parser.add_argument('--fields', default='build/player-data-fields.json')
parser.add_argument('--types', default='build/game-type-bases.json')
parser.add_argument('--scene-limit', type=int)
args = parser.parse_args()
sys.path.insert(0, str(Path('build/asset-tools').resolve()))
import UnityPy
UnityPy.config.FALLBACK_UNITY_VERSION = '6000.0.50f1'
warnings.simplefilter('ignore')
root = Path(args.game_root) / 'Hollow Knight Silksong_Data'
bundles = root / 'StreamingAssets/aa/StandaloneWindows64'
fields = {x['Name']: x['Type'] for x in json.loads(Path(args.fields).read_text(encoding='utf-8-sig'))}
bases = json.loads(Path(args.types).read_text(encoding='utf-8-sig'))
properties = json.loads(Path('build/player-data-properties.json').read_text(encoding='utf-8-sig'))
environment = UnityPy.load(str(bundles / '94696d22b6ed0a74097d1bd58feb4dce_monoscripts.bundle'))
classes = {o.path_id: o.read().m_ClassName for o in environment.objects if o.type.name == 'MonoScript'}
for file in sorted((bundles / 'dataassets_assets_assets').rglob('*.bundle')):
    environment.load_file(str(file))

def class_of(obj):
    return classes[obj.parse_monobehaviour_head().m_Script.m_PathID]

assets = {}
for obj in environment.objects:
    if obj.type.name == 'MonoBehaviour':
        tree = obj.read_typetree()
        if tree.get('m_Name'):
            assets[obj.path_id] = (class_of(obj), tree)

required = set()
def dependencies(value):
    result = set()
    if isinstance(value, dict):
        for key, child in value.items():
            if key in ('FieldName', 'BoolName', 'playerDataInt', 'orderListPd', 'playerDataBool') and child:
                if child in properties:
                    result.update(properties[child])
                    continue
                if child not in fields:
                    raise ValueError(f'Unknown PlayerData dependency: {child}')
                result.add(child)
            else:
                result.update(dependencies(child))
    elif isinstance(value, list):
        for child in value:
            result.update(dependencies(child))
    return result

def counter(ref):
    if not ref.get('m_PathID'):
        return {'Type': 'CompletionCount', 'Fields': []}
    kind, data = assets[ref['m_PathID']]
    found = dependencies(data)
    if kind == 'QuestTargetSteelQuestSpot': found.add('SteelQuestSpots')
    if kind == 'QuestTargetPlayerDataBools' and data.get('pdFieldTemplate'):
        found.update(name for name in fields if name.startswith(data['pdFieldTemplate']))
    storage = {
        'CollectableItem': 'Collectables', 'CollectableRelic': 'Relics',
        'EnemyJournalRecord': 'EnemyJournalKillData', 'QuestTargetJournalGroup': 'EnemyJournalKillData',
        'JournalQuestTarget': 'EnemyJournalKillData',
        'ToolItem': 'Tools', 'ToolCrest': 'ToolEquips',
        'QuestTargetPlayerDataInt': None, 'QuestTargetPlayerDataBools': None,
        'QuestTargetSteelQuestSpot': None, 'QuestTargetCurrency': None,
    }
    category = kind
    while category not in storage and category in bases:
        category = bases[category]
    if category not in storage:
        raise ValueError(f'Unreviewed quest counter kind: {kind} ({data["m_Name"]})')
    if storage[category]: found.add(storage[category])
    if kind == 'QuestTargetCurrency':
        if data['currencyType'] == 0: found.add('geo')
        elif data['currencyType'] == 1: found.add('ShellShards')
        else: raise ValueError('Unknown quest currency')
    required.update(found)
    return {'Type': kind, 'Asset': data['m_Name'], 'Fields': sorted(found)}

quests = {}
for kind, data in assets.values():
    if 'targets' not in data or 'targetCounter' not in data:
        continue
    found = dependencies({k: data[k] for k in ('playerDataTest', 'getTargetCondition') if k in data})
    targets = []
    for target in data['targets']:
        desc = counter(target['Counter'])
        desc['Count'] = target['Count']
        desc['Fields'] = sorted(set(desc['Fields']) | dependencies(target.get('AltTest')))
        found.update(desc['Fields']); targets.append(desc)
    if not targets:
        desc = counter(data['targetCounter']); desc['Count'] = data['targetCount']
        found.update(desc['Fields']); targets.append(desc)
    required.update(found)
    quests[data['m_Name']] = {'Fields': sorted(found), 'Targets': targets}

suffixes = ('_boss_defeated','_boss','_preload','_bellway','_mapper','_boss_golem',
            '_boss_golem_rest','_boss_beastfly','_additive','_pre','_caravan','_festival')
# These seven platform prefixes are copied from pinned SSMP PersonalPlaces.Things.
platforms = ('moss_crumble_plat','lava_crumble_plat','bone_plat_01_crumble',
             'bone_plat_02_crumble','bone_plat_crumble','crumble_plat_peak_','memory_ground_plat')
personal = {'persistentBools': set(), 'persistentInts': set()}
personal_fields = set()
money_assets = {key:data['m_Name'] for key,(_,data) in assets.items() if data['m_Name'].startswith('Rosary_Set_')}
source_probes = {**money_assets, **{key:data['m_Name'] for key,(_,data) in assets.items() if data['m_Name']=='Mossberry'}}
source_patterns = {key:struct.pack('<q',key) for key in source_probes}
fsm_sources = []
def derives(kind, base):
    while kind:
        if kind == base: return True
        kind = bases.get(kind)
    return False
pickups, persistent_count, scenes = [], 0, []
scene_files = sorted((bundles / 'scenes_scenes_scenes').rglob('*.bundle'))
if args.scene_limit: scene_files = scene_files[:args.scene_limit]
for number, file in enumerate(scene_files, 1):
    scene = file.stem
    for suffix in suffixes:
        if scene.endswith(suffix): scene = scene[:-len(suffix)]; break
    scene = scene.lower()
    env = UnityPy.load(str(file))
    objects = {o.path_id: o for o in env.objects}
    mono = {}
    for obj in objects.values():
        if obj.type.name == 'MonoBehaviour':
            head = obj.parse_monobehaviour_head()
            if head.m_Script.m_PathID: # Null-script components have no executable behavior.
                mono[obj.path_id] = (classes[head.m_Script.m_PathID], head.m_GameObject.m_PathID)
    names = {}
    def go_name(key):
        if key not in names: names[key] = objects[key].read().m_Name
        return names[key]
    def platform_owner(owner):
        seen = set()
        while owner and owner not in seen:
            seen.add(owner)
            if go_name(owner).startswith(platforms): return True
            go = objects[owner].read_typetree()
            parent = 0
            for component in go['m_Component']:
                ref = component['component']['m_PathID']
                if ref in objects and objects[ref].type.name in ('Transform','RectTransform'):
                    father = objects[ref].read_typetree()['m_Father']['m_PathID']
                    if father: parent = objects[father].read_typetree()['m_GameObject']['m_PathID']
                    break
            owner = parent
        return False
    persistent = {}
    persistent_owners = collections.defaultdict(list)
    for key, (kind, owner) in mono.items():
        if kind not in ('PersistentBoolItem', 'PersistentIntItem'): continue
        tree = objects[key].read_typetree(); data = tree['itemData']
        if tree.get('dontSave'): continue
        item_key = ((data.get('SceneName') or scene).lower(), data.get('ID') or go_name(owner))
        store = 'persistentBools' if kind == 'PersistentBoolItem' else 'persistentInts'
        persistent[key] = (store, item_key)
        persistent_owners[owner].append((store,item_key))
        persistent_count += 1
        if platform_owner(owner): personal[store].add(item_key)
    for key, (kind, owner) in mono.items():
        pickup = derives(kind, 'CollectableItemPickup')
        money = derives(kind, 'GeoControl') or derives(kind, 'RosaryCache')
        if not pickup and not money: continue
        tree = objects[key].read_typetree(); ptr = tree.get('persistent', {}).get('m_PathID')
        is_money = money
        item = None
        if pickup:
            item_ref = tree.get('item', {}).get('m_PathID')
            if item_ref:
                item = assets[item_ref][1]['m_Name']
                is_money = item.startswith('Rosary_Set_')
            flag = tree.get('playerDataBool')
            if flag:
                if flag not in fields: raise ValueError(f'Unknown pickup flag {flag}')
                if is_money: personal_fields.add(flag)
                else: required.add(flag)
            pickups.append({'Scene': scene, 'Object': go_name(owner), 'Item': item,
                            'PlayerDataFlag': flag or '', 'Record': list(persistent[ptr][1]) if ptr in persistent else None})
        if is_money and ptr in persistent:
            store, item_key = persistent[ptr]; personal[store].add(item_key)
    for key, (kind, owner) in mono.items():
        if kind != 'PlayMakerFSM': continue
        raw = objects[key].get_raw_data()
        matched = [item for item,pattern in source_patterns.items() if pattern in raw]
        if not matched: continue
        tree = objects[key].read_typetree()
        actions = {action.rsplit('.',1)[-1] for state in tree.get('fsm',{}).get('states',[])
                   for action in state.get('actionData',{}).get('actionNames',[])}
        gets_item = bool(actions & {'CollectableItemCollect','SavedItemGet'})
        records = persistent_owners.get(owner,[])
        source_fields = fsm_dependencies(tree,fields,properties)
        # A quest/NPC can reward rosaries without making its story or counters personal.
        # Only the typed currency pickup path above may classify a personal PD flag.
        if not all(item in money_assets for item in matched): required.update(source_fields)
        fsm_sources.append({'Scene':scene,'Object':go_name(owner),'Items':[source_probes[x] for x in matched],
                            'CollectionAction':gets_item,'Fields':sorted(source_fields),'Records':[[store,*pair] for store,pair in records]})
        referenced_items = [item for item,(asset_kind,_) in assets.items()
                            if derives(asset_kind,'SavedItem') and struct.pack('<q',item) in raw]
        if gets_item and referenced_items and all(item in money_assets for item in referenced_items):
            for store,item_key in records: personal[store].add(item_key)
    scenes.append({'File': file.relative_to(bundles).as_posix(), 'Sha256': hashlib.sha256(file.read_bytes()).hexdigest()})
    if number % 25 == 0 or number == len(scene_files):
        print(f'Scanned {number}/{len(scene_files)} scenes; pickups={len(pickups)}, persistent={persistent_count}', flush=True)

output = {
    'Complete': args.scene_limit is None,
    'GameVersion': '1.0.30000', 'UnityVersion': '6000.0.50f1-uum-100966-branch1',
    'AssemblySha256': hashlib.sha256((root/'Managed/Assembly-CSharp.dll').read_bytes()).hexdigest().upper(),
    'UnityPy': UnityPy.__version__, 'SceneCount': len(scenes), 'PersistentCount': persistent_count,
    'PlayerDataFields': sorted(required - {'geo','permadeathMode'}),
    'PersonalPlayerDataFields': sorted(personal_fields),
    'PersonalRecords': {name: [list(key) for key in sorted(keys)] for name, keys in personal.items()},
    'Quests': dict(sorted(quests.items())), 'Pickups': pickups, 'FsmSources': fsm_sources, 'SceneEvidence': scenes,
}
destination = Path(args.output)
temporary = destination.with_suffix(destination.suffix+'.tmp')
temporary.write_text(json.dumps(output, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
temporary.replace(destination)
print(json.dumps({'quests':len(quests),'fields':len(output['PlayerDataFields']),
                  'personal':{k:len(v)for k,v in personal.items()},'pickups':len(pickups)}, ensure_ascii=False), flush=True)
