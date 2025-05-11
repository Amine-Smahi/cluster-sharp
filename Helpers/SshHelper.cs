using System.Globalization;
using Renci.SshNet;
using ClusterSharp.Api.Models.Commands;
using ClusterSharp.Api.Models.Stats;
using ClusterSharp.Api.Shared;
using System.Text.RegularExpressions;

namespace ClusterSharp.Api.Helpers;

public static class SshHelper
{
    public static List<CommandResult>? ExecuteCommands(string hostname, IEnumerable<string> commands)
    {
        var clusterInfo = ClusterHelper.GetClusterSetup();
        if (clusterInfo == null)
            return null;
        
        var results = new List<CommandResult>();
        using var client = new SshClient(hostname, clusterInfo.Admin.Username, clusterInfo.Admin.Password);
        try
        {
            client.Connect();

            foreach (var command in commands)
            {
                var result = new CommandResult
                {
                    Command = command,
                    Status = Constants.NotOk
                };

                try
                {
                    var finalCommand = command;
                    if (command.StartsWith("sudo "))
                        finalCommand = $"echo '{clusterInfo.Admin.Password}' | sudo -S {command.Substring(5)}";

                    using var cmd = client.CreateCommand(finalCommand);
                    cmd.Execute();

                    result.Output = cmd.Result;
                    result.Error = cmd.Error;
                    result.Status = cmd.ExitStatus == 0 ? Constants.Ok : Constants.NotOk;
                }
                catch (Exception ex)
                {
                    result.Error = $"Exception: {ex.Message}";
                }

                results.Add(result);
            }
        }
        catch (Exception ex)
        {
            results.Add(new CommandResult
            {
                Command = "SSH Connection",
                Status = Constants.NotOk,
                Error = $"SSH connection error: {ex.Message}"
            });
        }
        finally
        {
            client.Disconnect();
        }


        return results;
    }

    public static List<ContainerStats>? GetDockerContainerStats(string hostname)
    {
        var clusterInfo = ClusterHelper.GetClusterSetup();
        if (clusterInfo == null)
            return null;
        
        var containers = new List<ContainerStats>();
        using var client = new SshClient(hostname, clusterInfo.Admin.Username, clusterInfo.Admin.Password);
        try
        {
            client.Connect();
            var statsCommand =
                "docker ps --format '{{.Names}}|{{.Ports}}' && " +
                "echo '---STATS_SEPARATOR---' && " +
                "docker stats --no-stream --format '{{.Name}}|{{.CPUPerc}}|{{.MemPerc}}' && " +
                "echo '---STATS_SEPARATOR---' && " +
                "docker ps -s --format '{{.Names}}|{{.Size}}'";

            using var batchCmd = client.CreateCommand(statsCommand);
            batchCmd.Execute();

            var sections = batchCmd.Result.Split("---STATS_SEPARATOR---", StringSplitOptions.RemoveEmptyEntries);
            if (sections.Length != 3)
                return containers;

            var portMap = new Dictionary<string, string>();
            foreach (var line in sections[0].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length == 2)
                {
                    portMap[parts[0].Trim()] = parts[1].Trim();
                }
            }

            var statsMap = new Dictionary<string, (double Cpu, double Memory)>();
            foreach (var line in sections[1].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length == 3)
                {
                    var name = parts[0].Trim();
                    double.TryParse(parts[1].TrimEnd('%'), CultureInfo.InvariantCulture, out var cpu);
                    double.TryParse(parts[2].TrimEnd('%'), CultureInfo.InvariantCulture, out var memory);
                    statsMap[name] = (cpu, memory);
                }
            }

            var diskMap = new Dictionary<string, string>();
            foreach (var line in sections[2].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length == 2)
                {
                    diskMap[parts[0].Trim()] = parts[1].Trim();
                }
            }

            foreach (var name in statsMap.Keys)
            {
                portMap.TryGetValue(name, out var portsString);
                diskMap.TryGetValue(name, out var diskUsage);

                var stats = statsMap[name];
                containers.Add(new ContainerStats
                {
                    Name = name,
                    Cpu = stats.Cpu,
                    Memory = stats.Memory,
                    Disk = diskUsage ?? "0B",
                    ExternalPort = ExtractFirstHostPort(portsString ?? string.Empty)
                });
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        finally
        {
            client.Disconnect();
        }
        return containers;
    }

    private static string ExtractFirstHostPort(string portsString)
    {
        if (string.IsNullOrWhiteSpace(portsString)) return string.Empty;

        var parts = portsString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var arrowIdx = trimmed.IndexOf("->", StringComparison.OrdinalIgnoreCase);
            if (arrowIdx > 0)
            {
                var left = trimmed.Substring(0, arrowIdx);
                var colonIdx = left.LastIndexOf(':');
                if (colonIdx >= 0 && colonIdx < left.Length - 1)
                {
                    var hostPort = left.Substring(colonIdx + 1);
                    if (int.TryParse(hostPort,  CultureInfo.InvariantCulture, out _))
                        return hostPort;
                }
            }
        }
        return string.Empty;
    }

    public static MachineStats? GetMachineStats(string hostname, string username, string password)
    {
        var clusterInfo = ClusterHelper.GetClusterSetup();
        if (clusterInfo == null)
            return null;
        
        using var client = new SshClient(hostname, username, password);
        client.Connect();
        try
        {
            var statsCommand =
                @"echo ""CPU $(LC_ALL=C top -bn1 | grep ""Cpu(s)"" | sed ""s/.*, *\([0-9.]*\)%* id.*/\1/"" | awk '{printf(""%.1f%%"", 100-$1)}')  RAM $(free -m | awk '/Mem:/ { printf(""%.1f%%"", $3/$2*100) }')  DISK $(df -h / | awk 'NR==2 {print $5}')"" ";
            using var cmd = client.CreateCommand(statsCommand);
            cmd.Execute();

            var result = cmd.Result.Trim();
            var stats = new MachineStats();

            if (!string.IsNullOrEmpty(result))
            {
                var cpuMatch = Regex.Match(result, @"CPU\s+(\d+\.\d+)%");
                if (cpuMatch.Success &&
                    double.TryParse(cpuMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var cpu))
                    stats.Cpu = cpu;

                var ramMatch = Regex.Match(result, @"RAM\s+(\d+\.\d+)%");
                if (ramMatch.Success &&
                    double.TryParse(ramMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var ram))
                    stats.Memory = ram;

                var diskMatch = Regex.Match(result, @"DISK\s+(\d+)%");
                if (diskMatch.Success &&
                    double.TryParse(diskMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var disk))
                    stats.Disk = disk;
            }

            return stats;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return null;
        }
        finally
        {
            client.Disconnect();
        }
    }
}