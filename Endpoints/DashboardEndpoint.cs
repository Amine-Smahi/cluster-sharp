using System.Globalization;
using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Services;
using FastEndpoints;

namespace ClusterSharp.Api.Endpoints;

public class DashboardEndpoint(ClusterOverviewService overviewService) : EndpointWithoutRequest
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

        var html = $@"
<!DOCTYPE html>
<html lang='en' data-bs-theme=""dark"">
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>ClusterSharp Dashboard</title>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css' rel='stylesheet'>
    <script src='https://unpkg.com/htmx.org@1.9.10'></script>
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
    </style>
</head>
<body class='h-100 p-3 fs-6' hx-get='/dashboard' hx-trigger='every 5s' hx-swap='outerHTML'>        
        <div class='row mb-3'>
            <div class='col-12 mb-3'>
                <div class='alert alert-info'>
                    <strong>YARP Routes:</strong> Last updated at {(YarpHelper.LastUpdateTime == DateTime.MinValue ? "Never" : YarpHelper.LastUpdateTime.ToString("yyyy-MM-dd HH:mm:ss"))}
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
                                <progress class='bg-{GetColorClass(machine.CpuPercent)}' 
                                          value='{machine.CpuPercent.ToString(CultureInfo.InvariantCulture)}' max='100'></progress>
                                <p class='progress-label'>{machine.CpuPercent.ToString(CultureInfo.InvariantCulture)}%</p>
                            </div>
                            <div class='col-6'>
                                <p class='mb-1'>Memory</p>
                                <progress class='bg-{GetColorClass(machine.MemoryPercent)}' 
                                          value='{machine.MemoryPercent.ToString(CultureInfo.InvariantCulture)}' max='100'></progress>
                                <p class='progress-label'>{machine.MemoryPercent.ToString(CultureInfo.InvariantCulture)}%</p>
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
                    </div>
                </div>
            </div>")) : "<div class='col-12'><div class='alert alert-warning'>No container data available</div></div>")}
        </div>
    </div>
</body>
</html>";

        await SendStringAsync(html, contentType: "text/html", cancellation: ct);
    }

    private string GetColorClass(double percent)
    {
        return percent switch
        {
            < 50 => "success",
            < 80 => "warning",
            _ => "danger"
        };
    }
} 