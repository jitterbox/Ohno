import { parentPort } from 'node:worker_threads';
import { dispatch, type WorkerRequest } from './messages';

if (!parentPort) {
  throw new Error('ohno-ts-worker must run as a worker thread');
}

parentPort.on('message', (message: WorkerRequest) => {
  parentPort!.postMessage(dispatch(message));
});
