// Browser services for Inkitty (imported by main.js, and by .NET via JSHost.ImportAsync("inkitty")).

let fs = null;
let root = null;
let syncing = false;
let pending = false;
let unsavedChangesCheck = null;

/** Mounts IndexedDB at `path` and loads its contents into memory. */
export async function mountStorage(FS, path) {
    fs = FS;
    root = path;
    try {
        FS.mkdir(path);
    } catch {
        // already exists
    }

    // autoPersist flushes after each change, so saves survive a reload without explicit syncs.
    FS.mount(FS.filesystems.IDBFS, { autoPersist: true }, path);
    await new Promise((resolve, reject) => FS.syncfs(true, error => error ? reject(error) : resolve()));
    if (navigator.storage?.persist) {
        navigator.storage.persist().catch(() => { });
    }
}

/** Writes pending changes to IndexedDB (coalesced). */
export function persist() {
    if (!fs) return;
    if (syncing) {
        pending = true;
        return;
    }

    syncing = true;
    fs.syncfs(false, error => {
        syncing = false;
        if (error) console.warn('Inkitty: saving to browser storage failed', error);
        if (pending) {
            pending = false;
            persist();
        }
    });
}

export function setTitle(title) {
    document.title = title;
}

export function setUnsavedChangesCheck(check) {
    unsavedChangesCheck = check;
}

export function download(fileName, bytes, mimeType) {
    const blob = new Blob([bytes], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

window.addEventListener('beforeunload', event => {
    persist();
    let unsaved = false;
    try {
        unsaved = unsavedChangesCheck?.() === true;
    } catch {
        // runtime gone
    }

    if (unsaved) {
        event.preventDefault();
        event.returnValue = '';
    }
});

document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') persist();
});
