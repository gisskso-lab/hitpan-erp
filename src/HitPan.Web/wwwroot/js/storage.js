window.hitpanStorage = {
    set: (key, value) => localStorage.setItem(key, value),
    get: (key) => localStorage.getItem(key),
    remove: (key) => localStorage.removeItem(key)
};

window.hitpanStorage_set = (key, value) => window.hitpanStorage.set(key, value);
window.hitpanStorage_get = (key) => window.hitpanStorage.get(key);
window.hitpanStorage_remove = (key) => window.hitpanStorage.remove(key);
