#!/usr/bin/env python3
"""
Moves bones of a .glb under another bone without moving them in the world (docs/coop/PLAYER_MODELS.md, "Pitfalls").

    python reparent_glb_bones.py <in.glb> <out.glb> <bone>=<new parent> [<bone>=<new parent> ...]

The names are the node names in the file ("Bone_019=mixamorig:LeftHand"). The bone keeps its place and its turn in the world (its local translation and rotation are worked out again for the new parent), so
the bind pose, the inverse bind matrices of the skin and the animations of every other bone are untouched. Only the node tree changes: the bone is taken out of the children of its old parent and put among the
children of the new one. The model's file is not otherwise touched (the binary chunk is copied as it is). Plain Python 3, no packages.

Why: some exported rigs hang the helper bone of the last two fingers (for example Bone_019, Bone_024) off the forearm, next to the hand, so those finger chains stay behind when the wrist turns and a hand that grips
at the middle of its four knuckle bones would find two of them in the wrong place.
"""
import json
import struct
import sys


def read_glb(path):
    with open(path, 'rb') as f:
        data = f.read()
    magic, version, length = struct.unpack_from('<4sII', data, 0)
    if magic != b'glTF' or version != 2:
        raise SystemExit(f'{path} is not a glTF 2 binary file')
    offset = 12
    json_length, json_type = struct.unpack_from('<I4s', data, offset)
    if json_type != b'JSON':
        raise SystemExit('the first chunk is not JSON')
    document = json.loads(data[offset + 8: offset + 8 + json_length].decode('utf-8'))
    rest = data[offset + 8 + json_length:]  # the binary chunk(s), copied as they are
    return document, rest


def write_glb(path, document, rest):
    text = json.dumps(document, separators=(',', ':')).encode('utf-8')
    text += b' ' * ((4 - len(text) % 4) % 4)
    total = 12 + 8 + len(text) + len(rest)
    with open(path, 'wb') as f:
        f.write(struct.pack('<4sII', b'glTF', 2, total))
        f.write(struct.pack('<I4s', len(text), b'JSON'))
        f.write(text)
        f.write(rest)


def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def q_conj(q):
    return (-q[0], -q[1], -q[2], q[3])


def q_rotate(q, v):
    r = q_mul(q_mul(q, (v[0], v[1], v[2], 0.0)), q_conj(q))
    return (r[0], r[1], r[2])


def world_transforms(nodes, parent):
    """The world position and rotation of every node (the scale of these rigs is 1 to within rounding)."""
    count = len(nodes)
    position = [None] * count
    rotation = [None] * count

    def resolve(i):
        if position[i] is not None:
            return
        node = nodes[i]
        t = tuple(node.get('translation', (0.0, 0.0, 0.0)))
        r = tuple(node.get('rotation', (0.0, 0.0, 0.0, 1.0)))
        p = parent[i]
        if p < 0:
            position[i], rotation[i] = t, r
            return
        resolve(p)
        moved = q_rotate(rotation[p], t)
        position[i] = (position[p][0] + moved[0], position[p][1] + moved[1], position[p][2] + moved[2])
        rotation[i] = q_mul(rotation[p], r)

    for i in range(count):
        resolve(i)
    return position, rotation


def main():
    if len(sys.argv) < 4:
        raise SystemExit(__doc__)
    source, target = sys.argv[1], sys.argv[2]
    moves = []
    for argument in sys.argv[3:]:
        child, _, new_parent = argument.partition('=')
        if not child or not new_parent:
            raise SystemExit(f'"{argument}" is not <bone>=<new parent>')
        moves.append((child, new_parent))

    document, rest = read_glb(source)
    nodes = document['nodes']
    names = [node.get('name', '') for node in nodes]
    parent = [-1] * len(nodes)
    for i, node in enumerate(nodes):
        for c in node.get('children', []):
            parent[c] = i

    def find(name):
        matches = [i for i, n in enumerate(names) if n == name]
        if len(matches) != 1:
            raise SystemExit(f'"{name}" matches {len(matches)} nodes (exactly one is needed)')
        return matches[0]

    position, rotation = world_transforms(nodes, parent)
    for child_name, parent_name in moves:
        child, new_parent = find(child_name), find(parent_name)
        # not under itself
        walk = new_parent
        while walk >= 0:
            if walk == child:
                raise SystemExit(f'"{parent_name}" is below "{child_name}": that would make a loop')
            walk = parent[walk]
        old_parent = parent[child]
        inverse = q_conj(rotation[new_parent])
        offset = q_rotate(inverse, (position[child][0] - position[new_parent][0], position[child][1] - position[new_parent][1], position[child][2] - position[new_parent][2]))
        local_rotation = q_mul(inverse, rotation[child])
        if local_rotation[3] < 0:
            local_rotation = tuple(-c for c in local_rotation)
        if old_parent >= 0:
            nodes[old_parent]['children'].remove(child)
            if not nodes[old_parent]['children']:
                del nodes[old_parent]['children']
        nodes[new_parent].setdefault('children', []).append(child)
        parent[child] = new_parent
        node = nodes[child]
        node.pop('matrix', None)
        node.pop('scale', None)
        node['translation'] = [float(c) for c in offset]
        node['rotation'] = [float(c) for c in local_rotation]
        print(f'{child_name} (node {child}): from {names[old_parent] if old_parent >= 0 else "the scene"} to {parent_name}, local translation {tuple(round(c, 5) for c in offset)}')

    write_glb(target, document, rest)
    # the world places of every node must be what they were
    after_position, after_rotation = world_transforms(nodes, parent)
    worst = max(max(abs(a - b) for a, b in zip(position[i], after_position[i])) for i in range(len(nodes)))
    print(f'wrote {target}; the largest change of a node\'s place in the world: {worst:.2e}')


if __name__ == '__main__':
    main()
