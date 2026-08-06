(function () {
    "use strict";

    const trendDataElement =
        document.getElementById("accessibility-trend-data");

    if (!trendDataElement) {
        return;
    }

    const statusElement =
        document.getElementById("accessibility-chart-status");

    if (typeof Chart === "undefined") {
        if (statusElement) {
            statusElement.textContent =
                "The visual charts could not be loaded. " +
                "The complete trend data is available in the table below.";
        }

        return;
    }

    let trendData;

    try {
        trendData = {
            labels:
                JSON.parse(
                    trendDataElement.dataset.labels || "[]"),

            authenticatedStatesScanned:
                JSON.parse(
                    trendDataElement.dataset.authenticatedStates || "[]"),

            authenticatedFindings:
                JSON.parse(
                    trendDataElement.dataset.authenticatedFindings || "[]"),

            fixFirstFindings:
                JSON.parse(
                    trendDataElement.dataset.fixFirst || "[]"),

            publicPagesScanned:
                JSON.parse(
                    trendDataElement.dataset.publicPages || "[]"),

            waveErrorsAndContrastErrors:
                JSON.parse(
                    trendDataElement.dataset.waveErrors || "[]")
        };
    }
    catch (error) {
        console.error(
            "Accessibility trend data could not be parsed.",
            error);

        if (statusElement) {
            statusElement.textContent =
                "The visual charts could not be created. " +
                "The complete trend data is available in the table below.";
        }

        return;
    }

    const commonOptions = {
        responsive: true,
        maintainAspectRatio: false,
        interaction: {
            mode: "index",
            intersect: false
        },
        plugins: {
            legend: {
                position: "bottom"
            }
        },
        scales: {
            y: {
                beginAtZero: true,
                ticks: {
                    precision: 0
                }
            }
        }
    };

    const authenticatedCanvas =
        document.getElementById(
            "authenticatedAccessibilityTrendChart");

    if (authenticatedCanvas) {
        new Chart(authenticatedCanvas, {
            type: "line",
            data: {
                labels: trendData.labels,
                datasets: [
                    {
                        label: "States scanned",
                        data:
                            trendData.authenticatedStatesScanned,
                        borderWidth: 2,
                        tension: 0.15
                    },
                    {
                        label: "Violations",
                        data:
                            trendData.authenticatedFindings,
                        borderWidth: 2,
                        tension: 0.15
                    },
                    {
                        label: "Fix First",
                        data:
                            trendData.fixFirstFindings,
                        borderWidth: 2,
                        tension: 0.15
                    }
                ]
            },
            options: commonOptions
        });
    }

    const publicCanvas =
        document.getElementById(
            "publicAccessibilityTrendChart");

    if (publicCanvas) {
        new Chart(publicCanvas, {
            type: "line",
            data: {
                labels: trendData.labels,
                datasets: [
                    {
                        label: "Public pages scanned",
                        data:
                            trendData.publicPagesScanned,
                        borderWidth: 2,
                        tension: 0.15
                    },
                    {
                        label: "WAVE errors and contrast",
                        data:
                            trendData.waveErrorsAndContrastErrors,
                        borderWidth: 2,
                        tension: 0.15
                    }
                ]
            },
            options: commonOptions
        });
    }
})();
