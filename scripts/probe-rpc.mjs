#!/usr/bin/env node
import { spawn } from 'node:child_process';
import {
  createMessageConnection,
  StreamMessageReader,
  StreamMessageWriter,
} from 'vscode-jsonrpc/node';

const server = process.argv[2];
const child = spawn(server, [], { stdio: ['pipe', 'pipe', 'pipe'] });
child.stderr.on('data', (c) => process.stderr.write(c));
const connection = createMessageConnection(
  new StreamMessageReader(child.stdout),
  new StreamMessageWriter(child.stdin),
);
connection.listen();
const init = await connection.sendRequest('initialize');
console.log('initialize', JSON.stringify(init));
const result = await connection.sendRequest('ohno/analyze', {
  uri: 'file:///tmp/A.cs',
  version: 1,
  tier: 'fast',
  text: 'public static class S { public static int G(int[] n) => n[0]; }',
});
console.log('analyze keys', Object.keys(result));
console.log('functions type', Array.isArray(result.functions), Array.isArray(result.Functions));
console.log(JSON.stringify(result, null, 2).slice(0, 1500));
connection.dispose();
child.kill();
