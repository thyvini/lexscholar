// Keep archive bytes in browser-managed Blob storage, not in WASM interop.
window.lexScholarLibraryExport = (() => {
    let downloadUrl;
    const release = () => {
        if (downloadUrl) URL.revokeObjectURL(downloadUrl);
        downloadUrl = undefined;
    };
    window.addEventListener("pagehide", release);
    return {
        download: async () => {
            try {
                const options = { credentials: "same-origin", redirect: "error", cache: "no-store" };
                const tokenResponse = await fetch("/api/antiforgery", options);
                if (!tokenResponse.ok) throw new Error();
                const token = (await tokenResponse.json()).requestToken;
                if (typeof token !== "string" || !token) throw new Error();
                const response = await fetch("/api/library/export", {
                    ...options, method: "POST", headers: { "X-CSRF-TOKEN": token }
                });
                if (!response.ok) {
                    return JSON.stringify({ status: response.status, body: await response.text() });
                }
                const length = Number(response.headers.get("Content-Length"));
                if (response.status !== 200 ||
                    response.headers.get("Content-Type") !== "application/zip" ||
                    !Number.isSafeInteger(length) || length <= 0) throw new Error();
                // blob() rejects a broken transfer. Do not offer a partial file.
                const archive = await response.blob();
                if (archive.size !== length) throw new Error();
                release();
                downloadUrl = URL.createObjectURL(archive);
                const link = document.createElement("a");
                link.href = downloadUrl;
                link.download = "lexscholar-library.zip";
                document.body.appendChild(link);
                try { link.click(); } finally { link.remove(); }
                // Retain the URL until next export/page exit, avoiding a revoke
                // race with Firefox consuming the download.
                return "received";
            } catch {
                return JSON.stringify({ status: 0, body: "" });
            }
        }
    };
})();
