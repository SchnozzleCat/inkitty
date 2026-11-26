import { dotnet } from './_framework/dotnet.js'
import { mountStorage } from './inkitty.js'

const status = document.getElementById('splash-status');
const setStatus = text => { if (status) status.textContent = text; };

try {
    const runtime = await dotnet
        .withDiagnosticTracing(false)
        .withApplicationArgumentsFromQuery()
        .create();

    // Projects and settings live in IndexedDB (Emscripten IDBFS), loaded before Main runs.
    setStatus('Loading your projects…');
    await mountStorage(runtime.Module.FS, '/inkitty');

    setStatus('Starting…');
    await runtime.runMain(runtime.getConfig().mainAssemblyName, [globalThis.location.href]);
} catch (error) {
    console.error(error);
    setStatus(`Inkitty couldn't start: ${error?.message ?? error}`);
}
