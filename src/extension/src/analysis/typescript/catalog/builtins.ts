export type SizeKind = 'constant' | 'receiver' | 'logReceiver' | 'nLogN';
export type CostKind = 'exact' | 'amortized' | 'expected';

export interface BuiltinEntry {
  time: SizeKind;
  space: SizeKind;
  kind: CostKind;
  loop?: boolean;
}

const entries = new Map<string, BuiltinEntry>();

function add(
  type: string,
  member: string,
  arity: number,
  time: SizeKind,
  kind: CostKind = 'exact',
  space: SizeKind = 'constant',
  loop = false,
): void {
  entries.set(`${type}#${member}#${arity}`, {
    time, space, kind, loop,
  });
}

function addAll(
  type: string,
  member: string,
  arities: number[],
  time: SizeKind,
  kind: CostKind = 'exact',
  space: SizeKind = 'constant',
  loop = false,
): void {
  for (const arity of arities) {
    add(type, member, arity, time, kind, space, loop);
  }
}

add('Array', 'length', 0, 'constant');
addAll('Array', 'push', [1, 2, 3], 'constant', 'amortized');
add('Array', 'pop', 0, 'constant');
addAll('Array', 'unshift', [1, 2, 3], 'receiver');
add('Array', 'shift', 0, 'receiver');
addAll('Array', 'splice', [1, 2, 3], 'receiver');
addAll('Array', 'sort', [0, 1], 'nLogN');
addAll('Array', 'toSorted', [0, 1], 'nLogN');
addAll('Array', 'map', [1], 'receiver', 'exact', 'receiver', true);
addAll('Array', 'filter', [1], 'receiver', 'exact', 'receiver', true);
addAll('Array', 'forEach', [1], 'receiver', 'exact', 'constant', true);
addAll('Array', 'reduce', [1, 2], 'receiver', 'exact', 'constant', true);
addAll('Array', 'flatMap', [1], 'receiver', 'exact', 'receiver', true);
addAll('Array', 'indexOf', [1, 2], 'receiver');
addAll('Array', 'includes', [1, 2], 'receiver');
addAll('Array', 'find', [1], 'receiver', 'exact', 'constant', true);
addAll('Array', 'some', [1], 'receiver', 'exact', 'constant', true);
addAll('Array', 'every', [1], 'receiver', 'exact', 'constant', true);
addAll('Array', 'at', [1], 'constant');
addAll('Array', 'concat', [1, 2, 3], 'receiver', 'exact', 'receiver');
addAll('Array', 'slice', [0, 1, 2], 'receiver', 'exact', 'receiver');
addAll('Array', 'join', [0, 1], 'receiver', 'exact', 'receiver');
addAll('Array', 'flat', [0, 1], 'receiver', 'exact', 'receiver');
addAll('Array', 'reverse', [0], 'receiver');
addAll('Array', 'toReversed', [0], 'receiver', 'exact', 'receiver');

add('Map', 'size', 0, 'constant');
add('Map', 'get', 1, 'constant', 'expected');
add('Map', 'has', 1, 'constant', 'expected');
add('Map', 'set', 2, 'constant', 'expected');
add('Map', 'delete', 1, 'constant', 'expected');
add('Map', 'clear', 0, 'receiver');
add('Map', 'forEach', 1, 'receiver', 'exact', 'constant', true);
add('Set', 'size', 0, 'constant');
add('Set', 'has', 1, 'constant', 'expected');
add('Set', 'add', 1, 'constant', 'expected');
add('Set', 'delete', 1, 'constant', 'expected');
add('Set', 'clear', 0, 'receiver');
add('Set', 'forEach', 1, 'receiver', 'exact', 'constant', true);

add('String', 'length', 0, 'constant');
addAll('String', 'includes', [1, 2], 'receiver');
addAll('String', 'indexOf', [1, 2], 'receiver');
addAll('String', 'lastIndexOf', [1, 2], 'receiver');
addAll('String', 'split', [1, 2], 'receiver', 'exact', 'receiver');
addAll('String', 'slice', [1, 2], 'receiver', 'exact', 'receiver');
addAll('String', 'substring', [1, 2], 'receiver', 'exact', 'receiver');
addAll('String', 'charAt', [1], 'constant');
addAll('String', 'startsWith', [1, 2], 'receiver');
addAll('String', 'endsWith', [1, 2], 'receiver');
addAll('String', 'replace', [2], 'receiver', 'exact', 'receiver');
addAll('String', 'match', [1], 'receiver', 'exact', 'receiver');

addAll('Object', 'keys', [1], 'receiver', 'exact', 'receiver');
addAll('Object', 'values', [1], 'receiver', 'exact', 'receiver');
addAll('Object', 'entries', [1], 'receiver', 'exact', 'receiver');
addAll('JSON', 'parse', [1], 'receiver', 'exact', 'receiver');
addAll('JSON', 'stringify', [1, 2, 3], 'receiver', 'exact', 'receiver');

export function lookupBuiltin(
  typeName: string,
  member: string,
  arity: number,
): BuiltinEntry | undefined {
  return entries.get(`${typeName}#${member}#${arity}`)
    ?? entries.get(`${typeName}#${member}#${Math.min(arity, 3)}`);
}

export function builtinTypeName(name: string): string | undefined {
  switch (name) {
    case 'Array':
    case 'ReadonlyArray':
      return 'Array';
    case 'Map':
    case 'ReadonlyMap':
      return 'Map';
    case 'Set':
    case 'ReadonlySet':
      return 'Set';
    case 'String':
    case 'string':
      return 'String';
    case 'Object':
      return 'Object';
    case 'JSON':
      return 'JSON';
    default:
      return undefined;
  }
}
