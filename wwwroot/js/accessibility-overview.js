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
                "The visual chart could not be loaded. " +
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
                    trendDataElement.dataset.fixFirst || "[]")
        };
    }
    catch (error) {
        console.error(
            "Accessibility trend data could not be parsed.",
            error);

        if (statusElement) {
            statusElement.textContent =
                "The visual chart could not be created. " +
                "The complete trend data is available in the table below.";
        }

        return;
    }

    const authenticatedCanvas =
        document.getElementById(
            "authenticatedAccessibilityTrendChart");

    if (!authenticatedCanvas) {
        return;
    }

    new Chart(
        authenticatedCanvas,
        {
            type: "line",

            data: {
                labels:
                    trendData.labels,

                datasets: [
                    {
                        label:
                            "States scanned",

                        data:
                            trendData.authenticatedStatesScanned,

                        borderColor:
                            "#0d6efd",

                        backgroundColor:
                            "rgba(13, 110, 253, 0.15)",

                        pointBackgroundColor:
                            "#0d6efd",

                        borderWidth:
                            2,

                        pointRadius:
                            3,

                        pointHoverRadius:
                            5,

                        tension:
                            0.15,

                        fill:
                            false
                    },
                    {
                        label:
                            "Violations",

                        data:
                            trendData.authenticatedFindings,

                        borderColor:
                            "#b02a37",

                        backgroundColor:
                            "rgba(176, 42, 55, 0.15)",

                        pointBackgroundColor:
                            "#b02a37",

                        borderWidth:
                            2,

                        pointRadius:
                            3,

                        pointHoverRadius:
                            5,

                        tension:
                            0.15,

                        fill:
                            false
                    },
                    {
                        label:
                            "Fix First",

                        data:
                            trendData.fixFirstFindings,

                        borderColor:
                            "#997404",

                        backgroundColor:
                            "rgba(153, 116, 4, 0.15)",

                        pointBackgroundColor:
                            "#997404",

                        borderWidth:
                            2,

                        pointRadius:
                            3,

                        pointHoverRadius:
                            5,

                        tension:
                            0.15,

                        fill:
                            false
                    }
                ]
            },

            options: {
                responsive:
                    true,

                maintainAspectRatio:
                    false,

                interaction: {
                    mode:
                        "index",

                    intersect:
                        false
                },

                plugins: {
                    legend: {
                        position:
                            "bottom",

                        labels: {
                            usePointStyle:
                                true,

                            padding:
                                18
                        }
                    },

                    tooltip: {
                        mode:
                            "index",

                        intersect:
                            false
                    }
                },

                scales: {
                    x: {
                        grid: {
                            display:
                                false
                        }
                    },

                    y: {
                        beginAtZero:
                            true,

                        ticks: {
                            precision:
                                0
                        },

                        title: {
                            display:
                                true,

                            text:
                                "Saved results"
                        }
                    }
                }
            }
        });
})();
