// Telegram Mini App bridge used from Blazor through IJSRuntime.
window.telegramApp = {
    getInitData: function () {
        return window.Telegram?.WebApp?.initData ?? "";
    },

    getStartParam: function (key) {
        const webApp = window.Telegram?.WebApp;
        const startParam = webApp?.initDataUnsafe?.start_param
            ?? new URLSearchParams(webApp?.initData || "").get("start_param");
        if (!startParam) {
            return null;
        }
        if (key) {
            // Only return parameters meant for this key ("expense_1500_Lunch").
            return startParam.startsWith(key + "_")
                ? startParam.substring(key.length + 1)
                : null;
        }
        return startParam;
    },

    getLanguageCode: function () {
        return window.Telegram?.WebApp?.initDataUnsafe?.user?.language_code ?? null;
    },

    isDarkTheme: function () {
        return window.Telegram?.WebApp?.colorScheme === "dark";
    },

    confirm: function (message) {
        const webApp = window.Telegram?.WebApp;
        // Native dialogs exist since Bot API 6.2; outside Telegram initData is empty.
        if (webApp?.initData && webApp.isVersionAtLeast?.("6.2")) {
            return new Promise(function (resolve) {
                webApp.showConfirm(message, function (ok) { resolve(!!ok); });
            });
        }
        return window.confirm(message);
    },

    ready: function () {
        const webApp = window.Telegram?.WebApp;
        if (!webApp) {
            return;
        }
        webApp.ready();
        webApp.expand();
    }
};

window.expensesUtils = {
    readFileContent: function (fileInputElement) {
        return new Promise(function (resolve, reject) {
            const files = fileInputElement.files;
            if (!files || files.length === 0) {
                resolve(null);
                return;
            }
            const reader = new FileReader();
            reader.onload = function (e) {
                resolve(e.target.result);
            };
            reader.onerror = function () {
                reject(new Error("Error reading file"));
            };
            reader.readAsText(files[0], "utf-8");
        });
    },

    downloadCsv: function (bytes, fileName) {
        const data = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
        const blob = new Blob([data], { type: "text/csv;charset=utf-8;" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        link.style.display = "none";
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    }
};
