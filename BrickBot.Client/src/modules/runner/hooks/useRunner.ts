import { useEffect } from 'react';
import { eventBus } from '@/shared/services/eventBus';
import { useProfileStore } from '@/modules/profile';
import { scriptService } from '@/modules/script';
import { useRunnerStore } from '../store/runnerStore';
import { captureService } from '../services/captureService';
import { runnerService } from '../services/runnerService';
import type { LogEntry, RunnerState } from '../types';

export function useRunner() {
  const store = useRunnerStore();
  const activeProfileId = useProfileStore((s) => s.activeProfileId);

  useEffect(() => {
    const offState = eventBus.onModule('RUNNER', 'STATUS_CHANGED', (payload) => {
      store.setState(payload as RunnerState);
    });
    const offLog = eventBus.onModule('RUNNER', 'LOG', (payload) => {
      store.appendLog(payload as LogEntry);
    });
    runnerService.getState().then(store.setState).catch(() => undefined);
    return () => { offState(); offLog(); };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Refresh the list of main scripts whenever the active profile changes.
  useEffect(() => {
    if (!activeProfileId) {
      store.setAvailableMains([]);
      return;
    }
    let cancelled = false;
    scriptService.list(activeProfileId).then((res) => {
      if (cancelled) return;
      store.setAvailableMains(res.files.filter((f) => f.kind === 'main').map((f) => f.name));
    }).catch(() => undefined);
    return () => { cancelled = true; };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeProfileId]);

  const refreshWindows = async () => {
    const list = await captureService.listWindows();
    store.setWindows(list);
  };

  const start = async () => {
    if (!store.selectedWindow) throw new Error('Pick a window first.');
    if (!activeProfileId) throw new Error('No active profile.');
    if (!store.selectedMain) throw new Error('Pick a main script first.');
    const next = await runnerService.start({
      windowHandle: store.selectedWindow.handle,
      profileId: activeProfileId,
      mainName: store.selectedMain,
      templateRoot: store.templateRoot,
    });
    store.setState(next);
  };

  const stop = async () => {
    const next = await runnerService.stop();
    store.setState(next);
  };

  return { ...store, activeProfileId, refreshWindows, start, stop };
}
