using System.Globalization;
using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Services;
using FastEndpoints;

namespace ClusterSharp.Api.Endpoints;

public class DashboardEndpoint(ClusterOverviewService overviewService, RequestStatsService requestStatsService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/dashboard");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Display cluster dashboard";
            s.Description = "Renders an HTML dashboard showing the current state of the cluster";
            s.Response<string>(200, "HTML content for the dashboard");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var overview = overviewService.Overview;
        var requestStats = requestStatsService.GetCurrentStats();

        string cpuDataPoints = "[]";
        string memoryDataPoints = "[]";
        string timeLabels = "[]";
        string requestsPerSecondDataPoints = "[]";
        string requestsTimeLabels = "[]";

        var currentTime = DateTime.Now.ToString("HH:mm:ss");

        if (overview?.Machines != null && overview.Machines.Any())
        {
            var avgCpu = (int)overview.Machines.Average(m => m.Cpu);
            var avgMemory = (int)overview.Machines.Average(m => m.Memory);
            
            cpuDataPoints = $"[{avgCpu.ToString(CultureInfo.InvariantCulture)}]";
            memoryDataPoints = $"[{avgMemory.ToString(CultureInfo.InvariantCulture)}]";
            timeLabels = $"['{currentTime}']";
        }

        
        requestsPerSecondDataPoints = $"[{requestStats.RequestsPerSecond.ToString(CultureInfo.InvariantCulture)}]";
        requestsTimeLabels = $"['{currentTime}']";

        var html = $@"
<!DOCTYPE html>
<html lang='en' class='h-100' data-bs-theme=""dark"">
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>ClusterSharp Dashboard</title>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css' rel='stylesheet'>
    <script src='https://unpkg.com/htmx.org@1.9.10'></script>
    <script src='https://cdn.jsdelivr.net/npm/chart.js'></script>
    <style>
        .card {{
            transition: all 0.3s;
        }}
        .card:hover {{
            transform: translateY(-5px);
            box-shadow: 0 10px 20px rgba(0,0,0,0.1);
        }}
        .container-card {{
            border-left: 4px solid #0d6efd;
        }}
        .machine-card {{
            border-left: 4px solid #198754;
        }}
        .timestamp {{
            font-size: 0.8rem;
            color: #6c757d;
        }}
        progress {{
            width: 100%;
            height: 20px;
            border-radius: 0.25rem;
            overflow: hidden;
        }}
        progress::-webkit-progress-bar {{
            background-color: #e9ecef;
        }}
        progress::-webkit-progress-value {{
            transition: width 0.3s ease;
        }}
        progress.bg-success::-webkit-progress-value {{
            background-color: #198754;
        }}
        progress.bg-warning::-webkit-progress-value {{
            background-color: #ffc107;
        }}
        progress.bg-danger::-webkit-progress-value {{
            background-color: #dc3545;
        }}
        progress.bg-success::-moz-progress-bar {{
            background-color: #198754;
        }}
        progress.bg-warning::-moz-progress-bar {{
            background-color: #ffc107;
        }}
        progress.bg-danger::-moz-progress-bar {{
            background-color: #dc3545;
        }}
        .progress-label {{
            margin-top: 5px;
            text-align: center;
        }}
        .chart-container {{
            height: 300px;
            margin-bottom: 20px;
        }}
    </style>
</head>
<body class='h-100 p-3 fs-6' hx-get='/dashboard' hx-trigger='every 1s' hx-swap='outerHTML' hx-on:after-swap='initCharts()'>        
    <div class='container-fluid h-100'>
        <div class='row mb-4'>
            <div class='col-md-6 col-12'>
                <div class='mb-3'>
                    <h2>Request Statistics</h2>
                </div>
                <div class='mb-3'>
                    <div class='card bg-dark-subtle shadow-sm'>
                        <div class='card-body'>
                            <div class='chart-container'>
                                <canvas id='requestsChart'></canvas>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            
            <div class='col-md-6 col-12'>
                <div class='mb-3'>
                    <h2>Cluster Resources</h2>
                </div>
                <div class='mb-3'>
                    <div class='card bg-dark-subtle shadow-sm'>
                        <div class='card-body'>
                            <div class='chart-container'>
                                <canvas id='resourceChart'></canvas>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
        
        <div class='row mb-3'>
            <div class='col-12 mb-3'>
                <h2>Machines</h2>
            </div>
            {(overview?.Machines != null ? string.Join("", overview.Machines.Select(machine => $@"
            <div class='col-xl-3 col-lg-4 col-md-6 col-sm-6 mb-3'>
                <div class='card bg-dark-subtle shadow-sm machine-card'>
                    <div class='card-body'>
                        <h5 class='card-title'>{machine.Hostname}</h5>
                        <div class='row'>
                            <div class='col-6'>
                                <p class='mb-1'>CPU</p>
                                <progress class='bg-{GetColorClass(machine.Cpu)}' 
                                          value='{machine.Cpu.ToString(CultureInfo.InvariantCulture)}' max='100'></progress>
                                <p class='progress-label'>{machine.Cpu.ToString(CultureInfo.InvariantCulture)}%</p>
                            </div>
                            <div class='col-6'>
                                <p class='mb-1'>Memory</p>
                                <progress class='bg-{GetColorClass(machine.Memory)}' 
                                          value='{machine.Memory.ToString(CultureInfo.InvariantCulture)}' max='100'></progress>
                                <p class='progress-label'>{machine.Memory.ToString(CultureInfo.InvariantCulture)}%</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>")) : "<div class='col-12'><div class='alert alert-warning'>No machine data available</div></div>")}
        </div>
        
        <div class='row mb-3'>
            <div class='col-12 mb-3'>
                <h2>Containers</h2>
            </div>
            {(overview?.Containers != null ? string.Join("", overview.Containers.Select(container => $@"
            <div class='col-xl-3 col-lg-4 col-md-6 col-sm-6 mb-3'>
                <div class='card bg-dark-subtle shadow-sm container-card'>
                    <div class='card-body'>
                        <h5 class='card-title'>{container.Name}</h5>
                        <div class='d-flex justify-content-between mb-2'>
                            <span class='badge bg-primary'>{container.Replicas} {(container.Replicas == 1 ? "replica" : "replicas")}</span>
                            {(string.IsNullOrEmpty(container.ExternalPort) ? "" : $"<span class='badge bg-success'>Port: {container.ExternalPort}</span>")}
                        </div>
                        <p class='card-text mb-1'>Hosts:</p>
                            {string.Join("", container.Hosts.Select(host => $"<span class=\"badge rounded-pill text-bg-secondary me-2\">{host}</span>"))}
                        <!-- Per-host container stats table -->
                        {(container.ContainerOnHostStatsList.Count > 0 ? $@"
                        <div class='mt-3'>
                            <table class='table table-sm table-dark table-bordered mb-0'>
                                <thead>
                                    <tr>
                                        <th scope='col'>Host</th>
                                        <th scope='col'>CPU (%)</th>
                                        <th scope='col'>Memory (%)</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {string.Join("", container.ContainerOnHostStatsList.Select(stats => $@"
                                        <tr>
                                            <td>{stats.Host}</td>
                                            <td><progress class='bg-{GetColorClass(stats.Cpu)}' value='{stats.Cpu.ToString(CultureInfo.InvariantCulture)}' max='100'></progress> {stats.Cpu.ToString(CultureInfo.InvariantCulture)}%</td>
                                            <td><progress class='bg-{GetColorClass(stats.Memory)}' value='{stats.Memory.ToString(CultureInfo.InvariantCulture)}' max='100'></progress> {stats.Memory.ToString(CultureInfo.InvariantCulture)}%</td>
                                        </tr>"))}
                                </tbody>
                            </table>
                        </div>" : "")}
                    </div>
                </div>
            </div>")) : "<div class='col-12'><div class='alert alert-warning'>No container data available</div></div>")}
        </div>
    </div>

    <script>
    (function() {{
            const newCpuData = {cpuDataPoints};
            const newMemoryData = {memoryDataPoints};
            const newTimeLabel = {timeLabels};
            const newRequestsData = {requestsPerSecondDataPoints};
            const newRequestsTimeLabel = {requestsTimeLabels};
            const MAX_DATA_POINTS = 30;
            let cpuData = [];
            let memoryData = [];
            let labels = [];
            let requestsData = [];
            let requestsLabels = [];
            try {{
                const storedCpuData = JSON.parse(localStorage.getItem('cpuData')) || [];
                const storedMemoryData = JSON.parse(localStorage.getItem('memoryData')) || [];
                const storedLabels = JSON.parse(localStorage.getItem('timeLabels')) || [];
                const storedRequestsData = JSON.parse(localStorage.getItem('requestsData')) || [];
                const storedRequestsLabels = JSON.parse(localStorage.getItem('requestsTimeLabels')) || [];
                
                if(newCpuData.length > 0 && newMemoryData.length > 0 && newTimeLabel.length > 0) {{
                    if(storedLabels.length === 0 || storedLabels[storedLabels.length-1] !== newTimeLabel[0]) {{
                        cpuData = [...storedCpuData, ...newCpuData].slice(-MAX_DATA_POINTS);
                        memoryData = [...storedMemoryData, ...newMemoryData].slice(-MAX_DATA_POINTS);
                        labels = [...storedLabels, ...newTimeLabel].slice(-MAX_DATA_POINTS);
                        localStorage.setItem('cpuData', JSON.stringify(cpuData));
                        localStorage.setItem('memoryData', JSON.stringify(memoryData));
                        localStorage.setItem('timeLabels', JSON.stringify(labels));
                    }} else {{
                        cpuData = storedCpuData;
                        memoryData = storedMemoryData;
                        labels = storedLabels;
                    }}
                }} else {{
                    cpuData = storedCpuData;
                    memoryData = storedMemoryData;
                    labels = storedLabels;
                }}
                
                if(newRequestsData.length > 0 && newRequestsTimeLabel.length > 0) {{
                    if(storedRequestsLabels.length === 0 || storedRequestsLabels[storedRequestsLabels.length-1] !== newRequestsTimeLabel[0]) {{
                        requestsData = [...storedRequestsData, ...newRequestsData].slice(-MAX_DATA_POINTS);
                        requestsLabels = [...storedRequestsLabels, ...newRequestsTimeLabel].slice(-MAX_DATA_POINTS);
                        localStorage.setItem('requestsData', JSON.stringify(requestsData));
                        localStorage.setItem('requestsTimeLabels', JSON.stringify(requestsLabels));
                    }} else {{
                        requestsData = storedRequestsData;
                        requestsLabels = storedRequestsLabels;
                    }}
                }} else {{
                    requestsData = storedRequestsData;
                    requestsLabels = storedRequestsLabels;
                }}
            }} catch (e) {{
                console.error('Error loading chart data:', e);
                localStorage.removeItem('cpuData');
                localStorage.removeItem('memoryData');
                localStorage.removeItem('timeLabels');
                localStorage.removeItem('requestsData');
                localStorage.removeItem('requestsTimeLabels');
            }}
            window.resourceChart = window.resourceChart || null;
            window.requestsChart = window.requestsChart || null;
            window.initCharts = function() {{
                if(window.resourceChart && typeof window.resourceChart.destroy === 'function') {{
                    window.resourceChart.destroy();
                    window.resourceChart = null;
                }}
                
                if(window.requestsChart && typeof window.requestsChart.destroy === 'function') {{
                    window.requestsChart.destroy();
                    window.requestsChart = null;
                }}
                
                const resourceCanvas = document.getElementById('resourceChart');
                if(resourceCanvas) {{
                    const resourceCtx = resourceCanvas.getContext('2d');
                    window.resourceChart = new Chart(resourceCtx, {{
                        type: 'line',
                        data: {{
                            labels: labels,
                            datasets: [
                                {{
                                    label: 'CPU Usage (%)',
                                    data: cpuData,
                                    borderColor: 'rgba(75, 192, 192, 1)',
                                    backgroundColor: 'rgba(75, 192, 192, 0.2)',
                                    borderWidth: 2,
                                    tension: 0.2,
                                    fill: true
                                }},
                                {{
                                    label: 'Memory Usage (%)',
                                    data: memoryData,
                                    borderColor: 'rgba(255, 99, 132, 1)',
                                    backgroundColor: 'rgba(255, 99, 132, 0.2)',
                                    borderWidth: 2,
                                    tension: 0.2,
                                    fill: true
                                }}
                            ]
                        }},
                        options: {{
                            responsive: true,
                            maintainAspectRatio: false,
                            scales: {{
                                y: {{
                                    beginAtZero: true,
                                    max: 100,
                                    title: {{
                                        display: true,
                                        text: 'Percentage (%)'
                                    }}
                                }}
                            }},
                            animation: {{
                                duration: 0
                            }}
                        }}
                    }});
                }}
                
                const requestsCanvas = document.getElementById('requestsChart');
                if(requestsCanvas) {{
                    const requestsCtx = requestsCanvas.getContext('2d');
                    window.requestsChart = new Chart(requestsCtx, {{
                        type: 'line',
                        data: {{
                            labels: requestsLabels,
                            datasets: [
                                {{
                                    label: 'Requests Per Second',
                                    data: requestsData,
                                    borderColor: 'rgba(54, 162, 235, 1)',
                                    backgroundColor: 'rgba(54, 162, 235, 0.2)',
                                    borderWidth: 2,
                                    tension: 0.2,
                                    fill: true
                                }}
                            ]
                        }},
                        options: {{
                            responsive: true,
                            maintainAspectRatio: false,
                            scales: {{
                                y: {{
                                    title: {{
                                        display: true,
                                        text: 'Requests/sec'
                                    }}
                                }}
                            }},
                            animation: {{
                                duration: 0
                            }}
                        }}
                    }});
                }}
            }};
            document.addEventListener('DOMContentLoaded', window.initCharts);
            window.initCharts();
        }})();
    </script>
</body>
</html>";

        await SendStringAsync(html, contentType: "text/html", cancellation: ct);
    }

    private string GetColorClass(double percentage)
    {
        return percentage switch
        {
            < 50 => "success",
            < 80 => "warning",
            _ => "danger"
        };
    }
} 