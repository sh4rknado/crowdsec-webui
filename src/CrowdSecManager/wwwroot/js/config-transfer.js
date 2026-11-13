(function () {
    "use strict";
    window.configTransfer = {
        download: function (fileName, bytes) {
            const blob = new Blob([bytes], { type: "application/zip" });
            const url = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = url;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);
        }
    };
})();
