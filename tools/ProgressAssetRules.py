"""Decode literal PlayerData dependencies in a serialized PlayMaker item-source FSM."""

# These describe local gameplay/control, not persistent acquisition or quest progress.
LOCAL_CONTEXT = {'permadeathMode','geo','TempGeoStore','dicePilgrimBank','CrawbellCurrency',
                 'profileID','playTime','disablePause','isInvincible','atBench'}
# This legacy FSM query has no separate serialized bool in shipped PlayerData.
# Preserve its associated stored array; never invent a bool in the output save.
STORED_ALIASES = {'mossBerryValueListSet': {'mossBerryValueList'}}

def fsm_dependencies(tree, fields, properties):
    fsm = tree.get('fsm', {})
    variables = {x['name']:x.get('value','') for x in fsm.get('variables',{}).get('stringVariables',[])}
    result = set()
    for state in fsm.get('states', []):
        data = state.get('actionData', {})
        names = data.get('actionNames', [])
        starts = data.get('actionStartIndex', [])
        for index, action in enumerate(names):
            if 'PlayerData' not in action: continue
            end = starts[index+1] if index+1<len(starts) else len(data['paramName'])
            for param in range(starts[index], end):
                if data['paramName'][param] not in ('boolName','intName','floatName','stringName'): continue
                if data['paramDataType'][param]!=18: continue # PlayMaker FsmString
                value = data['fsmStringParams'][data['paramDataPos'][param]]
                field = variables.get(value['name'],'') if value.get('useVariable') else value.get('value','')
                if not field or field in LOCAL_CONTEXT: continue
                if field in fields: result.add(field)
                elif field in properties: result.update(properties[field])
                elif field in STORED_ALIASES: result.update(STORED_ALIASES[field])
                else: raise ValueError(f'Unknown acquisition PlayerData field: {field}')
    return result-LOCAL_CONTEXT
