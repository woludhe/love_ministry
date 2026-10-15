// Uploads one audio file to the Audio Library (POST /Admin/UploadAudio) and reports progress.
// type: the folder it goes in (sermons, music, or a new type name). Resolves with
// { success, name, type, typeLabel, url, size } or { success: false, message }.
function uploadAudioFile(file, type, onProgress) {
    return new Promise(resolve => {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
        const fd = new FormData();
        fd.append('file', file);
        fd.append('type', type);
        fd.append('__RequestVerificationToken', token);

        const xhr = new XMLHttpRequest();
        xhr.open('POST', '/Admin/UploadAudio');
        xhr.upload.onprogress = e => { if (e.lengthComputable && onProgress) onProgress(e.loaded / e.total); };
        xhr.onload = () => {
            if (xhr.status === 413) return resolve({ success: false, message: `${file.name} is too large for the server.` });
            try { resolve(JSON.parse(xhr.responseText)); }
            catch { resolve({ success: false, message: `${file.name} couldn't be uploaded (error ${xhr.status}).` }); }
        };
        xhr.onerror = () => resolve({ success: false, message: `${file.name} couldn't be uploaded. Check your connection.` });
        xhr.send(fd);
    });
}

function formatFileSize(bytes) {
    return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
