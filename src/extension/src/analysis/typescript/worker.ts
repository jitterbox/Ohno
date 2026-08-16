import { parentPort, workerData } from 'node:worker_threads';
import { bindAbort, dispatch, type WorkerRequest } from './messages';

if (!parentPort) {
  throw new Error('ohno-ts-worker must run as a worker thread');
}

if (workerData?.abortGen instanceof SharedArrayBuffer) {
  bindAbort(workerData.abortGen);
}

parentPort.on('message', (message: WorkerRequest) => {
  parentPort!.postMessage(dispatch(message));
});
