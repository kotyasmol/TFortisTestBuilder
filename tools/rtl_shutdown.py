"""Upgrade only board shutdown actions; retain production checks and K5 collection."""
from copy import deepcopy

OUTPUTS = {
    'SW': [2301, 2302, 2303, 2304, 2305, 2307, 2308, 2385],
    'POE': [2403, 2492, 2487, 2489, 2511],
}

def walk(graph):
    for node in graph['nodes']:
        yield node
        for key in ('body', 'bodyGraph'):
            if key in node:
                yield from walk(node[key])

def apply_shutdown(graph, board):
    stage = next(n for n in graph['nodes'] if n.get('name', '').startswith('Снятие питания'))
    body = stage['bodyGraph']['nodes'][1]['body']
    existing = {next(n['address'] for n in walk(s['bodyGraph']) if n['type'] == 'Write Register'): s
                for s in body['nodes'] if s['type'] == 'Subtest'}
    template = next(iter(existing.values()))
    actions = []
    for index, address in enumerate(OUTPUTS[board], 1):
        action = deepcopy(existing.get(address, template))
        action['id'] = str(index)
        action['name'] = f'Отключить {address}'
        action['bodyGraph']['name'] = action['name']
        action['x'], action['y'] = 352 + (index-1)*544, 80
        for node in walk(action['bodyGraph']):
            if node['type'] == 'Write Register':
                node.update(address=address, value=0, verifyWrite=True)
        actions.append(action)
    start, end = body['nodes'][0], body['nodes'][-1]
    end['id'] = str(len(actions)+1)
    end['x'], end['y'] = 352 + len(actions)*544, 80
    body['nodes'] = [start] + actions + [end]
    body['connections'] = [dict(sourceNodeId=start['id'], sourceConnector='Выход',
        targetNodeId=actions[0]['id'], targetConnector='In')]
    for action, following in zip(actions, actions[1:]+[end]):
        for outcome in ('Success', 'Error'):
            body['connections'].append(dict(sourceNodeId=action['id'], sourceConnector=outcome,
                targetNodeId=following['id'], targetConnector='Вход' if following is end else 'In'))
    stage['runOnFailure'] = True
    stage['description'] = ('Снять питание и сбросить выходы. Каждая команда проверяется чтением регистра; '
        'до трёх попыток. Ошибка одной команды не отменяет остальные. '
        'Контроль регистров не заменяет измерение напряжения на плате.')
