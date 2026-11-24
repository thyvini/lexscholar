// One synchronous compare-and-remove: a delayed result must never erase the
// marker of a different, newer confirmation.
window.lexScholarLibraryDeletion = {
    clearIfMatching(expected) {
        const key = "lexscholar.pending-deletion";
        const current = sessionStorage.getItem(key);
        if (current === null) return true;
        if (current !== expected) return false;
        sessionStorage.removeItem(key);
        return true;
    }
};
