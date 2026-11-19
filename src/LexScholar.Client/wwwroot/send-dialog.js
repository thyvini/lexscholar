// The native modal owns background inertness, focus containment and Escape.
// Consent and cancellation remain in the existing Elmish state machine.
window.lexScholarSendDialog = (() => {
    let shown = null;
    return {
        sync() {
            const dialog = document.getElementById("send-confirmation");
            if (dialog) {
                if (shown !== dialog) {
                    // Keep Tab in the page as well as out of the inert workspace;
                    // otherwise browsers may visit their chrome at the boundary.
                    dialog.addEventListener("keydown", event => {
                        if (event.key !== "Tab") return;
                        const controls = dialog.querySelectorAll("a[href], button:not(:disabled)");
                        const first = controls[0], last = controls[controls.length - 1];
                        if (!event.shiftKey && document.activeElement === last) {
                            event.preventDefault();
                            first.focus();
                        } else if (event.shiftKey && document.activeElement === first) {
                            event.preventDefault();
                            last.focus();
                        }
                    });
                }
                if (!dialog.open) dialog.showModal();
                shown = dialog;
            } else if (shown) {
                shown = null;
                // The request control is recreated after cancellation. Do not
                // steal focus from navigation or from a newly running summary.
                if (document.activeElement === document.body) {
                    document.getElementById("summary-request")?.focus();
                }
            }
        }
    };
})();
